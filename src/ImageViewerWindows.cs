using System;
using System.IO;
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
    internal static class ViewerImageLoader
    {
        public static BitmapSource Load(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    /// <summary>单击卡片的相框放大。</summary>
    internal sealed class CardZoomWindow : Window
    {
        readonly Border card;
        readonly Canvas canvas;
        readonly Rect sourceScreen;
        readonly Rect returnScreen;
        readonly DispatcherTimer closing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        readonly string path;
        readonly Action closedAction;
        bool shrinking;
        bool returned;
        DateTime openedAt;

        public CardZoomWindow(string path, Rect source, Action closedAction = null, Rect? returnScreen = null)
        {
            this.path = path;
            this.closedAction = closedAction;
            sourceScreen = source;
            this.returnScreen = returnScreen.HasValue ? returnScreen.Value : source;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            BitmapSource picture = ViewerImageLoader.Load(path);
            double scale = Math.Min(780.0 / picture.PixelWidth, 540.0 / picture.PixelHeight);
            scale = Math.Min(scale, Math.Min(Width * 0.70 / picture.PixelWidth, Height * 0.70 / picture.PixelHeight));
            double targetWidth = picture.PixelWidth * scale + 14;
            double targetHeight = picture.PixelHeight * scale + 14;

            card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 15, 21)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 217, 245)),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(3),
                Child = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(4),
                    Child = new Image { Source = picture, Stretch = Stretch.Uniform }
                },
                Effect = new DropShadowEffect { Color = Color.FromRgb(56, 217, 245), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.6 }
            };
            canvas = new Canvas();
            canvas.Children.Add(card);
            var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            root.Children.Add(canvas);
            Content = root;

            Loaded += delegate
            {
                Point origin = PointFromScreen(new Point(sourceScreen.X, sourceScreen.Y));
                Canvas.SetLeft(card, origin.X);
                Canvas.SetTop(card, origin.Y);
                card.Width = Math.Max(2, sourceScreen.Width);
                card.Height = Math.Max(2, sourceScreen.Height);
                double targetLeft = (Width - targetWidth) / 2;
                double targetTop = (Height - targetHeight) / 2;
                Animate(origin.X, origin.Y, card.Width, card.Height,
                    targetLeft, targetTop, targetWidth, targetHeight, 220);
                closing.Tick += delegate { closing.Stop(); Close(); };
                openedAt = DateTime.UtcNow;
                Focus();
            };
            root.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Point screen = PointToScreen(e.GetPosition(root));
                Rect near = sourceScreen;
                near.Inflate(30, 30);
                bool quickSecond = openedAt != DateTime.MinValue
                    && (DateTime.UtcNow - openedAt).TotalMilliseconds <= 600
                    && near.Contains(screen);
                if (e.ClickCount == 2 || quickSecond)
                {
                    try { new FullImageViewerWindow(path, closedAction, returnScreen).Show(); } catch (Exception) { }
                    Close();
                    e.Handled = true;
                    return;
                }
                if (!card.IsMouseOver) Shrink();
            };
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                bool quickSecond = openedAt != DateTime.MinValue && (DateTime.UtcNow - openedAt).TotalMilliseconds <= 600;
                if (e.ClickCount == 2 || quickSecond)
                {
                    try { new FullImageViewerWindow(path, closedAction, returnScreen).Show(); } catch (Exception) { }
                    Close();
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Shrink();
            };
        }

        void Animate(double l, double t, double w, double h, double toL, double toT, double toW, double toH, double ms)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(ms);
            card.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(toL, time) { EasingFunction = ease });
            card.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(toT, time) { EasingFunction = ease });
            card.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(toW, time) { EasingFunction = ease });
            card.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(toH, time) { EasingFunction = ease });
        }

        void Shrink()
        {
            if (shrinking) return;
            shrinking = true;
            if (!returned && closedAction != null) { returned = true; closedAction(); }
            Point origin = PointFromScreen(new Point(returnScreen.X, returnScreen.Y));
            Animate(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight,
                origin.X, origin.Y, Math.Max(8, returnScreen.Width), Math.Max(8, returnScreen.Height), 190);
            closing.Start();
        }
    }

    /// <summary>双击卡片的完整查看：滚轮缩放、拖动平移、Esc/点空白关闭。</summary>
    internal sealed class FullImageViewerWindow : Window
    {
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        readonly Border card;
        readonly TextBlock caption;
        readonly ScaleTransform closeScale = new ScaleTransform(1, 1);
        readonly TranslateTransform closeMove = new TranslateTransform();
        readonly Rect? returnScreen;
        readonly Action closedAction;
        bool moving;
        bool closing;
        Point moveFrom;

        public FullImageViewerWindow(string path, Action closedAction = null, Rect? returnScreen = null)
        {
            this.closedAction = closedAction;
            this.returnScreen = returnScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            BitmapSource source = ViewerImageLoader.Load(path);
            var picture = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var group = new TransformGroup();
            group.Children.Add(zoom);
            group.Children.Add(pan);
            picture.RenderTransform = group;

            caption = new TextBlock
            {
                Text = System.IO.Path.GetFileName(path) + "   " + source.PixelWidth + " × " + source.PixelHeight,
                Foreground = new SolidColorBrush(Color.FromRgb(225, 235, 245)),
                Background = new SolidColorBrush(Color.FromArgb(190, 8, 12, 18)),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var layout = new Grid();
            layout.Children.Add(picture);
            layout.Children.Add(caption);
            var closeGroup = new TransformGroup();
            closeGroup.Children.Add(closeScale);
            closeGroup.Children.Add(closeMove);
            card = new Border
            {
                Width = Math.Max(360, Width * 0.86),
                Height = Math.Max(280, Height * 0.86),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Child = layout,
                RenderTransform = closeGroup,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var close = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 12, 18, 26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(19),
                Width = 38,
                Height = 38,
                Margin = new Thickness(0, 16, 24, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            close.MouseLeftButtonDown += delegate { BeginClose(); };
            var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            root.Children.Add(card);
            root.Children.Add(close);
            Content = root;

            root.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                double next = Math.Max(1, Math.Min(8, zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15)));
                zoom.ScaleX = next;
                zoom.ScaleY = next;
                if (next <= 1.001) { pan.X = 0; pan.Y = 0; }
                e.Handled = true;
            };
            picture.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    zoom.ScaleX = 1; zoom.ScaleY = 1; pan.X = 0; pan.Y = 0;
                    e.Handled = true;
                    return;
                }
                if (zoom.ScaleX <= 1.001) return;
                moving = true;
                moveFrom = e.GetPosition(root);
                picture.CaptureMouse();
                e.Handled = true;
            };
            picture.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!moving) return;
                Point at = e.GetPosition(root);
                pan.X += at.X - moveFrom.X;
                pan.Y += at.Y - moveFrom.Y;
                moveFrom = at;
            };
            picture.MouseLeftButtonUp += delegate
            {
                if (!moving) return;
                moving = false;
                picture.ReleaseMouseCapture();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) BeginClose();
            };
            Loaded += delegate { Activate(); Focus(); };
        }

        void BeginClose()
        {
            if (closing) return;
            closing = true;
            if (closedAction != null) closedAction();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(200);
            if (returnScreen.HasValue)
            {
                double dpi = 1.0;
                PresentationSource windowSource = PresentationSource.FromVisual(this);
                if (windowSource != null && windowSource.CompositionTarget != null) dpi = windowSource.CompositionTarget.TransformToDevice.M11;
                double targetX = (returnScreen.Value.X + returnScreen.Value.Width / 2) / dpi;
                double targetY = (returnScreen.Value.Y + returnScreen.Value.Height / 2) / dpi;
                closeMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(targetX - (Left + Width / 2), time) { EasingFunction = ease });
                closeMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(targetY - (Top + Height / 2), time) { EasingFunction = ease });
            }
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
            closeScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.15, time) { EasingFunction = ease });
            closeScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.15, time) { EasingFunction = ease });
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(215) };
            timer.Tick += delegate { timer.Stop(); Close(); };
            timer.Start();
        }
    }

    /// <summary>钉在桌面的图片，可拖动、滚轮缩放。</summary>
    internal sealed class PinnedImageViewerWindow : Window
    {
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        bool moving;
        Point moveFrom;

        public PinnedImageViewerWindow(string path)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            BitmapSource source = ViewerImageLoader.Load(path);
            double scale = Math.Min(1, Math.Min(SystemParameters.WorkArea.Width * 0.7 / source.PixelWidth, SystemParameters.WorkArea.Height * 0.7 / source.PixelHeight));
            Width = Math.Max(160, source.PixelWidth * scale + 12);
            Height = Math.Max(100, source.PixelHeight * scale + 12);
            var picture = new Image { Source = source, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
            var group = new TransformGroup();
            group.Children.Add(zoom);
            group.Children.Add(pan);
            picture.RenderTransform = group;
            var frame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(245, 20, 25, 32)),
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.45 },
                Padding = new Thickness(5),
                Child = picture
            };
            Content = frame;
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "复制图片" };
            copy.Click += delegate { try { Clipboard.SetImage(source); } catch (Exception) { } };
            var close = new MenuItem { Header = "取消钉住" };
            close.Click += delegate { Close(); };
            menu.Items.Add(copy);
            menu.Items.Add(close);
            frame.ContextMenu = menu;
            frame.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (zoom.ScaleX > 1.001)
                {
                    moving = true;
                    moveFrom = e.GetPosition(this);
                    frame.CaptureMouse();
                    return;
                }
                try { DragMove(); } catch (Exception) { }
            };
            frame.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!moving) return;
                Point at = e.GetPosition(this);
                pan.X += at.X - moveFrom.X;
                pan.Y += at.Y - moveFrom.Y;
                moveFrom = at;
            };
            frame.MouseLeftButtonUp += delegate
            {
                if (!moving) return;
                moving = false;
                frame.ReleaseMouseCapture();
            };
            frame.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                double next = Math.Max(1, Math.Min(8, zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15)));
                zoom.ScaleX = next;
                zoom.ScaleY = next;
                if (next <= 1.001) { pan.X = 0; pan.Y = 0; }
                e.Handled = true;
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            Loaded += delegate
            {
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                Left = Math.Max(0, Math.Min(SystemParameters.WorkArea.Right - Width, cursor.X - Width / 2));
                Top = Math.Max(0, Math.Min(SystemParameters.WorkArea.Bottom - Height, cursor.Y - Height / 2));
            };
        }
    }
}
