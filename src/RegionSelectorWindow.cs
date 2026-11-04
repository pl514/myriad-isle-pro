using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DrawingRectangle = System.Drawing.Rectangle;

namespace IslandShot
{
    internal sealed class RegionSelectorWindow : Window
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        readonly Canvas surface;
        readonly Path shade;
        readonly Border border;
        System.Windows.Point start;
        bool selecting;
        Rect selection;
        DrawingRectangle? result;

        public RegionSelectorWindow(BitmapSource background)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Brushes.Black;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            Cursor = Cursors.Cross;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var root = new Grid { Background = Brushes.Black };
            var image = new Image { Source = background, Stretch = Stretch.Fill };
            root.Children.Add(image);
            surface = new Canvas { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Width = Width, Height = Height };
            shade = new Path { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
            border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 225, 255)),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            surface.Children.Add(shade);
            surface.Children.Add(border);
            root.Children.Add(surface);
            Content = root;

            surface.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                start = e.GetPosition(surface);
                selecting = true;
                selection = new Rect(start, start);
                surface.CaptureMouse();
                Redraw();
            };
            surface.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!selecting) return;
                System.Windows.Point now = e.GetPosition(surface);
                selection = new Rect(
                    Math.Min(start.X, now.X),
                    Math.Min(start.Y, now.Y),
                    Math.Abs(now.X - start.X),
                    Math.Abs(now.Y - start.Y));
                Redraw();
            };
            surface.MouseLeftButtonUp += delegate
            {
                if (!selecting) return;
                selecting = false;
                surface.ReleaseMouseCapture();
                if (selection.Width < 4 || selection.Height < 4)
                {
                    result = null;
                    Close();
                    return;
                }
                System.Windows.Point topLeft = PointToScreen(new System.Windows.Point(selection.Left, selection.Top));
                System.Windows.Point bottomRight = PointToScreen(new System.Windows.Point(selection.Right, selection.Bottom));
                result = new DrawingRectangle(
                    (int)Math.Round(topLeft.X),
                    (int)Math.Round(topLeft.Y),
                    Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
                    Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
                Close();
            };
            Loaded += delegate
            {
                Redraw();
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                Activate();
                Focus();
            };
            SizeChanged += delegate { Redraw(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    result = null;
                    Close();
                }
            };
        }

        public DrawingRectangle? Pick()
        {
            ShowDialog();
            return result;
        }

        void Redraw()
        {
            var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
            AddFigure(geometry, new Rect(0, 0, Width, Height));
            if (selection.Width > 0 && selection.Height > 0) AddFigure(geometry, selection);
            shade.Data = geometry;
            Canvas.SetLeft(border, selection.Left);
            Canvas.SetTop(border, selection.Top);
            border.Width = Math.Max(0, selection.Width);
            border.Height = Math.Max(0, selection.Height);
        }

        static void AddFigure(PathGeometry geometry, Rect rect)
        {
            var figure = new PathFigure { StartPoint = new System.Windows.Point(rect.Left, rect.Top), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(new System.Windows.Point(rect.Right, rect.Top), true));
            figure.Segments.Add(new LineSegment(new System.Windows.Point(rect.Right, rect.Bottom), true));
            figure.Segments.Add(new LineSegment(new System.Windows.Point(rect.Left, rect.Bottom), true));
            geometry.Figures.Add(figure);
        }
    }
}
