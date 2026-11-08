using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandShot
{
    // 像素级图像处理：区域效果（马赛克/模糊）与全局效果（调整/变换）。
    // 全部按 BGRA32 字节数组实现，不依赖任何外部库。
    internal static class ImageEffects
    {
        // ------------------------------------------------------------ 基础转换
        internal static BitmapSource ToBgra32(BitmapSource source)
        {
            if (source == null) return null;
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }

        internal static byte[] GetPixels(BitmapSource source, out int width, out int height)
        {
            BitmapSource bgra = ToBgra32(source);
            width = bgra.PixelWidth;
            height = bgra.PixelHeight;
            int stride = width * 4;
            var buffer = new byte[stride * height];
            bgra.CopyPixels(buffer, stride, 0);
            return buffer;
        }

        internal static BitmapSource FromPixels(byte[] pixels, int width, int height)
        {
            int stride = width * 4;
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }

        static byte Clamp(double value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        // ------------------------------------------------------------ 区域：像素化
        internal static BitmapSource Pixelate(byte[] pixels, int width, int height, int block)
        {
            if (block < 2) block = 2;
            var result = new byte[pixels.Length];
            for (int by = 0; by < height; by += block)
            {
                for (int bx = 0; bx < width; bx += block)
                {
                    int xEnd = Math.Min(bx + block, width);
                    int yEnd = Math.Min(by + block, height);

                    long b = 0, g = 0, r = 0, a = 0;
                    int count = 0;
                    for (int y = by; y < yEnd; y++)
                    {
                        int rowOffset = y * width * 4;
                        for (int x = bx; x < xEnd; x++)
                        {
                            int index = rowOffset + x * 4;
                            b += pixels[index];
                            g += pixels[index + 1];
                            r += pixels[index + 2];
                            a += pixels[index + 3];
                            count++;
                        }
                    }
                    if (count == 0) continue;
                    byte avgB = (byte)(b / count);
                    byte avgG = (byte)(g / count);
                    byte avgR = (byte)(r / count);
                    byte avgA = (byte)(a / count);

                    for (int y = by; y < yEnd; y++)
                    {
                        int rowOffset = y * width * 4;
                        for (int x = bx; x < xEnd; x++)
                        {
                            int index = rowOffset + x * 4;
                            result[index] = avgB;
                            result[index + 1] = avgG;
                            result[index + 2] = avgR;
                            result[index + 3] = avgA;
                        }
                    }
                }
            }
            return FromPixels(result, width, height);
        }

        // ------------------------------------------------------------ 区域：模糊（两次盒式模糊近似高斯）
        internal static BitmapSource Blur(byte[] pixels, int width, int height, int radius)
        {
            if (radius < 1) radius = 1;
            byte[] current = (byte[])pixels.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                current = BoxBlurPass(current, width, height, Math.Max(1, radius / 2));
            }
            return FromPixels(current, width, height);
        }

        static byte[] BoxBlurPass(byte[] pixels, int width, int height, int radius)
        {
            var horizontal = new byte[pixels.Length];
            var result = new byte[pixels.Length];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0, count = 0;
                    int from = Math.Max(0, x - radius);
                    int to = Math.Min(width - 1, x + radius);
                    for (int sx = from; sx <= to; sx++)
                    {
                        int index = rowOffset + sx * 4;
                        b += pixels[index];
                        g += pixels[index + 1];
                        r += pixels[index + 2];
                        a += pixels[index + 3];
                        count++;
                    }
                    int target = rowOffset + x * 4;
                    horizontal[target] = (byte)(b / count);
                    horizontal[target + 1] = (byte)(g / count);
                    horizontal[target + 2] = (byte)(r / count);
                    horizontal[target + 3] = (byte)(a / count);
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int b = 0, g = 0, r = 0, a = 0, count = 0;
                    int from = Math.Max(0, y - radius);
                    int to = Math.Min(height - 1, y + radius);
                    for (int sy = from; sy <= to; sy++)
                    {
                        int index = sy * width * 4 + x * 4;
                        b += horizontal[index];
                        g += horizontal[index + 1];
                        r += horizontal[index + 2];
                        a += horizontal[index + 3];
                        count++;
                    }
                    int target = y * width * 4 + x * 4;
                    result[target] = (byte)(b / count);
                    result[target + 1] = (byte)(g / count);
                    result[target + 2] = (byte)(r / count);
                    result[target + 3] = (byte)(a / count);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ 全局：调整
        internal static BitmapSource Grayscale(BitmapSource source)
        {
            return MapPixels(source, delegate(double b, double g, double r, out double ob, out double og, out double or)
            {
                double luma = 0.114 * b + 0.587 * g + 0.299 * r;
                ob = luma; og = luma; or = luma;
            });
        }

        internal static BitmapSource Invert(BitmapSource source)
        {
            return MapPixels(source, delegate(double b, double g, double r, out double ob, out double og, out double or)
            {
                ob = 255 - b; og = 255 - g; or = 255 - r;
            });
        }

        internal static BitmapSource Sepia(BitmapSource source)
        {
            return MapPixels(source, delegate(double b, double g, double r, out double ob, out double og, out double or)
            {
                ob = 0.131 * b + 0.534 * g + 0.272 * r;
                og = 0.168 * b + 0.686 * g + 0.349 * r;
                or = 0.189 * b + 0.769 * g + 0.393 * r;
            });
        }

        // brightness/contrast/saturation 都是 -100..100
        internal static BitmapSource Adjust(BitmapSource source, int brightness, int contrast, int saturation)
        {
            double bAdd = brightness * 1.28;
            double cFactor = (100.0 + contrast) / 100.0;
            cFactor = cFactor * cFactor;
            double sFactor = (100.0 + saturation) / 100.0;

            return MapPixels(source, delegate(double b, double g, double r, out double ob, out double og, out double or)
            {
                b = ((b - 128) * cFactor) + 128 + bAdd;
                g = ((g - 128) * cFactor) + 128 + bAdd;
                r = ((r - 128) * cFactor) + 128 + bAdd;

                double luma = 0.114 * b + 0.587 * g + 0.299 * r;
                b = luma + (b - luma) * sFactor;
                g = luma + (g - luma) * sFactor;
                r = luma + (r - luma) * sFactor;

                ob = b; og = g; or = r;
            });
        }

        internal static BitmapSource Sharpen(BitmapSource source, int amount)
        {
            if (amount <= 0) return source;
            int width, height;
            byte[] pixels = GetPixels(source, out width, out height);
            byte[] result = (byte[])pixels.Clone();
            double strength = amount / 100.0 * 1.4;

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int center = y * width * 4 + x * 4 + channel;
                        int sum = 0;
                        sum += pixels[center - 4];
                        sum += pixels[center + 4];
                        sum += pixels[center - width * 4];
                        sum += pixels[center + width * 4];
                        double average = sum / 4.0;
                        result[center] = Clamp(pixels[center] + (pixels[center] - average) * strength);
                    }
                }
            }
            return FromPixels(result, width, height);
        }

        internal static BitmapSource Vignette(BitmapSource source, int strength)
        {
            int width, height;
            byte[] pixels = GetPixels(source, out width, out height);
            if (strength <= 0) return FromPixels(pixels, width, height);

            double cx = width / 2.0;
            double cy = height / 2.0;
            double maxDistance = Math.Sqrt(cx * cx + cy * cy);
            double factor = strength / 100.0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    double distance = Math.Sqrt(dx * dx + dy * dy) / maxDistance;
                    double shade = 1 - factor * Math.Pow(distance, 2.2);
                    if (shade < 0) shade = 0;
                    int index = y * width * 4 + x * 4;
                    pixels[index] = Clamp(pixels[index] * shade);
                    pixels[index + 1] = Clamp(pixels[index + 1] * shade);
                    pixels[index + 2] = Clamp(pixels[index + 2] * shade);
                }
            }
            return FromPixels(pixels, width, height);
        }

        delegate void PixelMapper(double b, double g, double r, out double ob, out double og, out double or);

        static BitmapSource MapPixels(BitmapSource source, PixelMapper mapper)
        {
            int width, height;
            byte[] pixels = GetPixels(source, out width, out height);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double ob, og, or;
                mapper(pixels[i], pixels[i + 1], pixels[i + 2], out ob, out og, out or);
                pixels[i] = Clamp(ob);
                pixels[i + 1] = Clamp(og);
                pixels[i + 2] = Clamp(or);
            }
            return FromPixels(pixels, width, height);
        }

        // ------------------------------------------------------------ 全局：变换
        internal static BitmapSource Flip(BitmapSource source, bool horizontal)
        {
            var transform = horizontal
                ? new ScaleTransform(-1, 1)
                : new ScaleTransform(1, -1);
            var transformed = new TransformedBitmap(source, transform);
            transformed.Freeze();
            return transformed;
        }

        internal static BitmapSource Rotate90(BitmapSource source, bool clockwise)
        {
            var transformed = new TransformedBitmap(source, new RotateTransform(clockwise ? 90 : -90));
            transformed.Freeze();
            return transformed;
        }

        internal static BitmapSource Rotate180(BitmapSource source)
        {
            var transformed = new TransformedBitmap(source, new RotateTransform(180));
            transformed.Freeze();
            return transformed;
        }

        internal static BitmapSource Resize(BitmapSource source, int width, int height)
        {
            if (width < 1 || height < 1) return source;
            var scaled = new TransformedBitmap(source, new ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
            scaled.Freeze();
            return scaled;
        }

        internal static BitmapSource Crop(BitmapSource source, Int32Rect region)
        {
            int x = Math.Max(0, Math.Min(source.PixelWidth - 1, region.X));
            int y = Math.Max(0, Math.Min(source.PixelHeight - 1, region.Y));
            int w = Math.Max(1, Math.Min(source.PixelWidth - x, region.Width));
            int h = Math.Max(1, Math.Min(source.PixelHeight - y, region.Height));
            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            return cropped;
        }

        // 给整图加留白背景（分享图常用）：padding 内外边距，radius 圆角，shadow 阴影
        internal static BitmapSource AddBackground(BitmapSource source, Color background, int padding, int radius)
        {
            int width = source.PixelWidth + padding * 2;
            int height = source.PixelHeight + padding * 2;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var brush = new SolidColorBrush(background);
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));

                var clip = new RectangleGeometry(new Rect(padding, padding, source.PixelWidth, source.PixelHeight), radius, radius);
                dc.PushClip(clip);
                dc.DrawImage(source, new Rect(padding, padding, source.PixelWidth, source.PixelHeight));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
