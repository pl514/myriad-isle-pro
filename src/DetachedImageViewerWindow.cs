using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IslandShot
{
    /// <summary>
    /// 从胶囊拖出的图片：默认小尺寸、白框、可移动、可缩放、可平移、可改大小。
    /// </summary>
    internal sealed class DetachedImageViewerWindow : Window
    {
        readonly Border frame;
        readonly Grid viewport;
        readonly Image picture;
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        readonly Border closeButton;
        readonly Border resizeGrip;
        readonly ScaleTransform closeScale = new ScaleTransform(1, 1);
        readonly TranslateTransform closeMove = new TranslateTransform();
        readonly Rect? returnScreen;
        readonly Action closedAction;
        bool closing;
        readonly DispatcherTimer controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        double dpi = 1;
        double startWidth, startHeight, startLeft, startTop, startPanX, startPanY;
        System.Drawing.Point startCursor;
        bool moving, panning, resizing;
        int resizeDir;

        public DetachedImageViewerWindow(string path, Rect? returnScreen = null, Action closedAction = null)
        {
            this.returnScreen = returnScreen;
            this.closedAction = closedAction;
            BitmapSource source = ViewerImageLoader.Load(path);
            double width = Math.Min(260, source.PixelWidth);
            double height = width * source.PixelHeight / Math.Max(1, source.PixelWidth);
            if (height > 320)
            {
                height = 320;
                width = height * source.PixelWidth / Math.Max(1, source.PixelHeight);
            }
            Width = Math.Max(120, width + 4);
            Height = Math.Max(84, height + 4);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            Title = "Capsule image Pro · " + System.IO.Path.GetFileName(path);

            frame = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 214, 245)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Opacity = 0.24 }
            };
            viewport = new Grid { ClipToBounds = true, Background = Brushes.White };
            picture = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var transforms = new TransformGroup();
            transforms.Children.Add(zoom);
            transforms.Children.Add(pan);
            picture.RenderTransform = transforms;
            viewport.Children.Add(picture);
            frame.Child = viewport;

            var closeGroup = new TransformGroup();
            closeGroup.Children.Add(closeScale);
            closeGroup.Children.Add(closeMove);
            frame.RenderTransform = closeGroup;
            frame.RenderTransformOrigin = new Point(0.5, 0.5);

            var root = new Grid();
            root.Children.Add(frame);
            AddResizeHandles(root);

            closeButton = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(220, 24, 32, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(7),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            closeButton.MouseLeftButtonDown += delegate { BeginClose(); };
            root.Children.Add(closeButton);

            resizeGrip = new Border
            {
                Width = 26,
                Height = 26,
                Background = new SolidColorBrush(Color.FromArgb(190, 0, 143, 174)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 214, 245)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(7),
                Child = new Path
                {
                    Data = Geometry.Parse("M 3 17 L 17 3 M 9 17 L 17 9"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                }
            };
            root.Children.Add(resizeGrip);
            Content = root;

            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "复制图片" };
            copy.Click += delegate { try { Clipboard.SetImage(source); } catch (Exception) { } };
            var close = new MenuItem { Header = "关闭图片" };
            close.Click += delegate { BeginClose(); };
            menu.Items.Add(copy);
            menu.Items.Add(close);
            frame.ContextMenu = menu;

            frame.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (zoom.ScaleX > 1.001) return;
                try { DragMove(); } catch (Exception) { }
            };
            viewport.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    zoom.ScaleX = 1;
                    zoom.ScaleY = 1;
                    pan.X = 0;
                    pan.Y = 0;
                    e.Handled = true;
                    return;
                }
                if (zoom.ScaleX <= 1.001) return;
                panning = true;
                startCursor = System.Windows.Forms.Cursor.Position;
                startPanX = pan.X;
                startPanY = pan.Y;
                viewport.CaptureMouse();
                e.Handled = true;
            };
            viewport.MouseMove += delegate
            {
                if (!panning) return;
                var cursor = System.Windows.Forms.Cursor.Position;
                pan.X = startPanX + (cursor.X - startCursor.X) / dpi;
                pan.Y = startPanY + (cursor.Y - startCursor.Y) / dpi;
                ClampPan();
            };
            viewport.MouseLeftButtonUp += delegate
            {
                if (!panning) return;
                panning = false;
                viewport.ReleaseMouseCapture();
            };
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                double next = Math.Max(1, Math.Min(8, zoom.ScaleX * Math.Pow(1.15, e.Delta / 120.0)));
                zoom.ScaleX = next;
                zoom.ScaleY = next;
                if (next <= 1.001)
                {
                    pan.X = 0;
                    pan.Y = 0;
                }
                else
                {
                    ClampPan();
                }
                e.Handled = true;
            };
            resizeGrip.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                resizing = true;
                resizeDir = 15;
                startCursor = System.Windows.Forms.Cursor.Position;
                startWidth = Width;
                startHeight = Height;
                resizeGrip.CaptureMouse();
            };
            resizeGrip.MouseMove += delegate
            {
                if (!resizing) return;
                var cursor = System.Windows.Forms.Cursor.Position;
                double newWidth = Math.Max(120, Math.Min(SystemParameters.WorkArea.Width - 12, startWidth + (cursor.X - startCursor.X) / dpi));
                double newHeight = Math.Max(84, newWidth * startHeight / Math.Max(1, startWidth));
                if (newHeight > SystemParameters.WorkArea.Height - 12) return;
                Width = newWidth;
                Height = newHeight;
                ClampPan();
            };
            resizeGrip.MouseLeftButtonUp += delegate
            {
                if (!resizing) return;
                resizing = false;
                resizeGrip.ReleaseMouseCapture();
            };

            controlsTimer.Tick += delegate { controlsTimer.Stop(); HideControls(); };
            MouseMove += delegate
            {
                Point at = Mouse.GetPosition(this);
                bool nearControls = (at.X <= 60 && at.Y <= 60) || (at.X >= ActualWidth - 70 && at.Y >= ActualHeight - 70);
                if (nearControls) ShowControls();
                else if (closeButton.Opacity > 0 && !resizing) ScheduleHide();
            };
            Loaded += delegate
            {
                dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                Left = Math.Max(SystemParameters.WorkArea.Left, Math.Min(SystemParameters.WorkArea.Right - Width, cursor.X / dpi - Width / 2));
                Top = Math.Max(SystemParameters.WorkArea.Top, Math.Min(SystemParameters.WorkArea.Bottom - Height, cursor.Y / dpi - Height / 2));
                ShowControls();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) BeginClose(); };
        }

        void AddResizeHandles(Grid root)
        {
            for (int direction = 1; direction <= 15; direction++)
            {
                int edges = direction;
                bool left = (edges & 1) != 0, right = (edges & 2) != 0;
                bool top = (edges & 4) != 0, bottom = (edges & 8) != 0;
                if ((left && right) || (top && bottom)) continue;
                var handle = new Border
                {
                    Background = Brushes.Transparent,
                    Tag = edges,
                    HorizontalAlignment = left ? HorizontalAlignment.Left : (right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch),
                    VerticalAlignment = top ? VerticalAlignment.Top : (bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch),
                    Width = left || right ? 9 : double.NaN,
                    Height = top || bottom ? 9 : double.NaN,
                    Cursor = left && top || right && bottom ? Cursors.SizeNWSE : (right && top || left && bottom ? Cursors.SizeNESW : (left || right ? Cursors.SizeWE : Cursors.SizeNS))
                };
                handle.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    resizing = true;
                    resizeDir = (int)((Border)sender).Tag;
                    startCursor = System.Windows.Forms.Cursor.Position;
                    startLeft = Left;
                    startTop = Top;
                    startWidth = Width;
                    startHeight = Height;
                    ((Border)sender).CaptureMouse();
                };
                handle.MouseMove += delegate
                {
                    if (!resizing) return;
                    var cursor = System.Windows.Forms.Cursor.Position;
                    double dx = (cursor.X - startCursor.X) / dpi;
                    double dy = (cursor.Y - startCursor.Y) / dpi;
                    const double minW = 120, minH = 84;
                    double newLeft = startLeft, newTop = startTop, width = startWidth, height = startHeight;
                    if ((resizeDir & 1) != 0) { newLeft = Math.Min(startLeft + dx, startLeft + startWidth - minW); width = startWidth - (newLeft - startLeft); }
                    if ((resizeDir & 2) != 0) width = Math.Max(minW, startWidth + dx);
                    if ((resizeDir & 4) != 0) { newTop = Math.Min(startTop + dy, startTop + startHeight - minH); height = startHeight - (newTop - startTop); }
                    if ((resizeDir & 8) != 0) height = Math.Max(minH, startHeight + dy);
                    width = Math.Min(width, SystemParameters.WorkArea.Width - 8);
                    height = Math.Min(height, SystemParameters.WorkArea.Height - 8);
                    newLeft = Math.Max(SystemParameters.WorkArea.Left, Math.Min(newLeft, SystemParameters.WorkArea.Right - width));
                    newTop = Math.Max(SystemParameters.WorkArea.Top, Math.Min(newTop, SystemParameters.WorkArea.Bottom - height));
                    Left = newLeft;
                    Top = newTop;
                    Width = width;
                    Height = height;
                    ClampPan();
                };
                handle.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    resizing = false;
                    ((Border)sender).ReleaseMouseCapture();
                };
                root.Children.Add(handle);
            }
        }

        void ClampPan()
        {
            double maxX = Math.Max(0, (viewport.ActualWidth * zoom.ScaleX - viewport.ActualWidth) / 2);
            double maxY = Math.Max(0, (viewport.ActualHeight * zoom.ScaleY - viewport.ActualHeight) / 2);
            pan.X = Math.Max(-maxX, Math.Min(maxX, pan.X));
            pan.Y = Math.Max(-maxY, Math.Min(maxY, pan.Y));
        }

        // 关闭时先由大变小飞回胶囊，再真正关窗；胶囊同时被点亮接住它。
        void BeginClose()
        {
            if (closing) return;
            closing = true;
            if (closedAction != null)
            {
                try { closedAction(); } catch (Exception) { }
            }
            if (!returnScreen.HasValue)
            {
                Close();
                return;
            }

            // 关闭动画只缩内容，顺手把按钮和缩放手柄藏掉，免得它们原地“飘”着。
            closeButton.Visibility = Visibility.Collapsed;
            resizeGrip.Visibility = Visibility.Collapsed;

            // 和左键单击放大后关闭的收回动画完全一致：190ms 缩到胶囊那一格，不额外淡出。
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(190);
            double dpi = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null) dpi = source.CompositionTarget.TransformToDevice.M11;
            double capsuleWidth = Math.Max(8, returnScreen.Value.Width / dpi);
            double capsuleHeight = Math.Max(8, returnScreen.Value.Height / dpi);
            double targetX = (returnScreen.Value.X + returnScreen.Value.Width / 2) / dpi;
            double targetY = (returnScreen.Value.Y + returnScreen.Value.Height / 2) / dpi;
            closeMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(targetX - (Left + Width / 2), time) { EasingFunction = ease });
            closeMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(targetY - (Top + Height / 2), time) { EasingFunction = ease });
            closeScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(capsuleWidth / Math.Max(1, Width), time) { EasingFunction = ease });
            closeScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(capsuleHeight / Math.Max(1, Height), time) { EasingFunction = ease });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += delegate { timer.Stop(); try { Close(); } catch (Exception) { } };
            timer.Start();
        }

        void ShowControls()
        {
            closeButton.Opacity = 1;
            resizeGrip.Opacity = 1;
            closeButton.IsHitTestVisible = true;
            resizeGrip.IsHitTestVisible = true;
            ScheduleHide();
        }

        void ScheduleHide()
        {
            controlsTimer.Stop();
            controlsTimer.Start();
        }

        void HideControls()
        {
            if (resizing) return;
            closeButton.Opacity = 0;
            resizeGrip.Opacity = 0;
            closeButton.IsHitTestVisible = false;
            resizeGrip.IsHitTestVisible = false;
        }
    }
}
