using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandShot
{
    // 外部红果客户端没有标题栏时我们自己补一条：拖动 / 收起 / 关闭。
    // 它是独立的置顶小窗，始终贴在那个客户端窗口的上沿。
    internal sealed class HongguoChromeWindow : Window
    {
        readonly Action onHide;
        readonly Action onClose;
        readonly DispatcherTimer followTimer;

        bool dragging;
        double dragDpi = 1.0, dragCursorX, dragCursorY, dragStartLeft, dragStartTop;

        static double Dpi()
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1.0, g.DpiX / 96.0);
            }
            catch (Exception) { return 1.0; }
        }

        public HongguoChromeWindow(Action hide, Action close)
        {
            onHide = hide;
            onClose = close;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var bar = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(238, 22, 24, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1, 1, 1, 0)
            };
            var grid = new Grid();

            var dragZone = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.SizeAll };
            dragZone.MouseLeftButtonDown += DragDown;
            dragZone.MouseMove += DragMoveHandler;
            dragZone.MouseLeftButtonUp += DragUp;
            grid.Children.Add(dragZone);

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsHitTestVisible = false };
            titleRow.Children.Add(new TextBlock
            {
                Text = "红果短剧",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 236, 246)),
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(titleRow);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            buttons.Children.Add(BuildButton(false, "收起（再点小球回来）", delegate { if (onHide != null) onHide(); }));
            buttons.Children.Add(BuildButton(true, "关闭", delegate { if (onClose != null) onClose(); }));
            grid.Children.Add(buttons);

            bar.Child = grid;
            Content = bar;

            followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            followTimer.Tick += delegate { Follow(); };
            Loaded += delegate { followTimer.Start(); };
            Closed += delegate { followTimer.Stop(); };

            Width = 720;
        }

        Border BuildButton(bool close, string tip, Action click)
        {
            var icon = new Grid { Width = 12, Height = 12, IsHitTestVisible = false };
            if (close)
            {
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(-45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }

            var button = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = icon
            };
            button.MouseEnter += delegate { button.Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate { click(); };
            return button;
        }

        // 贴上外部窗口的上沿
        public void AttachTo(Rect target)
        {
            Width = Math.Max(240, target.Width);
            Height = 28;
            Left = target.X;
            Top = target.Y;
            if (!IsVisible) Show();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        void BringToFront()
        {
            try
            {
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);   // HWND_TOPMOST, NOSIZE|NOMOVE
            }
            catch (Exception) { }
        }

        void Follow()
        {
            try
            {
                IntPtr hwnd = HongguoAppOverlay.Handle;
                if (hwnd == IntPtr.Zero) { Hide(); return; }
                if (!HongguoAppOverlay.IsShown) { if (IsVisible) Hide(); return; }

                Rect rect = HongguoAppOverlay.CurrentRect();
                double targetWidth = Math.Max(240, rect.Width);
                if (Math.Abs(Left - rect.X) > 0.5 || Math.Abs(Top - rect.Y) > 0.5
                    || Math.Abs(Width - targetWidth) > 0.5)
                {
                    Left = rect.X;
                    Top = rect.Y;
                    Width = targetWidth;
                }
                if (!IsVisible) Show();
                BringToFront();
            }
            catch (Exception) { }
        }

        void DragDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            dragging = true;
            dragDpi = Dpi();
            dragCursorX = System.Windows.Forms.Cursor.Position.X / dragDpi;
            dragCursorY = System.Windows.Forms.Cursor.Position.Y / dragDpi;
            dragStartLeft = Left;
            dragStartTop = Top;
            ((UIElement)sender).CaptureMouse();
        }

        void DragMoveHandler(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { dragging = false; return; }
            double left = dragStartLeft + (System.Windows.Forms.Cursor.Position.X / dragDpi - dragCursorX);
            double top = dragStartTop + (System.Windows.Forms.Cursor.Position.Y / dragDpi - dragCursorY);
            Left = left;
            Top = top;
            HongguoAppOverlay.SetRect(new Rect(left, top, Width, HongguoAppOverlay.CurrentRect().Height));
        }

        void DragUp(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            try { ((UIElement)sender).ReleaseMouseCapture(); } catch (Exception) { }
            HongguoAppOverlay.SaveRect();
        }
    }
}
