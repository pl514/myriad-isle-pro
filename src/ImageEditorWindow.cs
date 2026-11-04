using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace IslandShot
{
    /// <summary>图片标注编辑器：画笔、移动、形状、缩放、裁剪、翻转、保存副本。</summary>
    internal sealed class ImageEditorWindow : Window
    {
        enum EditorTool { Ink, Pan, Rectangle, Square, Circle, Crop }

        readonly string path;
        readonly Action<string> saved;
        readonly InkCanvas ink = new InkCanvas { Background = Brushes.Transparent };
        readonly Canvas shapeLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        readonly Canvas cropLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        readonly Grid canvas = new Grid();
        readonly Grid view = new Grid();
        readonly Grid viewport = new Grid();
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform(0, 0);
        readonly List<Shape> shapes = new List<Shape>();
        readonly StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock zoomLabel = new TextBlock { Text = "100%", Foreground = new SolidColorBrush(Color.FromRgb(90, 104, 122)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        BitmapSource source;
        Image picture;
        EditorTool tool = EditorTool.Pan;
        Border cropBox;
        Point cropStart;
        bool panning;
        Point panStart;
        double panOriginX, panOriginY;
        Button brushButton, panButton, rectButton, squareButton, circleButton, cropButton;

        public ImageEditorWindow(string path, Action<string> saved)
        {
            this.path = path;
            this.saved = saved;
            Title = "图片编辑 · " + System.IO.Path.GetFileName(path);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 251));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ShowInTaskbar = true;
            Topmost = true;

            source = ViewerImageLoader.Load(path);
            viewport.Background = new SolidColorBrush(Color.FromRgb(24, 28, 36));
            viewport.ClipToBounds = true;
            view.HorizontalAlignment = HorizontalAlignment.Center;
            view.VerticalAlignment = VerticalAlignment.Center;
            var transforms = new TransformGroup();
            transforms.Children.Add(zoom);
            transforms.Children.Add(pan);
            view.RenderTransform = transforms;
            view.RenderTransformOrigin = new Point(0, 0);
            canvas.Children.Add(new Border { Background = Brushes.White });
            picture = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            canvas.Children.Add(picture);
            canvas.Children.Add(shapeLayer);
            canvas.Children.Add(ink);
            canvas.Children.Add(cropLayer);
            view.Children.Add(canvas);
            viewport.Children.Add(view);
            RebuildCanvas();

            ink.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Color.FromRgb(255, 72, 72),
                Width = 4,
                Height = 4,
                FitToCurve = true
            };
            ink.EditingMode = InkCanvasEditingMode.Ink;

            brushButton = ToolButton("画笔", true);
            brushButton.Click += delegate { SetTool(EditorTool.Ink); };
            panButton = ToolButton("移动", false);
            panButton.Click += delegate { SetTool(EditorTool.Pan); };
            rectButton = ToolButton("矩形", false);
            rectButton.Click += delegate { SetTool(EditorTool.Rectangle); };
            squareButton = ToolButton("正方形", false);
            squareButton.Click += delegate { SetTool(EditorTool.Square); };
            circleButton = ToolButton("圆形", false);
            circleButton.Click += delegate { SetTool(EditorTool.Circle); };
            cropButton = ToolButton("裁剪", false);
            cropButton.Click += delegate { SetTool(EditorTool.Crop); };
            tools.Children.Add(brushButton);
            tools.Children.Add(panButton);
            tools.Children.Add(rectButton);
            tools.Children.Add(squareButton);
            tools.Children.Add(circleButton);
            tools.Children.Add(cropButton);
            tools.Children.Add(new Border { Width = 1, Height = 22, Background = new SolidColorBrush(Color.FromRgb(218, 226, 238)), Margin = new Thickness(7, 0, 7, 0) });

            foreach (string value in new[] { "#FF4848", "#FFB23E", "#FFE45C", "#43D17A", "#38D6F5", "#7C6CF2", "#FFFFFF", "#151A22" })
            {
                Color color = (Color)ColorConverter.ConvertFromString(value);
                var swatch = new Button
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(3, 0, 3, 0),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 204)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = value
                };
                swatch.Click += delegate { ink.DefaultDrawingAttributes.Color = color; SetTool(EditorTool.Ink); };
                tools.Children.Add(swatch);
            }

            tools.Children.Add(new TextBlock { Text = "粗细", Foreground = new SolidColorBrush(Color.FromRgb(90, 104, 122)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 5, 0) });
            var slider = new Slider { Minimum = 1, Maximum = 24, Value = 4, Width = 80, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += delegate
            {
                ink.DefaultDrawingAttributes.Width = slider.Value;
                ink.DefaultDrawingAttributes.Height = slider.Value;
            };
            tools.Children.Add(slider);
            var zoomOut = ToolButton("−", false);
            zoomOut.Width = 28;
            zoomOut.Padding = new Thickness(0);
            zoomOut.Click += delegate { AdjustZoom(1 / 1.2); };
            var zoomIn = ToolButton("+", false);
            zoomIn.Width = 28;
            zoomIn.Padding = new Thickness(0);
            zoomIn.Click += delegate { AdjustZoom(1.2); };
            tools.Children.Add(zoomOut);
            tools.Children.Add(zoomLabel);
            tools.Children.Add(zoomIn);

            var undo = ToolButton("撤回", false);
            undo.ToolTip = "撤回上一笔（Ctrl+Z）";
            undo.Click += delegate { Undo(); };
            var clear = ToolButton("清空", false);
            clear.Click += delegate { ink.Strokes.Clear(); shapeLayer.Children.Clear(); shapes.Clear(); };
            var flipH = ToolButton("水平翻转", false);
            flipH.Click += delegate { Flip(true); };
            var flipV = ToolButton("垂直翻转", false);
            flipV.Click += delegate { Flip(false); };
            var size = ToolButton("尺寸", false);
            size.Click += delegate { ResizeImage(); };
            tools.Children.Add(clear);
            tools.Children.Add(flipH);
            tools.Children.Add(flipV);
            tools.Children.Add(size);

            var save = ToolButton("保存副本", true);
            save.Background = new SolidColorBrush(Color.FromRgb(124, 108, 242));
            save.Foreground = Brushes.White;
            save.Click += delegate { SaveCopy(); };
            var close = ToolButton("关闭", false);
            close.Click += delegate { Close(); };
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(undo);
            right.Children.Add(save);
            right.Children.Add(close);

            var bar = new Grid { Height = 46, Margin = new Thickness(12, 8, 12, 8) };
            bar.Children.Add(tools);
            bar.Children.Add(right);

            var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)) };
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(viewport);
            Content = root;
            Width = Math.Min(1240, SystemParameters.WorkArea.Width - 60);
            Height = Math.Min(820, SystemParameters.WorkArea.Height - 60);
            MinWidth = 760;
            MinHeight = 520;

            PreviewMouseWheel += ZoomWheel;
            viewport.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount != 2) return;
                zoom.ScaleX = zoom.ScaleY = 1;
                pan.X = pan.Y = 0;
                zoomLabel.Text = "100%";
            };
            viewport.PreviewMouseLeftButtonDown += ViewDown;
            viewport.PreviewMouseMove += ViewMove;
            viewport.PreviewMouseLeftButtonUp += ViewUp;
            viewport.MouseRightButtonDown += delegate { if (cropBox != null) { cropLayer.Children.Remove(cropBox); cropBox = null; } };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Undo();
            };
            SetTool(EditorTool.Pan);
        }

        void RebuildCanvas()
        {
            double maxWidth = 1000;
            double maxHeight = 650;
            double scale = Math.Min(1, Math.Min(maxWidth / source.PixelWidth, maxHeight / source.PixelHeight));
            double width = Math.Max(220, source.PixelWidth * scale);
            double height = Math.Max(160, source.PixelHeight * scale);
            canvas.Width = width;
            canvas.Height = height;
            picture.Source = source;
            picture.Width = width;
            picture.Height = height;
            ink.Width = width;
            ink.Height = height;
            shapeLayer.Width = width;
            shapeLayer.Height = height;
            cropLayer.Width = width;
            cropLayer.Height = height;
            zoom.ScaleX = zoom.ScaleY = 1;
            pan.X = pan.Y = 0;
            zoomLabel.Text = "100%";
        }

        void SetTool(EditorTool value)
        {
            tool = value;
            ink.EditingMode = value == EditorTool.Ink ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
            ink.IsHitTestVisible = value == EditorTool.Ink;
            viewportCursor(value);
            Button[] buttons = { brushButton, panButton, rectButton, squareButton, circleButton, cropButton };
            foreach (Button button in buttons)
            {
                if (button != null) button.Background = Brushes.Transparent;
            }
            Button active = value == EditorTool.Ink ? brushButton : value == EditorTool.Pan ? panButton : value == EditorTool.Rectangle ? rectButton : value == EditorTool.Square ? squareButton : value == EditorTool.Circle ? circleButton : cropButton;
            if (active != null) active.Background = new SolidColorBrush(Color.FromRgb(238, 234, 255));
        }

        void viewportCursor(EditorTool value)
        {
            Cursor cursor = value == EditorTool.Pan ? Cursors.SizeAll : value == EditorTool.Crop ? Cursors.Cross : value == EditorTool.Ink ? Cursors.Pen : Cursors.Cross;
            Cursor = cursor;
            if (view != null) view.Cursor = cursor;
            if (viewport != null) viewport.Cursor = cursor;
            if (ink != null) ink.Cursor = cursor;
        }

        void AdjustZoom(double factor)
        {
            double next = Math.Max(1.0, Math.Min(5, zoom.ScaleX * factor));
            zoom.ScaleX = next;
            zoom.ScaleY = next;
            ClampPan();
            zoomLabel.Text = (int)Math.Round(next * 100) + "%";
        }

        void ZoomWheel(object sender, MouseWheelEventArgs e)
        {
            double oldScale = zoom.ScaleX;
            double next = Math.Max(1.0, Math.Min(5, oldScale * (e.Delta > 0 ? 1.12 : 1 / 1.12)));
            zoom.ScaleX = next;
            zoom.ScaleY = next;
            ClampPan();
            zoomLabel.Text = (int)Math.Round(next * 100) + "%";
            e.Handled = true;
        }

        void ClampPan()
        {
            if (viewport == null || canvas == null) return;
            double scaledWidth = canvas.Width * zoom.ScaleX;
            double scaledHeight = canvas.Height * zoom.ScaleY;
            double maxX = Math.Max(120, scaledWidth - 80);
            double maxY = Math.Max(120, scaledHeight - 80);
            pan.X = Math.Max(-maxX, Math.Min(maxX, pan.X));
            pan.Y = Math.Max(-maxY, Math.Min(maxY, pan.Y));
        }

        void ViewDown(object sender, MouseButtonEventArgs e)
        {
            if (tool == EditorTool.Ink) return;
            Point at = e.GetPosition(canvas);
            if (tool == EditorTool.Pan)
            {
                panning = true;
                panStart = e.GetPosition(this);
                panOriginX = pan.X;
                panOriginY = pan.Y;
                view.CaptureMouse();
                e.Handled = true;
                return;
            }
            if (tool == EditorTool.Crop)
            {
                cropStart = at;
                cropBox = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(124, 108, 242)),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromArgb(35, 124, 108, 242)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(cropBox, at.X);
                Canvas.SetTop(cropBox, at.Y);
                cropLayer.Children.Add(cropBox);
                view.CaptureMouse();
                e.Handled = true;
                return;
            }
            if (tool == EditorTool.Rectangle || tool == EditorTool.Square || tool == EditorTool.Circle)
            {
                shapes.Add(null);
                Shape shape = tool == EditorTool.Circle ? (Shape)new Ellipse() : new Rectangle();
                shape.Stroke = new SolidColorBrush(ink.DefaultDrawingAttributes.Color);
                shape.StrokeThickness = ink.DefaultDrawingAttributes.Width;
                shape.Fill = Brushes.Transparent;
                shapeLayer.Children.Add(shape);
                shapes[shapes.Count - 1] = shape;
                Canvas.SetLeft(shape, at.X);
                Canvas.SetTop(shape, at.Y);
                shape.Tag = new Point(at.X, at.Y);
                view.CaptureMouse();
                e.Handled = true;
            }
        }

        void ViewMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                Point at = e.GetPosition(this);
                pan.X = panOriginX + (at.X - panStart.X);
                pan.Y = panOriginY + (at.Y - panStart.Y);
                ClampPan();
                e.Handled = true;
                return;
            }
            if (cropBox != null && tool == EditorTool.Crop)
            {
                Point at = e.GetPosition(canvas);
                double left = Math.Min(cropStart.X, at.X);
                double top = Math.Min(cropStart.Y, at.Y);
                Canvas.SetLeft(cropBox, left);
                Canvas.SetTop(cropBox, top);
                cropBox.Width = Math.Abs(at.X - cropStart.X);
                cropBox.Height = Math.Abs(at.Y - cropStart.Y);
                e.Handled = true;
                return;
            }
            if (shapes.Count > 0 && shapes[shapes.Count - 1] != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Shape shape = shapes[shapes.Count - 1];
                if (!(shape.Tag is Point)) return;
                Point start = (Point)shape.Tag;
                Point at = e.GetPosition(canvas);
                double width = Math.Abs(at.X - start.X);
                double height = Math.Abs(at.Y - start.Y);
                if (tool == EditorTool.Square || tool == EditorTool.Circle)
                {
                    width = Math.Max(width, height);
                    height = width;
                }
                double left = at.X < start.X ? start.X - width : start.X;
                double top = at.Y < start.Y ? start.Y - height : start.Y;
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, top);
                shape.Width = width;
                shape.Height = height;
                e.Handled = true;
            }
        }

        void ViewUp(object sender, MouseButtonEventArgs e)
        {
            if (panning)
            {
                panning = false;
                view.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (cropBox != null && tool == EditorTool.Crop)
            {
                double left = Canvas.GetLeft(cropBox);
                double top = Canvas.GetTop(cropBox);
                int x = Math.Max(0, (int)Math.Round(left * source.PixelWidth / canvas.Width));
                int y = Math.Max(0, (int)Math.Round(top * source.PixelHeight / canvas.Height));
                int w = Math.Min(source.PixelWidth - x, Math.Max(1, (int)Math.Round(cropBox.Width * source.PixelWidth / canvas.Width)));
                int h = Math.Min(source.PixelHeight - y, Math.Max(1, (int)Math.Round(cropBox.Height * source.PixelHeight / canvas.Height)));
                cropLayer.Children.Remove(cropBox);
                cropBox = null;
                view.ReleaseMouseCapture();
                if (w > 4 && h > 4)
                {
                    source = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
                    ink.Strokes.Clear();
                    shapeLayer.Children.Clear();
                    shapes.Clear();
                    RebuildCanvas();
                }
                e.Handled = true;
                return;
            }
            if (shapes.Count > 0 && shapes[shapes.Count - 1] != null)
            {
                Shape shape = shapes[shapes.Count - 1];
                if (shape.Width < 3 || shape.Height < 3)
                {
                    shapeLayer.Children.Remove(shape);
                    shapes.RemoveAt(shapes.Count - 1);
                }
                view.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        void Undo()
        {
            if (shapes.Count > 0)
            {
                Shape shape = shapes[shapes.Count - 1];
                shapes.RemoveAt(shapes.Count - 1);
                if (shape != null) shapeLayer.Children.Remove(shape);
                return;
            }
            if (ink.Strokes.Count > 0) ink.Strokes.RemoveAt(ink.Strokes.Count - 1);
        }

        void Flip(bool horizontal)
        {
            source = new TransformedBitmap(source, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
            ink.Strokes.Clear();
            shapeLayer.Children.Clear();
            shapes.Clear();
            RebuildCanvas();
        }

        static Button ToolButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                Height = 30,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(65, 78, 96)),
                Background = primary ? new SolidColorBrush(Color.FromRgb(238, 234, 255)) : Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 236)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        void ResizeImage()
        {
            var dialog = new Window
            {
                Title = "调整尺寸",
                Width = 300,
                Height = 190,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
                FontFamily = new FontFamily("Microsoft YaHei UI")
            };
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = "宽度（像素）", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(90, 104, 122)) });
            var widthBox = new TextBox { Text = source.PixelWidth.ToString(), Height = 28, Margin = new Thickness(0, 3, 0, 10) };
            panel.Children.Add(widthBox);
            panel.Children.Add(new TextBlock { Text = "高度（像素）", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(90, 104, 122)) });
            var heightBox = new TextBox { Text = source.PixelHeight.ToString(), Height = 28, Margin = new Thickness(0, 3, 0, 10) };
            panel.Children.Add(heightBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Width = 70, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { dialog.Close(); };
            var apply = new Button { Content = "应用", Width = 70, Height = 28 };
            apply.Click += delegate
            {
                int width, height;
                if (!int.TryParse(widthBox.Text, out width) || !int.TryParse(heightBox.Text, out height) || width < 16 || height < 16 || width > 12000 || height > 12000)
                {
                    MessageBox.Show(dialog, "请输入 16–12000 之间的尺寸。", "调整尺寸", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                source = new TransformedBitmap(source, new ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
                ink.Strokes.Clear();
                shapeLayer.Children.Clear();
                shapes.Clear();
                RebuildCanvas();
                dialog.Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(apply);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.ShowDialog();
        }

        void SaveCopy()
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存编辑后的图片",
                FileName = System.IO.Path.GetFileNameWithoutExtension(path) + "_编辑.png",
                Filter = "PNG 图片|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Round(canvas.Width)),
                    Math.Max(1, (int)Math.Round(canvas.Height)),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(dialog.FileName)) encoder.Save(stream);
                if (saved != null) saved(dialog.FileName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "保存失败：" + exception.Message, "图片编辑", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
