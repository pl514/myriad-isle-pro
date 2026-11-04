using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace IslandShot.Probe
{
    // Exercises the exact IDataObject used by the drag without any UI:
    // asks for every offered format the way a native drop target would and
    // validates the HGLOBAL payload bytes.
    internal static class PayloadTest
    {
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormatW(string format);

        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            string path = args.Length > 0 ? args[0] : @"E:\deepseek1\IslandShot\build\data\images\sample.png";
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("payload test | " + path);

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            DragPayload payload = DragPayload.Create(path, image);
            ComDataObject data = new DragDataObject(payload);

            Console.WriteLine("source image: " + image.PixelWidth + "x" + image.PixelHeight);
            Console.WriteLine("offered formats: " + string.Join(", ", eFormats(data)));

            Check("CF_HDROP", data, DragNative.CF_HDROP, delegate(byte[] bytes)
            {
                Validate(bytes.Length > 20 && BitConverter.ToInt32(bytes, 0) == 20, "DROPFILES header");
                Validate(BitConverter.ToInt32(bytes, 16) == 1, "fWide flag");
                string text = Encoding.Unicode.GetString(bytes, 20, bytes.Length - 20).TrimEnd('\0');
                Validate(text == path, "path payload = " + text);
            });

            Check("CF_DIB", data, DragNative.CF_DIB, delegate(byte[] bytes)
            {
                Validate(bytes.Length >= 40, "DIB >= BITMAPINFOHEADER");
                int headerSize = BitConverter.ToInt32(bytes, 0);
                int width = BitConverter.ToInt32(bytes, 4);
                int height = BitConverter.ToInt32(bytes, 8);
                int bitCount = BitConverter.ToInt16(bytes, 14);
                int compression = BitConverter.ToInt32(bytes, 16);
                Validate(headerSize == 40, "biSize=" + headerSize);
                Validate(width == image.PixelWidth && height == image.PixelHeight,
                    "dimensions " + width + "x" + height);
                Validate(bitCount == 24, "bitCount=" + bitCount);
                Validate(compression == 0, "compression=" + compression);
                int stride = ((width * 24 + 31) / 32) * 4;
                Validate(bytes.Length == 40 + stride * height, "pixel payload size");
            });

            Check("PNG", data, (int)RegisterClipboardFormatW("PNG"), delegate(byte[] bytes)
            {
                Validate(bytes.Length > 8, "PNG bytes");
                Validate(bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47, "PNG signature");
            });

            Check("image/png", data, (int)RegisterClipboardFormatW("image/png"), delegate(byte[] bytes)
            {
                Validate(bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50, "PNG signature");
            });

            Check("Preferred DropEffect", data, (int)RegisterClipboardFormatW("Preferred DropEffect"), delegate(byte[] bytes)
            {
                Validate(bytes.Length == 4 && BitConverter.ToInt32(bytes, 0) == 1, "DROPEFFECT_COPY");
            });

            Console.WriteLine(failures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }

        static string eFormats(ComDataObject data)
        {
            StringBuilder builder = new StringBuilder();
            IEnumFORMATETC enumerator = data.EnumFormatEtc(DATADIR.DATADIR_GET);
            FORMATETC[] one = new FORMATETC[1];
            int[] fetched = new int[1];
            while (enumerator.Next(1, one, fetched) == 0)
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(one[0].cfFormat);
            }
            return builder.ToString();
        }

        static void Check(string name, ComDataObject data, int format, Action<byte[]> validate)
        {
            FORMATETC request = new FORMATETC();
            request.cfFormat = (short)format;
            request.dwAspect = DVASPECT.DVASPECT_CONTENT;
            request.lindex = -1;
            request.tymed = TYMED.TYMED_HGLOBAL;
            try
            {
                Validate(data.QueryGetData(ref request) == 0, name + " QueryGetData");
                STGMEDIUM medium;
                data.GetData(ref request, out medium);
                try
                {
                    Validate(medium.tymed == TYMED.TYMED_HGLOBAL, name + " medium type");
                    IntPtr pointer = GlobalLock(medium.unionmember);
                    Validate(pointer != IntPtr.Zero, name + " GlobalLock");
                    try
                    {
                        int size = (int)GlobalSize(medium.unionmember).ToUInt32();
                        byte[] bytes = new byte[size];
                        Marshal.Copy(pointer, bytes, 0, size);
                        validate(bytes);
                        Console.WriteLine("OK   " + name + " (" + size + " bytes)");
                    }
                    finally { GlobalUnlock(medium.unionmember); }
                }
                finally { ReleaseStgMedium(ref medium); }
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine("FAIL " + name + ": " + exception.Message);
            }
        }

        [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);

        static void Validate(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }
    }
}

