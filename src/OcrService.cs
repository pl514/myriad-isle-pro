using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace IslandShot
{
    internal sealed class OcrLineInfo
    {
        public string Text;
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public bool HasChinese
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return false;
                foreach (char c in Text)
                {
                    if (c >= 0x4E00 && c <= 0x9FFF) return true;
                }
                return false;
            }
        }
    }

    // Windows 自带的 OCR（Windows.Media.Ocr），未打包程序也能用：
    // 注意不能用 StorageFile（没有 broadFileSystemAccess），要自己读字节塞进内存流。
    internal static class OcrService
    {
        static readonly Dictionary<string, OcrEngine> engines = new Dictionary<string, OcrEngine>();

        public static bool IsAvailable
        {
            get { return CreateEngine("zh-Hans") != null; }
        }

        static OcrEngine CreateEngine(string languageTag)
        {
            OcrEngine cached;
            if (engines.TryGetValue(languageTag, out cached)) return cached;

            try
            {
                OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                if (engine == null && languageTag != "zh-Hans")
                {
                    engine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"));
                }
                if (engine != null) engines[languageTag] = engine;
                return engine;
            }
            catch (Exception) { return null; }
        }

        public static List<OcrLineInfo> Recognize(string imagePath)
        {
            return Recognize(imagePath, "zh-Hans");
        }

        public static List<OcrLineInfo> Recognize(string imagePath, string languageTag)
        {
            var results = new List<OcrLineInfo>();
            OcrEngine ocr = CreateEngine(languageTag);
            if (ocr == null) throw new Exception("系统没有可用的 OCR 语言包");

            byte[] bytes = File.ReadAllBytes(imagePath);
            var memoryStream = new InMemoryRandomAccessStream();
            memoryStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(bytes)).AsTask().GetAwaiter().GetResult();
            memoryStream.Seek(0);

            using (IRandomAccessStream stream = memoryStream)
            {
                BitmapDecoder decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                SoftwareBitmap bitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
                OcrResult result = ocr.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();

                foreach (OcrLine line in result.Lines)
                {
                    if (line.Words.Count == 0) continue;
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
                    foreach (OcrWord word in line.Words)
                    {
                        minX = Math.Min(minX, word.BoundingRect.X);
                        minY = Math.Min(minY, word.BoundingRect.Y);
                        maxX = Math.Max(maxX, word.BoundingRect.X + word.BoundingRect.Width);
                        maxY = Math.Max(maxY, word.BoundingRect.Y + word.BoundingRect.Height);
                    }
                    results.Add(new OcrLineInfo
                    {
                        Text = line.Text,
                        X = minX,
                        Y = minY,
                        Width = Math.Max(4, maxX - minX),
                        Height = Math.Max(4, maxY - minY)
                    });
                }
            }
            return results;
        }
    }
}
