using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Threading;

namespace IslandShot
{
    internal sealed class ImageTranslationPair
    {
        public string Source;
        public string Target;
    }

    internal sealed class ImageTranslationResult
    {
        public string OutputPath;
        public int LineCount;
        public int TranslatedCount;
        public readonly List<ImageTranslationPair> Pairs = new List<ImageTranslationPair>();
    }

    // 图片翻译：OCR 定位每一行文字 → 翻译 → 擦掉原文 → 在原位置写回译文。
    // 参考微信/手机「图片翻译」的做法，尽量保持版面不变。
    internal static class ImageTranslator
    {
        const int MaxLines = 40;          // 一次最多处理多少行，防止一次调用太多次翻译接口
        const int ChunkSize = 5;          // 每 5 行拼成一次请求

        public static ImageTranslationResult Translate(string imagePath, Action<string> status)
        {
            var result = new ImageTranslationResult();
            if (status != null) status("正在识别文字…");

            List<OcrLineInfo> lines = OcrService.Recognize(imagePath);
            result.LineCount = lines.Count;

            // 中文引擎读英文会出现 "HeIIo"、"P ease" 这种碎片：
            // 统计一下字符构成，拉丁字母明显更多时换成英文引擎重新识别。
            int latinChars = 0, cjkChars = 0;
            foreach (OcrLineInfo line in lines)
            {
                foreach (char c in line.Text)
                {
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latinChars++;
                    else if (c >= 0x4E00 && c <= 0x9FFF) cjkChars++;
                }
            }
            if (latinChars > cjkChars * 3 && latinChars > 20)
            {
                try
                {
                    List<OcrLineInfo> englishLines = OcrService.Recognize(imagePath, "en-US");
                    if (englishLines.Count > 0) lines = englishLines;
                }
                catch (Exception) { }
            }

            // 默认方向：外文 → 中文。
            // 只翻译"含拉丁字母"的行，纯中文行原样保留；整图没有外文时才退回中文→英文。
            var targets = new List<OcrLineInfo>();
            foreach (OcrLineInfo line in lines)
            {
                if (line.Text.Trim().Length == 0) continue;
                if (!HasLatin(line.Text)) continue;
                targets.Add(line);
                if (targets.Count >= MaxLines) break;
            }

            string pair = "en|zh-CN";
            if (targets.Count == 0)
            {
                foreach (OcrLineInfo line in lines)
                {
                    if (line.Text.Trim().Length == 0) continue;
                    if (!line.HasChinese) continue;
                    targets.Add(line);
                    if (targets.Count >= MaxLines) break;
                }
                pair = "zh-CN|en";
            }

            if (targets.Count == 0)
            {
                throw new Exception("这张图里没识别到可翻译的文字");
            }

            if (status != null)
            {
                status("正在翻译 " + targets.Count + " 行（" + (pair == "en|zh-CN" ? "外文 → 中文" : "中文 → 英文") + "）…");
            }
            var translations = TranslateLines(targets, pair, status);

            if (status != null) status("正在生成图片…");
            string output = Render(imagePath, targets, translations, result);
            result.OutputPath = output;
            result.TranslatedCount = translations.Count;

            for (int i = 0; i < targets.Count && i < translations.Count; i++)
            {
                result.Pairs.Add(new ImageTranslationPair { Source = targets[i].Text, Target = translations[i] });
            }
            return result;
        }

        static bool HasLatin(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
            }
            return false;
        }

        static List<string> TranslateLines(List<OcrLineInfo> lines, string pair, Action<string> status)
        {
            var results = new List<string>();
            int index = 0;

            while (index < lines.Count)
            {
                int count = Math.Min(ChunkSize, lines.Count - index);
                var builder = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) builder.Append('\n');
                    builder.Append(lines[index + i].Text);
                }

                string chunkResult = null;
                try
                {
                    chunkResult = TranslateService.TranslateAsync(builder.ToString(), pair)
                        .GetAwaiter().GetResult();
                }
                catch (Exception) { }

                string[] parts = (chunkResult ?? "").Replace("\r", "").Split('\n');
                bool usable = parts.Length == count;
                for (int i = 0; i < count; i++)
                {
                    if (usable && parts[i].Trim().Length > 0) results.Add(parts[i].Trim());
                    else
                    {
                        // 批量失败就单行再试一次
                        string single = null;
                        try
                        {
                            single = TranslateService.TranslateAsync(lines[index + i].Text, pair)
                                .GetAwaiter().GetResult();
                        }
                        catch (Exception) { }
                        results.Add(string.IsNullOrWhiteSpace(single) ? lines[index + i].Text : single.Trim());
                    }
                }

