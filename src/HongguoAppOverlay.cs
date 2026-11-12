using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace IslandShot
{
    // 把外面那个「红果短剧.exe」当成我们的小窗用：
    //   · 点小球 → 启动（或唤回）它，强制置顶 + 摆成横屏小窗；
    //   · 再点一次 → 隐藏（不退出进程，下次秒开）；
    //   · 位置记在 data\hongguo-app.txt，下次原地出现。
    // 这样做是因为官网网页只放前几集，全集只在这个客户端里。
    internal static class HongguoAppOverlay
    {
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        static readonly IntPtr HwndTopmost = new IntPtr(-1);
        const uint SwpShowWindow = 0x0040;
        const int SwHide = 0;
        const int SwRestore = 9;

        static Process process;
        static IntPtr hwnd;

        static string StateFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hongguo-app.txt"); }
        }

        internal static string ExePath
        {
            get
            {
                try
                {
                    string config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hongguo-player.txt");
                    if (File.Exists(config))
                    {
                        string custom = File.ReadAllText(config).Trim().Trim('"');
                        if (custom.Length > 0 && File.Exists(custom)) return custom;
                    }
                }
                catch (Exception) { }

                string[] guesses =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "红果短剧.exe"),
                    @"C:\桌面\红果短剧\红果短剧.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "红果短剧", "红果短剧.exe")
                };
                foreach (string guess in guesses) { if (File.Exists(guess)) return guess; }
                return "";
            }
        }

        internal static bool Available
        {
            get { return ExePath.Length > 0; }
        }

        internal static bool IsShown
        {
            get
            {
                try
                {
                    return hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd) && !IsIconic(hwnd);
                }
                catch (Exception) { return false; }
            }
        }

        // 点小球：显示 / 隐藏切换
        internal static void Toggle()
        {
            if (IsShown) { Hide(); return; }
            Show();
        }

        internal static void Hide()
        {
            try
            {
                SaveRect();
                if (hwnd != IntPtr.Zero && IsWindow(hwnd)) ShowWindow(hwnd, SwHide);
            }
            catch (Exception) { }
        }

        internal static void Show()
        {
            try
            {
                if (process == null || process.HasExited) StartProcess();
                if (process == null) return;

                // 进程刚起来时窗口还没创建，轮询等一会儿
                for (int i = 0; i < 100; i++)
                {
                    if (TryGetWindow(out hwnd)) break;
                    System.Threading.Thread.Sleep(150);
                    process.Refresh();
                }
                if (hwnd == IntPtr.Zero) return;

                if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                ApplyRect();
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | SwpShowWindow); // NOSIZE|NOMOVE|SHOW
                SetForegroundWindow(hwnd);

                // 客户端启动后会自己恢复上次的尺寸/位置：过一会儿再压一次
                IntPtr target = hwnd;
                Task.Run(delegate
                {
                    int[] delays = { 700, 1400, 2200 };
                    foreach (int delay in delays)
                    {
                        System.Threading.Thread.Sleep(delay);
                        try
                        {
                            if (target == IntPtr.Zero || !IsWindow(target)) return;
                            ApplyRectTarget(target);
                            SetWindowPos(target, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | SwpShowWindow);
                            StripCaption(target);
                        }
                        catch (Exception) { }
                    }
                });
            }
            catch (Exception) { }
        }

        static void StartProcess()
        {
            string exe = ExePath;
            if (exe.Length == 0) return;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = true
                });
            }
            catch (Exception) { process = null; }
        }

        static bool TryGetWindow(out IntPtr result)
        {
            result = IntPtr.Zero;
            try
            {
                if (process == null || process.HasExited) return false;
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    result = process.MainWindowHandle;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // 摆成横屏小窗：位置/尺寸优先用上次记住的，否则给个 16:9 的默认位置
        static void ApplyRect()
        {
            ApplyRectTarget(hwnd);
        }

        static void ApplyRectTarget(IntPtr target)
        {
            if (target == IntPtr.Zero) return;
            Rect rect = LoadRect();
            SetWindowPos(target, HwndTopmost, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, SwpShowWindow);
        }

        static Rect LoadRect()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    string[] parts = File.ReadAllText(StateFile).Trim().Split(',');
                    double left, top, width, height;
                    if (parts.Length >= 4
                        && double.TryParse(parts[0], out left) && double.TryParse(parts[1], out top)
                        && double.TryParse(parts[2], out width) && double.TryParse(parts[3], out height)
                        && width > 300 && height > 200)
                    {
                        return new Rect(left, top, width, height);
                    }
                }
            }
            catch (Exception) { }

            Rect work = SystemParameters.WorkArea;
            double w = Math.Max(640, Math.Min(1100, work.Height * 0.62));
            double h = Math.Round(w * 9.0 / 16.0);
            double x = work.Right - w - 60;
            double y = work.Top + 80;
            return new Rect(x, y, w, h);
        }

        // 记住用户拖动后的位置（隐藏/退出时保存）
        internal static void SaveRect()
        {
            try
            {
                RECT current;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out current)) return;
                string folder = Path.GetDirectoryName(StateFile);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(StateFile,
                    current.Left + "," + current.Top + "," + (current.Right - current.Left) + "," + (current.Bottom - current.Top));
            }
            catch (Exception) { }
        }

        internal static IntPtr Handle
        {
            get { return (hwnd != IntPtr.Zero && IsWindow(hwnd)) ? hwnd : IntPtr.Zero; }
        }

        // 目标矩形（保存过就用保存的，否则 16:9 靠右上）
        internal static Rect PlannedRect()
        {
            return LoadRect();
        }

        internal static Rect CurrentRect()
        {
            try
            {
                RECT r;
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out r))
                    return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch (Exception) { }
            return PlannedRect();
        }

        // 去掉系统标题栏（保留它自己的播放进度条等页面内容）
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        const int GwlStyle = -16;
        const int WsCaption = 0x00C00000;
        const int WsThickFrame = 0x00040000;
        const uint SwpFrameChanged = 0x0020;

        internal static void StripCaption(IntPtr target)
        {
            try
            {
                if (target == IntPtr.Zero || !IsWindow(target)) return;
                int style = GetWindowLong(target, GwlStyle);
                if ((style & WsCaption) == 0) return;
                SetWindowLong(target, GwlStyle, style & ~WsCaption & ~WsThickFrame);
                SetWindowPos(target, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | SwpFrameChanged);
            }
            catch (Exception) { }
        }

        internal static void StripCaption()
        {
            StripCaption(hwnd);
        }

        // 让外部窗口对准指定矩形（拖动时也用它）
        internal static void SetRect(Rect rect)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
                SetWindowPos(hwnd, HwndTopmost, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, SwpShowWindow);
            }
            catch (Exception) { }
        }

        // 启动 + 定位 + 去标题栏，但不显示（配合形变动画）
        internal static void Prepare(Rect rect)
        {
            try
            {
                if (process == null || process.HasExited) StartProcess();
                if (process == null) return;
                for (int i = 0; i < 100; i++)
                {
                    if (TryGetWindow(out hwnd)) break;
                    System.Threading.Thread.Sleep(120);
                    process.Refresh();
                }
                if (hwnd == IntPtr.Zero) return;
                System.Threading.Thread.Sleep(1200);   // 等客户端初始化完再改样式，太早它会重建窗口
                SetRect(rect);
                StripCaption();
                ShowWindow(hwnd, SwHide);
            }
            catch (Exception) { }
        }

        internal static void ShowAt(Rect rect)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                System.Threading.Thread.Sleep(1200);   // 等客户端初始化完再改样式，太早它会重建窗口
                SetRect(rect);
                StripCaption();
                SetWindowPos(hwnd, HwndTopmost, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, SwpShowWindow);
                ShowWindow(hwnd, SwShowNoActivate);
                SetForegroundWindow(hwnd);
            }
            catch (Exception) { }
        }

        const int SwShowNoActivate = 4;

        internal static void Reset()
        {
            try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch (Exception) { }
        }
    }
}
