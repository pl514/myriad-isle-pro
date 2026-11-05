using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 形变外壳：一块纯视觉的圆角板，从 A 矩形变到 B 矩形。
    //
    // 为什么需要它：抖音是 WebView2 子窗口，没法跟胶囊的 WPF 动画一起变形。
    // 所以打开时"面板 → 抖音"、退出时"抖音 → 胶囊"这两段运动，
    // 都由这个外壳来演，真窗口在旁边等着被揭开。
    //
    // 它是一次性的：演完就自己关掉，不留任何常驻窗口，
    // 也就不存在"底板露边""挡住点击"这类问题。
    // ---------------------------------------------------------------------
    internal sealed class MorphShellWindow : Window
    {
        static readonly Color TopColor = Color.FromArgb(242, 38, 44, 51);
        static readonly Color BottomColor = Color.FromArgb(240, 27, 33, 41);
        const double CornerRadiusPx = 28;
        const double ShadowMargin = 56;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hwnd, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        Border card;
        Rect bounds;

        /// <summary>播放一次形变。屏幕坐标，单位是 DIP。</summary>
        public static void Play(Rect from, Rect to, int durationMs, Action onArrived, Action onFinished)
        {
            try
            {
                var shell = new MorphShellWindow();
                shell.Run(from, to, durationMs, onArrived, onFinished);
            }
            catch (Exception)
            {
                if (onArrived != null) { try { onArrived(); } catch (Exception) { } }
                if (onFinished != null) { try { onFinished(); } catch (Exception) { } }
            }
        }

        MorphShellWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;
        }

        void Run(Rect from, Rect to, int durationMs, Action onArrived, Action onFinished)
        {
            bounds = Rect.Union(from, to);
            bounds.Inflate(ShadowMargin, ShadowMargin);
            Left = bounds.Left;
            Top = bounds.Top;
            Width = Math.Max(1, bounds.Width);
            Height = Math.Max(1, bounds.Height);

            card = new Border
            {
                CornerRadius = new CornerRadius(CornerRadiusPx),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(TopColor, BottomColor, new Point(0, 0), new Point(0, 1)),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.42, Color = Colors.Black },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(card, from.Left - bounds.Left);
            Canvas.SetTop(card, from.Top - bounds.Top);
            card.Width = Math.Max(1, from.Width);
            card.Height = Math.Max(1, from.Height);

            var canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            canvas.Children.Add(card);
            Content = canvas;

            SourceInitialized += delegate
            {
                // 整个窗口鼠标穿透，纯视觉
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, -20);
                SetWindowLong(hwnd, -20, style | 0x00000020 | 0x08000000);
            };

            Show();

            var duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(80, durationMs)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            double fromLeft = from.Left - bounds.Left;
            double fromTop = from.Top - bounds.Top;
            double toLeft = to.Left - bounds.Left;
            double toTop = to.Top - bounds.Top;

            card.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(from.Width, to.Width, duration) { EasingFunction = ease });
            card.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(from.Height, to.Height, duration) { EasingFunction = ease });
            card.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(fromLeft, toLeft, duration) { EasingFunction = ease });
            card.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(fromTop, toTop, duration) { EasingFunction = ease });

            var arrive = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(80, durationMs)) };
            arrive.Tick += delegate
            {
                arrive.Stop();
                try { if (onArrived != null) onArrived(); } catch (Exception) { }

                // 到位后淡出，露出底下已经就位的真窗口
                var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(130))) { EasingFunction = ease };
                fade.Completed += delegate
                {
                    try { Close(); } catch (Exception) { }
                    if (onFinished != null) { try { onFinished(); } catch (Exception) { } }
                };
                BeginAnimation(UIElement.OpacityProperty, fade);
            };
            arrive.Start();
        }
    }
}
