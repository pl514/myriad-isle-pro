using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace IslandShot
{
    /// <summary>
    /// 一张图片从屏幕上的 A 矩形“飞”到 B 矩形（胶囊或某张卡片），落地后回调。
    /// 整块窗口点击穿透、不抢焦点，所以飞行途中不会挡住鼠标和键盘操作。
    /// 同一时刻只保留一张飞行图，后一张会先让前一张落地。
    /// </summary>
    internal sealed class FlyImageWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        const int GwlExStyle = -20;
        const int WsExTransparent = 0x00000020;
        const int WsExToolWindow = 0x00000080;
        const int WsExNoActivate = 0x08000000;

        static FlyImageWindow current;

        readonly Border shell;
        readonly Rect start;
        readonly Rect end;
        readonly int duration;
        Action landed;
        bool done;

        public static void Fly(BitmapSource image, Rect fromScreen, Rect toScreen, int durationMs, Action landed)
        {
            if (image == null)
            {
                Invoke(landed);
                return;
            }
            try
            {
                if (current != null)
                {
                    FlyImageWindow previous = current;
                    current = null;
                    try { previous.Finish(); } catch (Exception) { }
                }
                var window = new FlyImageWindow(image, fromScreen, toScreen, durationMs, landed);
                current = window;
                window.Show();
            }
            catch (Exception)
            {
                Invoke(landed);
            }
        }

        static void Invoke(Action action)
        {
            if (action == null) return;
            try { action(); } catch (Exception) { }
        }

        FlyImageWindow(BitmapSource image, Rect fromScreen, Rect toScreen, int durationMs, Action landed)
        {
            this.landed = landed;
            start = fromScreen;
            end = toScreen;
            duration = Math.Max(160, durationMs);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            IsHitTestVisible = false;
            Focusable = false;
            Topmost = true;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            try { brush.Freeze(); } catch (Exception) { }

            shell = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = brush,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.45 },
                Opacity = 0,
                IsHitTestVisible = false
            };
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(shell);
            var root = new Grid { IsHitTestVisible = false, Background = Brushes.Transparent };
            root.Children.Add(canvas);
            Content = root;

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(this).Handle;
                    int style = GetWindowLong(handle, GwlExStyle);
                    SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
                }
                catch (Exception) { }
            };
            Loaded += delegate { Play(); };
        }

        void Play()
        {
            double offsetX = SystemParameters.VirtualScreenLeft;
            double offsetY = SystemParameters.VirtualScreenTop;

            Rect from = new Rect(start.X - offsetX, start.Y - offsetY, start.Width, start.Height);
            Rect to = new Rect(end.X - offsetX, end.Y - offsetY, end.Width, end.Height);
            if (from.Width < 2 || from.Height < 2) from = new Rect(to.X, to.Y, Math.Max(2, to.Width), Math.Max(2, to.Height));

            Canvas.SetLeft(shell, from.X);
            Canvas.SetTop(shell, from.Y);
            shell.Width = Math.Max(2, from.Width);
            shell.Height = Math.Max(2, from.Height);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(duration);
            Animate(Canvas.LeftProperty, from.X, to.X, time, ease);
            Animate(Canvas.TopProperty, from.Y, to.Y, time, ease);
            Animate(FrameworkElement.WidthProperty, Math.Max(2, from.Width), Math.Max(2, to.Width), time, ease);
            Animate(FrameworkElement.HeightProperty, Math.Max(2, from.Height), Math.Max(2, to.Height), time, ease);

            // 出现时快速淡入，接近目标时再淡出，避免“啪”一声盖住卡片。
            var fade = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(duration + 130)),
                FillBehavior = FillBehavior.HoldEnd
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), ease));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(time), ease));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration + 120))));
            fade.Completed += delegate { Finish(); };
            shell.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        void Animate(DependencyProperty property, double from, double to, TimeSpan time, IEasingFunction ease)
        {
            var animation = new DoubleAnimation(from, to, new Duration(time))
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd
            };
            shell.BeginAnimation(property, animation);
        }

        void Finish()
        {
            if (done) return;
            done = true;
            if (current == this) current = null;
            Action callback = landed;
            landed = null;
            try { Close(); } catch (Exception) { }
            Invoke(callback);
        }
    }
}