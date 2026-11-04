using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace IslandShot
{
    internal static class ScreenCapture
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        public static Rectangle VirtualScreen()
        {
            return System.Windows.Forms.SystemInformation.VirtualScreen;
        }

        public static Rectangle? ActiveWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            RECT rect;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out rect)) return null;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 8 || height < 8) return null;
            return new Rectangle(rect.Left, rect.Top, width, height);
        }

        public static Bitmap CaptureBitmap(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }
        public static string Capture(Rectangle bounds, string folder)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}
