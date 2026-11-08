using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandShot
{
    // 编辑器里的工具（只保留常用的）
    internal enum EditorTool
    {
        Select,
        Rect,
        Ellipse,
        Line,
        Arrow,
        Pen,
        Text,
        Bubble,
        Step,
        Highlight,
        Blur,
        Pixelate,
        Crop,
        Pan
    }

    internal enum EditorHandle
    {
        None,
        TopLeft, Top, TopRight,
        Left, Right,
        BottomLeft, Bottom, BottomRight
    }

    // ---------------------------------------------------------------- 基类
    internal abstract class EditorItem
    {
        public double Thickness = 3;
        public Color Stroke = Color.FromRgb(255, 69, 58);
        public Color Fill = Color.FromArgb(0, 0, 0, 0);
        public double Opacity = 1;

        public abstract Rect Bounds { get; }
        public abstract void Draw(DrawingContext dc, BitmapSource source);
        public abstract EditorItem Clone();
        public abstract void Move(Vector delta);

        // 按新包围盒缩放（线/笔按比例映射内部点）
        public abstract void SetBounds(Rect bounds);

        public virtual bool HitTest(Point point, double tolerance)
        {
            Rect bounds = Bounds;
            bounds.Inflate(tolerance, tolerance);
            return bounds.Contains(point);
        }

        protected Pen StrokePen()
        {
            var brush = new SolidColorBrush(Stroke);
            brush.Freeze();
            var pen = new Pen(brush, Math.Max(1, Thickness));
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
            return pen;
        }

        protected Brush FillBrush()
        {
            if (Fill.A == 0) return null;
            var brush = new SolidColorBrush(Fill);
            brush.Freeze();
            return brush;
        }

        protected static void ApplyOpacity(DrawingContext dc, double opacity, Action<DrawingContext> draw)
        {
            if (opacity >= 0.999) { draw(dc); return; }
            dc.PushOpacity(Math.Max(0.02, opacity));
            draw(dc);
            dc.Pop();
        }
    }

    // ---------------------------------------------------------------- 矩形
    internal sealed class RectItem : EditorItem
    {
        public Rect Rect;
        public bool Rounded;

        public override Rect Bounds { get { return Rect; } }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                double radius = Rounded ? Math.Min(12, Math.Min(Rect.Width, Rect.Height) * 0.22) : 0;
                d.DrawRoundedRectangle(FillBrush(), StrokePen(), Rect, radius, radius);
            });
        }

        public override EditorItem Clone()
        {
            return new RectItem { Rect = Rect, Rounded = Rounded, Thickness = Thickness, Stroke = Stroke, Fill = Fill, Opacity = Opacity };
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); }
        public override void SetBounds(Rect bounds) { Rect = bounds; }
    }

    // ---------------------------------------------------------------- 椭圆
    internal sealed class EllipseItem : EditorItem
    {
        public Rect Rect;

        public override Rect Bounds { get { return Rect; } }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                d.DrawEllipse(FillBrush(), StrokePen(), new Point(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2), Rect.Width / 2, Rect.Height / 2);
            });
        }

        public override EditorItem Clone()
        {
            return new EllipseItem { Rect = Rect, Thickness = Thickness, Stroke = Stroke, Fill = Fill, Opacity = Opacity };
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); }
        public override void SetBounds(Rect bounds) { Rect = bounds; }
    }

    // ---------------------------------------------------------------- 直线 / 箭头
    internal sealed class LineItem : EditorItem
    {
        public Point Start;
        public Point End;
        public bool Arrow;

        public override Rect Bounds
        {
            get
            {
                double left = Math.Min(Start.X, End.X);
                double top = Math.Min(Start.Y, End.Y);
                return new Rect(left, top, Math.Abs(End.X - Start.X), Math.Abs(End.Y - Start.Y));
            }
        }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                d.DrawLine(StrokePen(), Start, End);
                if (!Arrow) return;

                double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
                double head = Math.Max(10, Thickness * 3.6);
                var p1 = new Point(End.X - head * Math.Cos(angle - 0.42), End.Y - head * Math.Sin(angle - 0.42));
                var p2 = new Point(End.X - head * Math.Cos(angle + 0.42), End.Y - head * Math.Sin(angle + 0.42));
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(End, true, true);
                    ctx.LineTo(p1, true, false);
                    ctx.LineTo(p2, true, false);
                }
                geometry.Freeze();
                var brush = new SolidColorBrush(Stroke);
                brush.Freeze();
                d.DrawGeometry(brush, null, geometry);
            });
        }

        public override EditorItem Clone()
        {
            return new LineItem { Start = Start, End = End, Arrow = Arrow, Thickness = Thickness, Stroke = Stroke, Opacity = Opacity };
        }

        public override void Move(Vector delta)
        {
            Start = new Point(Start.X + delta.X, Start.Y + delta.Y);
            End = new Point(End.X + delta.X, End.Y + delta.Y);
        }

        public override void SetBounds(Rect bounds)
        {
            Rect old = Bounds;
            double sx = old.Width < 0.01 ? 1 : bounds.Width / old.Width;
            double sy = old.Height < 0.01 ? 1 : bounds.Height / old.Height;
            Start = new Point(bounds.X + (Start.X - old.X) * sx, bounds.Y + (Start.Y - old.Y) * sy);
            End = new Point(bounds.X + (End.X - old.X) * sx, bounds.Y + (End.Y - old.Y) * sy);
        }

        public override bool HitTest(Point point, double tolerance)
        {
            return DistanceToSegment(point, Start, End) <= Math.Max(6, Thickness + tolerance);
        }

        internal static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = dx * dx + dy * dy;
            if (length < 0.0001) return (p - a).Length;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length;
            t = Math.Max(0, Math.Min(1, t));
            return (p - new Point(a.X + t * dx, a.Y + t * dy)).Length;
        }
    }

    // ---------------------------------------------------------------- 自由笔
    internal sealed class PenItem : EditorItem
    {
        public readonly List<Point> Points = new List<Point>();

        public override Rect Bounds
        {
            get
            {
                if (Points.Count == 0) return Rect.Empty;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (Point p in Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
                double pad = Math.Max(1, Thickness / 2);
                return new Rect(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
            }
        }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            if (Points.Count < 2) return;
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(Points[0], false, false);
                    for (int i = 1; i < Points.Count; i++) ctx.LineTo(Points[i], true, false);
                }
                geometry.Freeze();
                d.DrawGeometry(null, StrokePen(), geometry);
            });
        }

        public override EditorItem Clone()
        {
            var copy = new PenItem { Thickness = Thickness, Stroke = Stroke, Opacity = Opacity };
            copy.Points.AddRange(Points);
            return copy;
        }

        public override void Move(Vector delta)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X + delta.X, Points[i].Y + delta.Y);
        }

        public override void SetBounds(Rect bounds)
        {
            Rect old = Bounds;
            if (old.IsEmpty || old.Width < 0.01 || old.Height < 0.01) return;
            double sx = bounds.Width / old.Width;
            double sy = bounds.Height / old.Height;
            for (int i = 0; i < Points.Count; i++)
            {
                Points[i] = new Point(bounds.X + (Points[i].X - old.X) * sx, bounds.Y + (Points[i].Y - old.Y) * sy);
            }
        }

        public override bool HitTest(Point point, double tolerance)
        {
            double limit = Math.Max(7, Thickness + tolerance);
            for (int i = 1; i < Points.Count; i++)
            {
                if (LineItem.DistanceToSegment(point, Points[i - 1], Points[i]) <= limit) return true;
            }
            return false;
        }
    }

    // ---------------------------------------------------------------- 文本
    internal sealed class TextItem : EditorItem
    {
        public string Text = "";
        public Rect Box;                 // 文本框：位置 + 宽度（高度按内容算）
        public double FontSize = 22;
        public bool Bold;
        public bool Italic;
        public double MeasuredHeight;

        public override Rect Bounds
        {
            get
            {
                double height = MeasuredHeight > 0 ? MeasuredHeight : FontSize * 1.4;
                return new Rect(Box.X, Box.Y, Math.Max(20, Box.Width), height);
            }
        }

        FormattedText Build()
        {
            var typeface = new Typeface(
                new FontFamily("Microsoft YaHei UI"),
                Italic ? FontStyles.Italic : FontStyles.Normal,
                Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            var brush = new SolidColorBrush(Stroke);
            brush.Freeze();
            double maxWidth = Box.Width > 8 ? Box.Width : 4000;
            var text = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, FontSize, brush, 1.0);
            text.MaxTextWidth = maxWidth;
            text.MaxTextHeight = 100000;
            return text;
        }

        public void Layout()
        {
            if (string.IsNullOrEmpty(Text)) { MeasuredHeight = FontSize * 1.4; return; }
            FormattedText text = Build();
            MeasuredHeight = text.Height;
        }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            if (string.IsNullOrEmpty(Text)) return;
            FormattedText text = Build();
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d) { d.DrawText(text, new Point(Box.X, Box.Y)); });
        }

        public override EditorItem Clone()
        {
            return new TextItem
            {
                Text = Text,
                Box = Box,
                FontSize = FontSize,
                Bold = Bold,
                Italic = Italic,
                MeasuredHeight = MeasuredHeight,
                Thickness = Thickness,
                Stroke = Stroke,
                Opacity = Opacity
            };
        }

        public override void Move(Vector delta)
        {
            Box = new Rect(Box.X + delta.X, Box.Y + delta.Y, Box.Width, Box.Height);
        }

        public override void SetBounds(Rect bounds)
        {
            Box = new Rect(bounds.X, bounds.Y, Math.Max(20, bounds.Width), Box.Height);
            Layout();
        }

        public override bool HitTest(Point point, double tolerance)
        {
            Rect bounds = Bounds;
            bounds.Inflate(4, 4);
            return bounds.Contains(point);
        }
    }

    // ---------------------------------------------------------------- 对话气泡
    internal sealed class BubbleItem : EditorItem
    {
        public Rect Rect;
        public bool Tail = true;

        public override Rect Bounds { get { return Rect; } }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                double radius = Math.Min(14, Math.Min(Rect.Width, Rect.Height) * 0.28);
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    double tailWidth = Tail ? Math.Min(Rect.Width * 0.28, 26) : 0;
                    double tailHeight = Tail ? Math.Min(Rect.Height * 0.28, 20) : 0;

                    ctx.BeginFigure(new Point(Rect.X + radius, Rect.Y), true, true);
                    ctx.LineTo(new Point(Rect.Right - radius, Rect.Y), true, false);
                    ctx.ArcTo(new Point(Rect.Right, Rect.Y + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.LineTo(new Point(Rect.Right, Rect.Bottom - radius), true, false);
                    ctx.ArcTo(new Point(Rect.Right - radius, Rect.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                    if (tailWidth > 0 && tailHeight > 0)
                    {
                        double tailX = Rect.X + Rect.Width * 0.28;
                        ctx.LineTo(new Point(tailX + tailWidth, Rect.Bottom), true, false);
                        ctx.LineTo(new Point(tailX + tailWidth * 0.25, Rect.Bottom + tailHeight), true, false);
                        ctx.LineTo(new Point(tailX, Rect.Bottom), true, false);
                    }
                    ctx.LineTo(new Point(Rect.X + radius, Rect.Bottom), true, false);
                    ctx.ArcTo(new Point(Rect.X, Rect.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.LineTo(new Point(Rect.X, Rect.Y + radius), true, false);
                    ctx.ArcTo(new Point(Rect.X + radius, Rect.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                geometry.Freeze();
                d.DrawGeometry(FillBrush(), StrokePen(), geometry);
            });
        }

        public override EditorItem Clone()
        {
            return new BubbleItem { Rect = Rect, Tail = Tail, Thickness = Thickness, Stroke = Stroke, Fill = Fill, Opacity = Opacity };
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); }
        public override void SetBounds(Rect bounds) { Rect = bounds; }
    }

    // ---------------------------------------------------------------- 步骤编号
    internal sealed class StepItem : EditorItem
    {
        public Rect Rect;
        public int Number = 1;

        public override Rect Bounds { get { return Rect; } }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                var fill = new SolidColorBrush(Stroke);
                fill.Freeze();
                d.DrawEllipse(fill, null, new Point(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2), Rect.Width / 2, Rect.Height / 2);

                var white = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                white.Freeze();
                var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var text = new FormattedText(Number.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, Math.Max(10, Rect.Height * 0.56), white, 1.0);
                d.DrawText(text, new Point(Rect.X + (Rect.Width - text.Width) / 2, Rect.Y + (Rect.Height - text.Height) / 2));
            });
        }

        public override EditorItem Clone()
        {
            return new StepItem { Rect = Rect, Number = Number, Thickness = Thickness, Stroke = Stroke, Opacity = Opacity };
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); }
        public override void SetBounds(Rect bounds) { Rect = bounds; }

        public override bool HitTest(Point point, double tolerance)
        {
            var center = new Point(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);
            double radius = Math.Min(Rect.Width, Rect.Height) / 2 + tolerance;
            return (point - center).Length <= radius;
        }
    }

    // ---------------------------------------------------------------- 高亮
    internal sealed class HighlightItem : EditorItem
    {
        public Rect Rect;

        public override Rect Bounds { get { return Rect; } }

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            var brush = new SolidColorBrush(Fill.A == 0 ? Color.FromArgb(90, 255, 226, 66) : Fill);
            brush.Freeze();
            ApplyOpacity(dc, Opacity, delegate(DrawingContext d)
            {
                d.DrawRectangle(brush, null, Rect);
            });
        }

        public override EditorItem Clone()
        {
            return new HighlightItem { Rect = Rect, Thickness = Thickness, Stroke = Stroke, Fill = Fill, Opacity = Opacity };
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); }
        public override void SetBounds(Rect bounds) { Rect = bounds; }
    }

    // ---------------------------------------------------------------- 马赛克 / 模糊（区域效果）
    internal abstract class RegionEffectItem : EditorItem
    {
        public Rect Rect;
        protected BitmapSource Cached;
        protected Rect CachedFor;

        public override Rect Bounds { get { return Rect; } }

        public void Invalidate()
        {
            Cached = null;
            CachedFor = Rect.Empty;
        }

        protected abstract BitmapSource Build(BitmapSource source, Int32Rect region, int width, int height);

        public override void Draw(DrawingContext dc, BitmapSource source)
        {
            if (source == null || Rect.Width < 2 || Rect.Height < 2) return;

            int x = (int)Math.Floor(Rect.X);
            int y = (int)Math.Floor(Rect.Y);
            int w = (int)Math.Ceiling(Rect.Width);
            int h = (int)Math.Ceiling(Rect.Height);
            x = Math.Max(0, Math.Min(source.PixelWidth - 1, x));
            y = Math.Max(0, Math.Min(source.PixelHeight - 1, y));
            w = Math.Max(1, Math.Min(source.PixelWidth - x, w));
            h = Math.Max(1, Math.Min(source.PixelHeight - y, h));

            if (Cached == null || CachedFor != Rect)
            {
                Cached = Build(source, new Int32Rect(x, y, w, h), w, h);
                CachedFor = Rect;
            }
            if (Cached != null)
            {
                // 缓存图已按区域原尺寸生成，1:1 绘制即可，不需要额外缩放
                dc.DrawImage(Cached, new Rect(x, y, w, h));
            }
        }

        public override void Move(Vector delta) { Rect = new Rect(Rect.X + delta.X, Rect.Y + delta.Y, Rect.Width, Rect.Height); Invalidate(); }
        public override void SetBounds(Rect bounds) { Rect = bounds; Invalidate(); }

        protected static byte[] ReadPixels(BitmapSource source, Int32Rect region)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            int stride = region.Width * 4;
            var buffer = new byte[stride * region.Height];
            converted.CopyPixels(region, buffer, stride, 0);
            return buffer;
        }
    }

    internal sealed class PixelateItem : RegionEffectItem
    {
        public int Block = 10;

        protected override BitmapSource Build(BitmapSource source, Int32Rect region, int width, int height)
        {
            byte[] pixels = ReadPixels(source, region);
            return ImageEffects.Pixelate(pixels, width, height, Block);
        }

        public override EditorItem Clone()
        {
            return new PixelateItem { Rect = Rect, Block = Block, Thickness = Thickness, Opacity = Opacity };
        }
    }

    internal sealed class BlurItem : RegionEffectItem
    {
        public int Radius = 6;

        protected override BitmapSource Build(BitmapSource source, Int32Rect region, int width, int height)
        {
            byte[] pixels = ReadPixels(source, region);
            return ImageEffects.Blur(pixels, width, height, Radius);
        }

        public override EditorItem Clone()
        {
            return new BlurItem { Rect = Rect, Radius = Radius, Thickness = Thickness, Opacity = Opacity };
        }
    }
}
