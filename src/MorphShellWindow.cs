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
    //
    // 生命周期由调用方控制：
    //   Create(...)   → 显示并开始形变
    //   到位时回调 onArrived（这时通常去揭开真窗口）
    //   调用 Dismiss() → 淡出并销毁
    //
    // 之所以不让它"到位就自己淡出"：页面还没画出第一帧时淡出会露出桌面，
    // 所以由调用方等真窗口就绪后再叫它消失。
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
        bool dismissed;

        public static MorphShellWindow Create(Rect from, Rect to, int durationMs, Action onArrived)
        {
            try
            {
                var shell = new MorphShellWindow();
                shell.Run(from, to, durationMs, onArrived);
                return shell;
            }
            catch (Exception)
            {
                if (onArrived != null) { try { onArrived(); } catch (Exception) { } }
                return null;
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

        void Run(Rect from, Rect to, int durationMs, Action onArrived)
        {
            Rect bounds = Rect.Union(from, to);
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

            card.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(from.Width, to.Width, duration) { EasingFunction = ease });
            card.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(from.Height, to.Height, duration) { EasingFunction = ease });
            card.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.Left - bounds.Left, to.Left - bounds.Left, duration) { EasingFunction = ease });
            card.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from.Top - bounds.Top, to.Top - bounds.Top, duration) { EasingFunction = ease });

            var arrive = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(80, durationMs)) };
            arrive.Tick += delegate
            {
                arrive.Stop();
                try { if (onArrived != null) onArrived(); } catch (Exception) { }
            };
            arrive.Start();
        }

        /// <summary>淡出并销毁。调用方在真窗口就绪后调用。</summary>
        public void Dismiss()
        {
            if (dismissed) return;
            dismissed = true;
            try
            {
                var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(130)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fade.Completed += delegate
                {
                    try { Close(); } catch (Exception) { }
                };
                BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception)
            {
                try { Close(); } catch (Exception) { }
            }
        }
    }
}
