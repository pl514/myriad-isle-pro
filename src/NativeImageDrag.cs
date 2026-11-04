using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

[assembly: ComVisible(true)]

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // Native OLE drag names / P/Invoke.
    //
    // The drag is started with ole32!DoDragDrop, i.e. the same operating
    // system Drag-Drop pipeline Explorer, WeChat, Word and Paint use.  No
    // synthetic mouse events are manufactured here: the OS owns the drag
    // loop, the drop target is resolved by OLE, and the payload is handed
    // over through CF_HDROP / CF_DIB / PNG clipboard formats.
    // ---------------------------------------------------------------------
    internal static class DragNative
    {
        public const int CF_BITMAP = 2;
        public const int CF_DIB = 8;
        public const int CF_HDROP = 15;
        public const int DVASPECT_CONTENT = 1;
        public const int TYMED_HGLOBAL = 1;

        public const int GMEM_MOVEABLE = 0x0002;
        public const int GMEM_ZEROINIT = 0x0040;

        public const int DROPEFFECT_COPY = 1;

        public const int MK_LBUTTON = 0x0001;
        public const int MK_RBUTTON = 0x0002;

        public const int S_OK = 0;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const int DV_E_FORMATETC = unchecked((int)0x80040064);
        public const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
        public const int DATA_S_SAMEFORMATETC = 0x00040130;

        // Successful drop / user cancelled the drag / "use default cursors".
        public const int DRAGDROP_S_DROP = 0x00040100;
        public const int DRAGDROP_S_CANCEL = 0x00040101;
        public const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_ASYNCWINDOWPOS = 0x4000;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("ole32.dll", PreserveSig = false, ExactSpelling = true)]
        public static extern void DoDragDrop(
            [MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject,
            [MarshalAs(UnmanagedType.Interface)] IDropSource dropSource,
            int allowedEffects,
            out int effect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint RegisterClipboardFormatW(string format);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, int flags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint milliseconds);

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "CapsuleImagePro-drag.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }

    [ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag(int escapePressed, int keyState);
        [PreserveSig] int GiveFeedback(int effect);
    }

    // ---------------------------------------------------------------------
    // Payload: one image rendered into every format a drop target can ask
    // for.  Built once, before the OS drag loop starts.
    // ---------------------------------------------------------------------
    internal sealed class DragPayload
    {
        public string SourcePath;
        public byte[] FileDropBytes;
        public byte[] DibBytes;
        public byte[] PngBytes;
        public int PngFormat;
        public int ImagePngFormat;
        public int PreferredEffectFormat;

        public static DragPayload Create(string path, BitmapSource full)
        {
            DragPayload payload = new DragPayload();
            payload.SourcePath = path;
            payload.FileDropBytes = BuildFileDrop(path);

            if (full != null)
            {
                try { payload.PngBytes = BuildPng(path, full); }
                catch (Exception exception) { DragNative.Log("png build failed: " + exception.Message); }
                try { payload.DibBytes = BuildDib(full); }
                catch (Exception exception) { DragNative.Log("dib build failed: " + exception.Message); }
            }

            payload.PngFormat = (int)DragNative.RegisterClipboardFormatW("PNG");
            payload.ImagePngFormat = (int)DragNative.RegisterClipboardFormatW("image/png");
            payload.PreferredEffectFormat = (int)DragNative.RegisterClipboardFormatW("Preferred DropEffect");
            return payload;
        }

        public List<FORMATETC> BuildFormats()
        {
            List<FORMATETC> formats = new List<FORMATETC>();
            formats.Add(Format(DragNative.CF_HDROP));
            if (DibBytes != null) formats.Add(Format(DragNative.CF_DIB));
            if (PngBytes != null)
            {
                formats.Add(Format(PngFormat));
                formats.Add(Format(ImagePngFormat));
            }
            formats.Add(Format(PreferredEffectFormat));
            return formats;
        }

        public byte[] BytesFor(int format)
        {
            // cfFormat is a 16-bit clipboard format id; registered formats have
            // the high bit set, so compare the unsigned value, not the short.
            int cf = format & 0xFFFF;
            if (cf == DragNative.CF_HDROP) return FileDropBytes;
            if (cf == DragNative.CF_DIB) return DibBytes;
            if (cf == (PngFormat & 0xFFFF)) return PngBytes;
            if (cf == (ImagePngFormat & 0xFFFF)) return PngBytes;
            if (cf == (PreferredEffectFormat & 0xFFFF)) return BitConverter.GetBytes(DragNative.DROPEFFECT_COPY);
            return null;
        }

        static FORMATETC Format(int clipboardFormat)
        {
            FORMATETC format = new FORMATETC();
            format.cfFormat = (short)clipboardFormat;
            format.dwAspect = DVASPECT.DVASPECT_CONTENT;
            format.lindex = -1;
            format.tymed = TYMED.TYMED_HGLOBAL;
            format.ptd = IntPtr.Zero;
            return format;
        }

        static byte[] BuildFileDrop(string path)
        {
            // DROPFILES + UTF-16 path list, double NUL terminated.
            string list = path + "\0";
            byte[] name = Encoding.Unicode.GetBytes(list + "\0");
            byte[] data = new byte[20 + name.Length];
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(20);       // pFiles
                writer.Write(0);        // pt.x
                writer.Write(0);        // pt.y
                writer.Write(0);        // fNC
                writer.Write(1);        // fWide
                writer.Write(name);
            }
            return data;
        }

        static byte[] BuildPng(string path, BitmapSource source)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                try { return File.ReadAllBytes(path); }
                catch (Exception) { }
            }
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        static byte[] BuildDib(BitmapSource source)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            BitmapSource bgr;
            if (source.Format == PixelFormats.Bgr24) bgr = source;
            else
            {
                FormatConvertedBitmap converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Bgr24;
                converted.EndInit();
                converted.Freeze();
                bgr = converted;
            }

            int stride = ((width * 24 + 31) / 32) * 4;
            long size = (long)stride * height;
            if (size > 160L * 1024 * 1024) return null;

            byte[] pixels = new byte[(int)size];
            bgr.CopyPixels(pixels, stride, 0);

            // BITMAPINFOHEADER (40 bytes) followed by bottom-up BGR rows.
            byte[] dib = new byte[40 + (int)size];
            using (MemoryStream stream = new MemoryStream(dib))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(40);           // biSize
                writer.Write(width);        // biWidth
                writer.Write(height);       // biHeight (positive = bottom-up)
                writer.Write((short)1);     // biPlanes
                writer.Write((short)24);    // biBitCount
                writer.Write(0);            // biCompression = BI_RGB
                writer.Write((int)size);    // biSizeImage
                writer.Write(0);            // biXPelsPerMeter
                writer.Write(0);            // biYPelsPerMeter
                writer.Write(0);            // biClrUsed
                writer.Write(0);            // biClrImportant
                for (int row = height - 1; row >= 0; row--)
                {
                    writer.Write(pixels, row * stride, stride);
                }
            }
            return dib;
        }
    }

    // ---------------------------------------------------------------------
    // IDataObject implementation.
    //
    // Every format is exposed as an HGLOBAL, so OLE can marshal the data to
    // another process (WeChat, Explorer, Word...) without our help.
    // ---------------------------------------------------------------------
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class DragDataObject : ComDataObject
    {
        readonly DragPayload payload;
        readonly List<FORMATETC> formats;

        public DragDataObject(DragPayload payload)
        {
            this.payload = payload;
            this.formats = payload.BuildFormats();
        }

        void ComDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = new STGMEDIUM();
            medium.pUnkForRelease = null;
            medium.tymed = TYMED.TYMED_HGLOBAL;

            byte[] bytes = payload.BytesFor(format.cfFormat);
            DragNative.Log("GetData cf=" + (format.cfFormat & 0xFFFF) + " aspect=" + (int)format.dwAspect
                + " tymed=" + (int)format.tymed + " -> " + (bytes == null ? "no data" : bytes.Length + " bytes"));
            if (bytes == null)
            {
                throw new COMException("IslandShot: unsupported drag format", DragNative.DV_E_FORMATETC);
            }

            IntPtr handle = DragNative.GlobalAlloc((uint)(DragNative.GMEM_MOVEABLE | DragNative.GMEM_ZEROINIT), (UIntPtr)(uint)bytes.Length);
            if (handle == IntPtr.Zero)
            {
                throw new COMException("IslandShot: cannot allocate drag memory", unchecked((int)0x8007000E));
            }

            IntPtr target = DragNative.GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                throw new COMException("IslandShot: cannot lock drag memory", unchecked((int)0x80004005));
            }
            Marshal.Copy(bytes, 0, target, bytes.Length);
            DragNative.GlobalUnlock(handle);
            medium.unionmember = handle;
        }

        void ComDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            throw new COMException("IslandShot: GetDataHere not supported", DragNative.E_NOTIMPL);
        }

        int ComDataObject.QueryGetData(ref FORMATETC format)
        {
            byte[] bytes = payload.BytesFor(format.cfFormat);
            if (bytes == null) return DragNative.DV_E_FORMATETC;
            if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) return DragNative.DV_E_FORMATETC;
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) return DragNative.DV_E_FORMATETC;
            return DragNative.S_OK;
        }

        int ComDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = IntPtr.Zero;
            return DragNative.DATA_S_SAMEFORMATETC;
        }

        void ComDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            throw new COMException("IslandShot: SetData not supported", DragNative.E_NOTIMPL);
        }

        IEnumFORMATETC ComDataObject.EnumFormatEtc(DATADIR direction)
        {
            if (direction == DATADIR.DATADIR_GET)
            {
                return new FormatEtcEnumerator(formats.ToArray());
            }
            throw new COMException("IslandShot: EnumFormatEtc(Set) not supported", DragNative.E_NOTIMPL);
        }

        int ComDataObject.DAdvise(ref FORMATETC format, ADVF advf, IAdviseSink sink, out int connection)
        {
            connection = 0;
            return DragNative.OLE_E_ADVISENOTSUPPORTED;
        }

        void ComDataObject.DUnadvise(int connection)
        {
            throw new COMException("IslandShot: DUnadvise not supported", DragNative.OLE_E_ADVISENOTSUPPORTED);
        }

        int ComDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null;
            return DragNative.OLE_E_ADVISENOTSUPPORTED;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class FormatEtcEnumerator : IEnumFORMATETC
    {
        readonly FORMATETC[] items;
        int index;

        public FormatEtcEnumerator(FORMATETC[] items)
        {
            this.items = items;
        }

        public int Next(int count, FORMATETC[] format, int[] fetched)
        {
            int copied = 0;
            while (copied < count && index < items.Length)
            {
                format[copied] = items[index];
                copied++;
                index++;
            }
            if (fetched != null && fetched.Length > 0) fetched[0] = copied;
            return copied == count ? DragNative.S_OK : 1; // S_FALSE
        }

        public int Skip(int count)
        {
            index += count;
            if (index > items.Length) index = items.Length;
            return DragNative.S_OK;
        }

        public int Reset()
        {
            index = 0;
            return DragNative.S_OK;
        }

        public void Clone(out IEnumFORMATETC newEnum)
        {
            FormatEtcEnumerator clone = new FormatEtcEnumerator(items);
            clone.index = index;
            newEnum = clone;
        }
    }

    // ---------------------------------------------------------------------
    // IDropSource: decides when the drag ends (mouse up / Esc) and receives
    // the effect the current target is willing to accept, which drives the
    // "droppable" highlight on the thumbnail.
    // ---------------------------------------------------------------------
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class ImageDropSource : IDropSource
    {
        readonly DragPreviewWindow preview;

        public ImageDropSource(DragPreviewWindow preview)
        {
            this.preview = preview;
        }

        public int QueryContinueDrag(int escapePressed, int keyState)
        {
            if (escapePressed != 0) return DragNative.DRAGDROP_S_CANCEL;
            if ((keyState & DragNative.MK_LBUTTON) == 0 && (keyState & DragNative.MK_RBUTTON) == 0)
            {
                return DragNative.DRAGDROP_S_DROP;
            }
            return DragNative.S_OK;
        }

        public int GiveFeedback(int effect)
        {
            if (preview != null) preview.SetDropAccepted(effect != 0);
            return DragNative.DRAGDROP_S_USEDEFAULTCURSORS;
        }
    }

    // ---------------------------------------------------------------------
    // Thumbnail that follows the pointer while the OS drag loop runs.
    //
    // It is a layered, never-activated, transparent-to-hit-testing window so
    // it can never become the drop target itself.
    // ---------------------------------------------------------------------
    internal sealed class DragPreviewWindow : Window
    {
        const int PreviewMaxWidth = 176;
        const int PreviewMaxHeight = 132;

        readonly Border frame;
        readonly Border badge;
        readonly Image picture;
        IntPtr handle;
        double dpi = 1.0;
        int pixelWidth;
        int pixelHeight;
        int screenLeft;
        int screenTop;
        int screenRight;
        int screenBottom;
        bool ready;
        bool accepted;

        public DragPreviewWindow(BitmapSource image)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            double sourceWidth = image == null ? 140 : image.PixelWidth;
            double sourceHeight = image == null ? 100 : image.PixelHeight;
            double scale = Math.Min(PreviewMaxWidth / Math.Max(1, sourceWidth), PreviewMaxHeight / Math.Max(1, sourceHeight));
            if (scale > 1.0) scale = 1.0;
            double width = Math.Max(64, Math.Round(sourceWidth * scale));
            double height = Math.Max(48, Math.Round(sourceHeight * scale));

            Width = width + 12;
            Height = height + 12;

            picture = new Image
            {
                Source = image,
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };

            TextBlock hint = new TextBlock
            {
                Text = "松开放置",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = Brushes.White
            };
            badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 18, 24, 31)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 2, 7, 2),
                Child = hint,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };

            Grid body = new Grid();
            body.Children.Add(picture);
            body.Children.Add(badge);

            frame = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(242, 28, 34, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Child = body,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 }
            };

            Content = frame;
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                int style = DragNative.GetWindowLong(handle, DragNative.GWL_EXSTYLE);
                DragNative.SetWindowLong(handle, DragNative.GWL_EXSTYLE,
                    style | DragNative.WS_EX_TRANSPARENT | DragNative.WS_EX_NOACTIVATE | DragNative.WS_EX_TOOLWINDOW);
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null) dpi = source.CompositionTarget.TransformToDevice.M11;

                // 提前把像素尺寸/虚拟屏幕边界算好：MoveTo 会在跟随线程里被高频调用，
                // 不能再读 WPF 依赖属性（跨线程访问会抛异常），也避免每帧重复查询系统度量。
                pixelWidth = (int)Math.Round(Width * dpi);
                pixelHeight = (int)Math.Round(Height * dpi);
                screenLeft = DragNative.GetSystemMetrics(76);
                screenTop = DragNative.GetSystemMetrics(77);
                screenRight = screenLeft + DragNative.GetSystemMetrics(78);
                screenBottom = screenTop + DragNative.GetSystemMetrics(79);
                ready = handle != IntPtr.Zero;
            };
        }

        public void SetDropAccepted(bool value)
        {
            if (accepted == value) return;
            accepted = value;
            if (value)
            {
                frame.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 220, 140));
                frame.BorderThickness = new Thickness(2);
                badge.Visibility = Visibility.Visible;
            }
            else
            {
                frame.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
                frame.BorderThickness = new Thickness(1);
                badge.Visibility = Visibility.Collapsed;
            }
        }

        public void MoveTo(int screenX, int screenY)
        {
            if (!ready) return;
            int left = Math.Min(screenX + 18, screenRight - pixelWidth - 4);
            int top = Math.Min(screenY + 18, screenBottom - pixelHeight - 4);
            left = Math.Max(screenLeft, left);
            top = Math.Max(screenTop, top);
            // 直接跨线程异步移动 HWND：不经过 WPF Dispatcher，拖拽时才能紧贴光标。
            DragNative.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                DragNative.SWP_NOSIZE | DragNative.SWP_NOZORDER | DragNative.SWP_NOACTIVATE | DragNative.SWP_ASYNCWINDOWPOS);
        }
    }

    internal sealed class DragOutcome
    {
        public int Effect;
        public bool Failed;
        public string Error;

        public bool Succeeded { get { return !Failed && Effect != 0; } }
    }

    // ---------------------------------------------------------------------
    // Entry point used by the island.
    // ---------------------------------------------------------------------
    internal static class NativeImageDrag
    {
        static bool dragActive;

        public static DragOutcome Run(string path, BitmapSource full, BitmapSource previewSource)
        {
            DragOutcome outcome = new DragOutcome();
            if (dragActive)
            {
                outcome.Failed = true;
                outcome.Error = "drag already active";
                return outcome;
            }

            DragPayload payload;
            try
            {
                payload = DragPayload.Create(path, full);
            }
            catch (Exception exception)
            {
                DragNative.Log("payload build failed: " + exception);
                outcome.Failed = true;
                outcome.Error = exception.Message;
                return outcome;
            }

            DragPreviewWindow preview = null;
            Thread follower = null;
            dragActive = true;
            try
            {
                // The thumbnail is a nicety: if it cannot be created the drag
                // must still go through.
                try
                {
                    preview = new DragPreviewWindow(previewSource);
                    preview.Show();

                    DragNative.POINT cursor;
                    if (DragNative.GetCursorPos(out cursor)) preview.MoveTo(cursor.X, cursor.Y);

                    DragPreviewWindow window = preview;
                    follower = new Thread(delegate()
                    {
                        DragNative.POINT point;
                        DragNative.POINT last = new DragNative.POINT();
                        last.X = int.MinValue;
                        last.Y = int.MinValue;
                        DragNative.TimeBeginPeriod(1);
                        try
                        {
                            while (dragActive)
                            {
                                if (DragNative.GetCursorPos(out point))
                                {
                                    if (point.X != last.X || point.Y != last.Y)
                                    {
                                        last = point;
                                        try { window.MoveTo(point.X, point.Y); }
                                        catch (Exception) { }
                                    }
                                }
                                // 4ms 轮询 + 1ms 系统计时器精度，视觉上紧贴光标。
                                Thread.Sleep(4);
                            }
                        }
                        finally
                        {
                            DragNative.TimeEndPeriod(1);
                        }
                    });
                    follower.IsBackground = true;
                    follower.Name = "IslandShot.DragPreview";
                    follower.Start();
                }
                catch (Exception previewException)
                {
                    DragNative.Log("preview unavailable: " + previewException);
                    preview = null;
                }

                DragDataObject data = new DragDataObject(payload);
                ImageDropSource source = new ImageDropSource(preview);

                DragNative.Log("drag start | " + path + " | formats=" + payload.BuildFormats().Count);
                int effect;
                DragNative.DoDragDrop(data, source, DragNative.DROPEFFECT_COPY, out effect);
                outcome.Effect = effect;
                DragNative.Log("drag end | effect=" + effect);
                GC.KeepAlive(data);
                GC.KeepAlive(source);
            }
            catch (Exception exception)
            {
                outcome.Failed = true;
                outcome.Error = exception.Message;
                DragNative.Log("drag failed: " + exception);
            }
            finally
            {
                dragActive = false;
                if (follower != null) { try { follower.Join(120); } catch (Exception) { } }
                if (preview != null)
                {
                    try { preview.SetDropAccepted(false); preview.Close(); }
                    catch (Exception) { }
                }
            }
            return outcome;
        }
    }
}