                index += count;
                if (status != null) status("已翻译 " + Math.Min(index, lines.Count) + " / " + lines.Count + " 行…");
                Thread.Sleep(120);
            }
            return results;
        }

        static string Render(string imagePath, List<OcrLineInfo> lines, List<string> translations, ImageTranslationResult result)
        {
            using (var bitmap = new Bitmap(imagePath))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                for (int i = 0; i < lines.Count && i < translations.Count; i++)
                {
                    OcrLineInfo line = lines[i];
                    var rect = new Rectangle(
                        (int)Math.Max(0, line.X - 2),
                        (int)Math.Max(0, line.Y - 1),
                        (int)Math.Min(bitmap.Width, line.Width + 4),
                        (int)Math.Min(bitmap.Height, line.Height + 2));
                    if (rect.Width < 4 || rect.Height < 4) continue;

                    Color background = SampleBackground(bitmap, rect);
                    using (var brush = new SolidBrush(background))
                    {
                        graphics.FillRectangle(brush, rect);
                    }

                    Color textColor = IsLight(background) ? Color.FromArgb(30, 30, 34) : Color.White;
                    DrawFittedText(graphics, translations[i], rect, textColor);
                }

                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "translate-img");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string output = Path.Combine(folder,
                    Path.GetFileNameWithoutExtension(imagePath) + "-译-" + DateTime.Now.ToString("HHmmss") + ".png");
                bitmap.Save(output, ImageFormat.Png);
                return output;
            }
        }

        // 取矩形四周一圈像素里出现最多的颜色，作为要擦除区域的底色
        static Color SampleBackground(Bitmap bitmap, Rectangle rect)
        {
            var counts = new Dictionary<int, int>();
            int best = -1, bestCount = 0;

            int stepX = Math.Max(1, rect.Width / 12);
            int stepY = Math.Max(1, rect.Height / 4);

            for (int x = rect.Left; x < rect.Right; x += stepX)
            {
                AddPixel(bitmap, x, Math.Max(0, rect.Top - 1), counts, ref best, ref bestCount);
                AddPixel(bitmap, x, Math.Min(bitmap.Height - 1, rect.Bottom), counts, ref best, ref bestCount);
            }
            for (int y = rect.Top; y < rect.Bottom; y += stepY)
            {
                AddPixel(bitmap, Math.Max(0, rect.Left - 1), y, counts, ref best, ref bestCount);
                AddPixel(bitmap, Math.Min(bitmap.Width - 1, rect.Right), y, counts, ref best, ref bestCount);
            }

            if (best < 0) return Color.White;
            return Color.FromArgb((best >> 16) & 0xFF, (best >> 8) & 0xFF, best & 0xFF);
        }

        static void AddPixel(Bitmap bitmap, int x, int y, Dictionary<int, int> counts, ref int best, ref int bestCount)
        {
            try
            {
                Color c = bitmap.GetPixel(x, y);
                int key = (c.R << 16) | (c.G << 8) | c.B;
                int count;
                if (!counts.TryGetValue(key, out count)) count = 0;
                count++;
                counts[key] = count;
                if (count > bestCount) { bestCount = count; best = key; }
            }
            catch (Exception) { }
        }

        static bool IsLight(Color color)
        {
            return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) > 150;
        }

        static void DrawFittedText(Graphics graphics, string text, Rectangle rect, Color color)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            float size = Math.Max(7f, rect.Height * 0.78f);
            string family = "Microsoft YaHei UI";

            for (int attempt = 0; attempt < 8; attempt++)
            {
                using (var font = new Font(family, size, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(color))
                using (var format = new StringFormat { Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap })
                {
                    SizeF measured = graphics.MeasureString(text, font, int.MaxValue, format);
                    if (measured.Width <= rect.Width + 1f || size <= 7f)
                    {
                        graphics.DrawString(text, font, brush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), format);
                        return;
                    }
                    size *= Math.Max(0.55f, (rect.Width + 1f) / measured.Width);
                    size = Math.Max(7f, size);
                }
            }
        }
    }
}
