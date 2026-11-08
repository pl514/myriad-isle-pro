using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandShot
{
    // 编辑画布：负责绘制（图片 + 标注 + 选中框）和鼠标交互（创建/选择/移动/缩放/裁剪）。
    // 所有标注坐标都用"图片像素坐标"保存，缩放只影响显示。
    internal sealed class EditorSurface : FrameworkElement
    {
        public BitmapSource BaseImage;
        public readonly List<EditorItem> Items = new List<EditorItem>();
        public EditorTool Tool = EditorTool.Select;
        public double Scale = 1;
        public Point Offset;

        public EditorItem Selected;
        public Rect CropRect;
        public bool CropActive;

        public int NextStepNumber = 1;
        public double DefaultThickness = 3;
        public Color DefaultStroke = Color.FromRgb(255, 69, 58);
        public Color DefaultFill = Color.FromArgb(0, 0, 0, 0);
        public double DefaultOpacity = 1;
        public double DefaultFontSize = 22;
        public int BlurRadius = 6;
        public int PixelBlock = 10;

        public event Action<Point> TextRequested;
        public event Action Changed;

        sealed class Snapshot
        {
            public BitmapSource Image;
            public List<EditorItem> Items;
        }

        readonly List<Snapshot> undoStack = new List<Snapshot>();
        readonly List<Snapshot> redoStack = new List<Snapshot>();

        // 交互状态
        bool dragging;
        Point dragStart;
        Point lastPoint;
        EditorItem editing;
        EditorHandle activeHandle = EditorHandle.None;
        Rect editStartBounds;
        bool movingItem;
        Point moveOrigin;
        Rect moveStartRect;
        bool cropDragging;
        Point cropStart;

        public EditorSurface()
        {
            ClipToBounds = true;
            Focusable = true;
            Cursor = Cursors.Arrow;
        }

        // ------------------------------------------------------------ 状态
        public void PushUndo()
        {
            undoStack.Add(new Snapshot { Image = BaseImage, Items = CloneItems() });
            if (undoStack.Count > 40) undoStack.RemoveAt(0);
            redoStack.Clear();
        }

        List<EditorItem> CloneItems()
        {
            var copy = new List<EditorItem>();
            foreach (EditorItem item in Items) copy.Add(item.Clone());
            return copy;
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            Snapshot previous = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            redoStack.Add(new Snapshot { Image = BaseImage, Items = CloneItems() });

            BaseImage = previous.Image;
            Items.Clear();
            Items.AddRange(previous.Items);
            Selected = null;
            Invalidate(true);
            RaiseChanged();
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            Snapshot next = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            undoStack.Add(new Snapshot { Image = BaseImage, Items = CloneItems() });

            BaseImage = next.Image;
            Items.Clear();
            Items.AddRange(next.Items);
            Selected = null;
            Invalidate(true);
            RaiseChanged();
        }

        public bool CanUndo { get { return undoStack.Count > 0; } }
        public bool CanRedo { get { return redoStack.Count > 0; } }

        public void Invalidate(bool resetSelection)
        {
            if (resetSelection) { Selected = null; }
            InvalidateVisual();
            RaiseChanged();
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        // ------------------------------------------------------------ 坐标
        public Point ToImage(Point point)
        {
            double sx = (point.X - Offset.X) / Scale;
            double sy = (point.Y - Offset.Y) / Scale;
            return new Point(sx, sy);
        }

        public Matrix ViewMatrix()
        {
            var matrix = Matrix.Identity;
            matrix.Scale(Scale, Scale);
            matrix.Translate(Offset.X, Offset.Y);
            return matrix;
        }

        public void ZoomToFit()
        {
            if (BaseImage == null || ActualWidth < 10 || ActualHeight < 10) return;
            double scaleX = (ActualWidth - 40) / BaseImage.PixelWidth;
            double scaleY = (ActualHeight - 40) / BaseImage.PixelHeight;
            Scale = Math.Max(0.05, Math.Min(1.0, Math.Min(scaleX, scaleY)));
            Offset = new Point(
                (ActualWidth - BaseImage.PixelWidth * Scale) / 2,
                (ActualHeight - BaseImage.PixelHeight * Scale) / 2);
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (BaseImage != null && (Offset.X == 0 && Offset.Y == 0)) ZoomToFit();
        }

        // ------------------------------------------------------------ 绘制
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 26, 32)), null, new Rect(0, 0, width, height));

            if (BaseImage == null) return;

            Matrix matrix = ViewMatrix();
            dc.PushTransform(new MatrixTransform(matrix));

            var imageRect = new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight);
            dc.DrawImage(BaseImage, imageRect);

            foreach (EditorItem item in Items) item.Draw(dc, BaseImage);

            // 选中虚线框/手柄只在"选择"工具下显示：
            // 画矩形、画笔、箭头时不应该在刚画的东西外面套一圈蓝框。
            bool showSelection = Tool == EditorTool.Select && Selected != null;

            if (showSelection)
            {
                double thickness = 1 / Scale;
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(90, 170, 255)), thickness);
                pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
                pen.Freeze();

                Rect box = Selected.Bounds;
                if (box.Width > 0 || box.Height > 0) dc.DrawRectangle(null, pen, box);

                foreach (Point handle in HandlePoints(box))
                {
                    double size = 7 / Scale;
                    dc.DrawRectangle(Brushes.White, new Pen(Brushes.SteelBlue, 1 / Scale),
                        new Rect(handle.X - size / 2, handle.Y - size / 2, size, size));
                }
            }
            else if (CropActive)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(90, 170, 255)), 1 / Scale);
                pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
                pen.Freeze();
                dc.DrawRectangle(null, pen, CropRect);
            }

            dc.Pop();

            if (CropActive && !CropRect.IsEmpty)
            {
                // 裁剪框外压暗（屏幕坐标）
                var shade = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
                shade.Freeze();
                Matrix m = ViewMatrix();
                Rect screen = new Rect(
                    CropRect.X * Scale + Offset.X,
                    CropRect.Y * Scale + Offset.Y,
                    CropRect.Width * Scale,
                    CropRect.Height * Scale);

                dc.DrawRectangle(shade, null, new Rect(0, 0, width, Math.Max(0, screen.Top)));
                dc.DrawRectangle(shade, null, new Rect(0, screen.Bottom, width, Math.Max(0, height - screen.Bottom)));
                dc.DrawRectangle(shade, null, new Rect(0, screen.Top, Math.Max(0, screen.Left), screen.Height));
                dc.DrawRectangle(shade, null, new Rect(screen.Right, screen.Top, Math.Max(0, width - screen.Right), screen.Height));
            }
        }

        IEnumerable<Point> HandlePoints(Rect box)
        {
            yield return new Point(box.Left, box.Top);
            yield return new Point(box.Left + box.Width / 2, box.Top);
            yield return new Point(box.Right, box.Top);
            yield return new Point(box.Left, box.Top + box.Height / 2);
            yield return new Point(box.Right, box.Top + box.Height / 2);
            yield return new Point(box.Left, box.Bottom);
            yield return new Point(box.Left + box.Width / 2, box.Bottom);
            yield return new Point(box.Right, box.Bottom);
        }

        // ------------------------------------------------------------ 鼠标
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            Point imagePoint = ToImage(e.GetPosition(this));
            dragStart = imagePoint;
            lastPoint = imagePoint;
            dragging = true;

            if (e.ClickCount == 2 && Tool == EditorTool.Select)
            {
                dragging = false;
                HandleDoubleClick(imagePoint);
                return;
            }

            if (Tool == EditorTool.Pan)
            {
                Mouse.Capture(this);
                return;
            }

            if (Tool == EditorTool.Crop)
            {
                cropDragging = true;
                CropActive = true;
                cropStart = ClampToImage(imagePoint);
                CropRect = new Rect(cropStart, cropStart);
                Mouse.Capture(this);
                InvalidateVisual();
                return;
            }

            if (Tool == EditorTool.Select)
            {
                // 先抓手柄，再选对象
                if (Selected != null)
                {
                    EditorHandle handle = HitHandle(imagePoint, Selected.Bounds);
                    if (handle != EditorHandle.None)
                    {
                        PushUndo();
                        activeHandle = handle;
                        editStartBounds = Selected.Bounds;
                        Mouse.Capture(this);
                        return;
                    }
                }

                EditorItem hit = HitTest(imagePoint);
                Selected = hit;
                if (hit != null)
                {
                    PushUndo();
                    movingItem = true;
                    moveOrigin = imagePoint;
                    moveStartRect = hit.Bounds;
                    Mouse.Capture(this);
                }
                InvalidateVisual();
                RaiseChanged();
                return;
            }

            // 创建新标注
            PushUndo();
            editing = CreateItem(Tool, imagePoint);
            if (editing == null) { dragging = false; return; }
            Items.Add(editing);
            Selected = editing;
            Mouse.Capture(this);
            InvalidateVisual();
        }

        EditorItem CreateItem(EditorTool tool, Point start)
        {
            switch (tool)
            {
                case EditorTool.Rect:
                    return new RectItem { Rect = new Rect(start, start), Thickness = DefaultThickness, Stroke = DefaultStroke, Fill = DefaultFill, Opacity = DefaultOpacity, Rounded = true };
                case EditorTool.Ellipse:
                    return new EllipseItem { Rect = new Rect(start, start), Thickness = DefaultThickness, Stroke = DefaultStroke, Fill = DefaultFill, Opacity = DefaultOpacity };
                case EditorTool.Line:
                    return new LineItem { Start = start, End = start, Thickness = DefaultThickness, Stroke = DefaultStroke, Opacity = DefaultOpacity };
                case EditorTool.Arrow:
                    return new LineItem { Start = start, End = start, Arrow = true, Thickness = DefaultThickness, Stroke = DefaultStroke, Opacity = DefaultOpacity };
                case EditorTool.Pen:
                    var pen = new PenItem { Thickness = DefaultThickness, Stroke = DefaultStroke, Opacity = DefaultOpacity };
                    pen.Points.Add(start);
                    return pen;
                case EditorTool.Bubble:
                    return new BubbleItem { Rect = new Rect(start, start), Thickness = DefaultThickness, Stroke = DefaultStroke, Fill = DefaultFill, Opacity = DefaultOpacity };
                case EditorTool.Step:
                    return new StepItem { Rect = new Rect(start, start), Number = NextStepNumber, Opacity = DefaultOpacity, Thickness = DefaultThickness, Stroke = DefaultStroke };
                case EditorTool.Highlight:
                    return new HighlightItem { Rect = new Rect(start, start), Opacity = 1, Fill = Color.FromArgb(90, 255, 226, 66) };
                case EditorTool.Blur:
                    return new BlurItem { Rect = new Rect(start, start), Radius = BlurRadius, Opacity = 1 };
                case EditorTool.Pixelate:
                    return new PixelateItem { Rect = new Rect(start, start), Block = PixelBlock, Opacity = 1 };
                case EditorTool.Text:
                    Action<Point> request = TextRequested;
                    if (request != null) request(start);
                    return null;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point imagePoint = ToImage(e.GetPosition(this));

            if (Tool == EditorTool.Pan &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                Point screen = e.GetPosition(this);
                Point lastScreen = new Point(lastPoint.X * Scale + Offset.X, lastPoint.Y * Scale + Offset.Y);
                Offset = new Point(Offset.X + (screen.X - lastScreen.X), Offset.Y + (screen.Y - lastScreen.Y));
                lastPoint = ToImage(screen);
                InvalidateVisual();
                return;
            }

            if (cropDragging)
            {
                Point current = ClampToImage(imagePoint);
                CropRect = new Rect(
                    Math.Min(cropStart.X, current.X),
                    Math.Min(cropStart.Y, current.Y),
                    Math.Abs(current.X - cropStart.X),
                    Math.Abs(current.Y - cropStart.Y));
                InvalidateVisual();
                return;
            }

            if (activeHandle != EditorHandle.None && Selected != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Selected.SetBounds(ResizeRect(editStartBounds, activeHandle, imagePoint));
                InvalidateVisual();
                return;
            }

            if (movingItem && Selected != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Vector delta = imagePoint - moveOrigin;
                Selected.Move(delta);
                moveOrigin = imagePoint;
                InvalidateVisual();
                return;
            }

            if (editing != null && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateItem(editing, imagePoint);
                InvalidateVisual();
            }
        }

        void UpdateItem(EditorItem item, Point point)
        {
            var rectItem = item as RectItem;
            if (rectItem != null) { rectItem.Rect = RectFrom(dragStart, point); return; }
            var ellipse = item as EllipseItem;
            if (ellipse != null) { ellipse.Rect = RectFrom(dragStart, point); return; }
            var line = item as LineItem;
            if (line != null) { line.End = point; return; }
            var pen = item as PenItem;
            if (pen != null)
            {
                if ((point - pen.Points[pen.Points.Count - 1]).Length > 1.5) pen.Points.Add(point);
                return;
            }
            var bubble = item as BubbleItem;
            if (bubble != null) { bubble.Rect = RectFrom(dragStart, point); return; }
            var step = item as StepItem;
            if (step != null) { step.Rect = SquareFrom(dragStart, point, 34); return; }
            var highlight = item as HighlightItem;
            if (highlight != null) { highlight.Rect = RectFrom(dragStart, point); return; }
            var blur = item as BlurItem;
            if (blur != null) { blur.Rect = RectFrom(dragStart, point); blur.Invalidate(); return; }
            var pixelate = item as PixelateItem;
            if (pixelate != null) { pixelate.Rect = RectFrom(dragStart, point); pixelate.Invalidate(); return; }
        }

        static Rect RectFrom(Point a, Point b)
        {
            return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }

        static Rect SquareFrom(Point a, Point b, double minimum)
        {
            double size = Math.Max(minimum, Math.Abs(b.X - a.X));
            size = Math.Max(size, Math.Abs(b.Y - a.Y));
            return new Rect(a.X, a.Y, size, size);
        }

        Point ClampToImage(Point point)
        {
            if (BaseImage == null) return point;
            return new Point(
                Math.Max(0, Math.Min(BaseImage.PixelWidth, point.X)),
                Math.Max(0, Math.Min(BaseImage.PixelHeight, point.Y)));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            try { if (IsMouseCaptured) ReleaseMouseCapture(); } catch (Exception) { }

            if (cropDragging)
            {
                cropDragging = false;
                dragging = false;
                if (CropRect.Width < 4 || CropRect.Height < 4) CropActive = false;
                InvalidateVisual();
                RaiseChanged();
                return;
            }

            if (editing != null)
            {
                // 太小的对象直接丢弃（避免误点留下小点）
                Rect bounds = editing.Bounds;
                if (bounds.Width < 3 && bounds.Height < 3 && !(editing is StepItem))
                {
                    Items.Remove(editing);
                    Selected = null;
                }
                else if (editing is StepItem)
                {
                    NextStepNumber++;
                }
                // 用绘制工具画完就取消选中：要改属性请切到"选择"工具点它
                if (editing != null && !(editing is TextItem)) Selected = null;
                editing = null;
            }

            activeHandle = EditorHandle.None;
            movingItem = false;
            dragging = false;
            InvalidateVisual();
            RaiseChanged();
        }

        // 双击文字/气泡：请求编辑文字（UIElement 没有 OnMouseDoubleClick，在按下时判断）
        void HandleDoubleClick(Point imagePoint)
        {
            TextItem text = Selected as TextItem;
            if (text != null)
            {
                Action<Point> request = TextRequested;
                if (request != null) request(text.Origin);
                return;
            }
            EditorItem hit = HitTest(imagePoint);
            TextItem hitText = hit as TextItem;
            if (hitText != null)
            {
                Selected = hitText;
                Action<Point> request2 = TextRequested;
                if (request2 != null) request2(hitText.Origin);
            }
        }

        // ------------------------------------------------------------ 命中
        EditorItem HitTest(Point imagePoint)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].HitTest(imagePoint, 4 / Scale)) return Items[i];
            }
            return null;
        }

        EditorHandle HitHandle(Point point, Rect bounds)
        {
            double tolerance = 7 / Scale;
            Point[] points = new Point[]
            {
                new Point(bounds.Left, bounds.Top), new Point(bounds.Left + bounds.Width / 2, bounds.Top), new Point(bounds.Right, bounds.Top),
                new Point(bounds.Left, bounds.Top + bounds.Height / 2), new Point(bounds.Right, bounds.Top + bounds.Height / 2),
                new Point(bounds.Left, bounds.Bottom), new Point(bounds.Left + bounds.Width / 2, bounds.Bottom), new Point(bounds.Right, bounds.Bottom)
            };
            EditorHandle[] handles = new EditorHandle[]
            {
                EditorHandle.TopLeft, EditorHandle.Top, EditorHandle.TopRight,
                EditorHandle.Left, EditorHandle.Right,
                EditorHandle.BottomLeft, EditorHandle.Bottom, EditorHandle.BottomRight
            };
            for (int i = 0; i < points.Length; i++)
            {
                if ((point - points[i]).Length <= tolerance) return handles[i];
            }
            return EditorHandle.None;
        }

        static Rect ResizeRect(Rect start, EditorHandle handle, Point point)
        {
            double left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
            switch (handle)
            {
                case EditorHandle.TopLeft: left = point.X; top = point.Y; break;
                case EditorHandle.Top: top = point.Y; break;
                case EditorHandle.TopRight: right = point.X; top = point.Y; break;
                case EditorHandle.Left: left = point.X; break;
                case EditorHandle.Right: right = point.X; break;
                case EditorHandle.BottomLeft: left = point.X; bottom = point.Y; break;
                case EditorHandle.Bottom: bottom = point.Y; break;
                case EditorHandle.BottomRight: right = point.X; bottom = point.Y; break;
            }
            if (right < left) { double temp = left; left = right; right = temp; }
            if (bottom < top) { double temp = top; top = bottom; bottom = temp; }
            return new Rect(left, top, Math.Max(2, right - left), Math.Max(2, bottom - top));
        }

        // ------------------------------------------------------------ 文本
        public void CommitText(string text, Point origin, bool isNew)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextItem item = Selected as TextItem;
            if (isNew || item == null)
            {
                PushUndo();
                item = new TextItem
                {
                    Text = text,
                    Origin = origin,
                    FontSize = DefaultFontSize,
                    Stroke = DefaultStroke,
                    Opacity = DefaultOpacity
                };
                item.Layout();
                Items.Add(item);
                Selected = item;
            }
            else
            {
                PushUndo();
                item.Text = text;
                item.Stroke = DefaultStroke;
                item.FontSize = DefaultFontSize;
                item.Layout();
            }
            InvalidateVisual();
            RaiseChanged();
        }

        // ------------------------------------------------------------ 图层
        public void MoveLayer(int direction)
        {
            if (Selected == null) return;
            int index = Items.IndexOf(Selected);
            if (index < 0) return;
            int target = index + direction;
            if (direction > 100) target = Items.Count - 1;
            if (direction < -100) target = 0;
            if (target < 0 || target >= Items.Count) return;
            PushUndo();
            Items.RemoveAt(index);
            Items.Insert(target, Selected);
            InvalidateVisual();
        }

        public void DeleteSelected()
        {
            if (Selected == null) return;
            PushUndo();
            Items.Remove(Selected);
            Selected = null;
            InvalidateVisual();
            RaiseChanged();
        }

        public void DuplicateSelected()
        {
            if (Selected == null) return;
            PushUndo();
            EditorItem copy = Selected.Clone();
            copy.Move(new Vector(12, 12));
            Items.Add(copy);
            Selected = copy;
            InvalidateVisual();
        }

        // ------------------------------------------------------------ 合成 / 裁剪
        public BitmapSource Compose()
        {
            if (BaseImage == null) return null;
            int width = BaseImage.PixelWidth;
            int height = BaseImage.PixelHeight;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(BaseImage, new Rect(0, 0, width, height));
                foreach (EditorItem item in Items) item.Draw(dc, BaseImage);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public void ApplyCrop()
        {
            if (!CropActive || CropRect.Width < 4 || CropRect.Height < 4) return;
            PushUndo();
            var region = new Int32Rect(
                (int)Math.Round(CropRect.X), (int)Math.Round(CropRect.Y),
                Math.Max(1, (int)Math.Round(CropRect.Width)), Math.Max(1, (int)Math.Round(CropRect.Height)));

            BaseImage = ImageEffects.Crop(BaseImage, region);
            foreach (EditorItem item in Items) item.Move(new Vector(-region.X, -region.Y));

            CropActive = false;
            CropRect = Rect.Empty;
            Selected = null;
            ZoomToFit();
            InvalidateVisual();
            RaiseChanged();
        }

        public void CancelCrop()
        {
            CropActive = false;
            CropRect = Rect.Empty;
            InvalidateVisual();
        }
    }
}
