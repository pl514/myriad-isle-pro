using System;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using IoPath = System.IO.Path;

namespace IslandShot
{
    internal sealed class RegionCaptureWindow : Window
    {
        const int ExtendedFrameBounds = 9;
        const uint SetWindowPosFlags = 0x0040;
        static readonly IntPtr TopmostHandle = new IntPtr(-1);

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out NativeRect value, int size);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }

        readonly Drawing.Bitmap desktop;
        readonly Drawing.Rectangle screen;
        readonly Rectangle marquee;
        readonly TextBlock size;
        readonly TaskCompletionSource<Drawing.Bitmap> completion = new TaskCompletionSource<Drawing.Bitmap>();
        Point anchor;
        bool dragging;
        bool finished;

        public RegionCaptureWindow(Drawing.Bitmap desktop, Drawing.Rectangle screen)
        {
            this.desktop = desktop;
            this.screen = screen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;

            var root = new Grid { Background = Brushes.Black };
            root.Children.Add(new Image { Source = ToSource(desktop), Stretch = Stretch.Fill, IsHitTestVisible = false });
            root.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(104, 8, 12, 18)), IsHitTestVisible = false });
            var surface = new Canvas();
            marquee = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(246, 250, 255)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            size = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(200, 22, 30, 40)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            surface.Children.Add(marquee);
            surface.Children.Add(size);
            root.Children.Add(surface);
            Content = root;

            double scale = ScreenScale();
            Left = screen.Left / scale;
            Top = screen.Top / scale;
            Width = screen.Width / scale;
            Height = screen.Height / scale;

            Loaded += delegate
            {
                var helper = new WindowInteropHelper(this);
                SetWindowPos(helper.Handle, TopmostHandle, screen.Left, screen.Top, screen.Width, screen.Height, SetWindowPosFlags);
                Activate();
                Focus();
            };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonDown += delegate { Cancel(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                Cancel();
                e.Handled = true;
            };
            Closed += delegate
            {
                if (finished) return;
                finished = true;
                completion.TrySetResult(null);
            };
        }

        public Task<Drawing.Bitmap> PickAsync()
        {
            Show();
            Activate();
            return completion.Task;
        }

        static double ScreenScale()
        {
            Forms.Screen primary = Forms.Screen.PrimaryScreen;
            try
            {
                using (var graphics = Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return graphics.DpiX / 96.0;
                }
            }
            catch (Exception) { return 1.0; }
        }

        internal static ImageSource ToSource(Drawing.Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(handle); }
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            anchor = Clamp(e.GetPosition(this));
            dragging = true;
            marquee.Visibility = Visibility.Visible;
            size.Visibility = Visibility.Visible;
            CaptureMouse();
            UpdateSelection(anchor);
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            UpdateSelection(Clamp(e.GetPosition(this)));
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            ReleaseMouseCapture();
            Point point = Clamp(e.GetPosition(this));
            Matrix transform = Transform;
            int left = (int)Math.Round(screen.Left + Math.Min(anchor.X, point.X) * transform.M11);
            int top = (int)Math.Round(screen.Top + Math.Min(anchor.Y, point.Y) * transform.M22);
            int right = (int)Math.Round(screen.Left + Math.Max(anchor.X, point.X) * transform.M11);
            int bottom = (int)Math.Round(screen.Top + Math.Max(anchor.Y, point.Y) * transform.M22);
            var crop = Drawing.Rectangle.FromLTRB(left, top, right, bottom);
            crop.Intersect(new Drawing.Rectangle(0, 0, desktop.Width, desktop.Height));
            if (crop.Width < 4 || crop.Height < 4)
            {
                Cancel();
                e.Handled = true;
                return;
            }
            Drawing.Bitmap result = desktop.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Finish(result);
            e.Handled = true;
        }

        void Cancel()
        {
            Finish(null);
        }

        void Finish(Drawing.Bitmap result)
        {
            if (finished) return;
            finished = true;
            Close();
            completion.TrySetResult(result);
        }

        void UpdateSelection(Point point)
        {
            double left = Math.Min(anchor.X, point.X);
            double top = Math.Min(anchor.Y, point.Y);
            double width = Math.Abs(point.X - anchor.X);
            double height = Math.Abs(point.Y - anchor.Y);
            Canvas.SetLeft(marquee, left);
            Canvas.SetTop(marquee, top);
            marquee.Width = width;
            marquee.Height = height;
            Matrix transform = Transform;
            size.Text = (int)Math.Round(width * transform.M11) + " × " + (int)Math.Round(height * transform.M22);
            double labelTop = top + height + 6;
            if (labelTop + 30 > ActualHeight) labelTop = Math.Max(0, top - 30);
            Canvas.SetLeft(size, left);
            Canvas.SetTop(size, labelTop);
        }

        Point Clamp(Point point)
        {
            return new Point(
                Math.Max(0, Math.Min(point.X, ActualWidth)),
                Math.Max(0, Math.Min(point.Y, ActualHeight)));
        }

        Matrix Transform
        {
            get
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                return source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            }
        }
    }
}
