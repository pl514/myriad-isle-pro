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

        static string OrigRectFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hongguo-orig.txt"); }
        }

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
                RestoreOriginalGeometry();   // 隐藏时恢复成它自己的尺寸：之后直接双击打开就是原样
                overlayActive = false;
                if (hwnd != IntPtr.Zero && IsWindow(hwnd)) ShowWindow(hwnd, SwHide);
                KeeperStop();
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
                            PlaceWithoutResize(LoadRect());   // 只挪位置，别改尺寸
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
                        && width > 300 && height > 200 && width <= 780)
                    {
                        return new Rect(left, top, width, height);
                    }
                }
            }
            catch (Exception) { }

            Rect work = SystemParameters.WorkArea;
            double w = Math.Min(1040, Math.Max(880, work.Width * 0.50));
            double h = Math.Round(w * 9.0 / 16.0);
            double x = work.Right - w - 60;
            double y = work.Top + 80;
            return new Rect(x, y, w, h);
        }

        // 记住用户拖动后的位置（隐藏/退出时保存）
        // 持续保持置顶：客户端会把自己的 z-order 改回去，所以每隔一会儿补一次
        static System.Threading.Timer keeper;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        internal static void KeeperStart()
        {
            if (keeper != null) return;
            keeper = new System.Threading.Timer(delegate
            {
                try
                {
                    // 客户端可能重建窗口：每次都从进程重新取主窗口句柄
                    IntPtr target = hwnd;
                    try
                    {
                        if (process != null && !process.HasExited)
                        {
                            process.Refresh();
                            if (process.MainWindowHandle != IntPtr.Zero) { target = process.MainWindowHandle; hwnd = target; }
                        }
                    }
                    catch (Exception) { }
                    if (target == IntPtr.Zero || !IsWindow(target)) return;
                    if (!IsWindowVisible(target) || IsIconic(target)) return;

                    // 先把 WS_EX_TOPMOST 位补上
                    int ex = GetWindowLong(target, -20);
                    if ((ex & 0x00000008) == 0)
                    {
                        SetWindowLong(target, -20, ex | 0x00000008);
                    }
                    // 再重新声明一次置顶（不移动、不改大小、不抢焦点）
                    SetWindowPos(target, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                }
                catch (Exception) { }
            }, null, 800, 800);
        }

        internal static void KeeperStop()
        {
            try { if (keeper != null) { keeper.Dispose(); keeper = null; } } catch (Exception) { }
        }

        internal static void SaveRect()
        {
            try
            {
                RECT current;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out current)) return;
                if ((current.Right - current.Left) > 780) return;   // 大窗（原本尺寸）不记
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
        // 画质优先：窗口太小时先放大到 1100x760（播放器按大窗口选高清流），
        // 已经够大就只挪位置、绝不改尺寸，避免播放器因为尺寸抖动而换成低清流。
        internal static void PlaceWithoutResize(Rect rect)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
                RECT current;
                bool small = GetWindowRect(hwnd, out current) && (current.Right - current.Left) <= 780;
                if (small)
                {
                    // 放大后可能超出屏幕：把位置夹进工作区
                    Rect work = SystemParameters.WorkArea;
                    int w = 1100, h = 760;
                    int x = (int)rect.X;
                    int y = (int)rect.Y;
                    x = Math.Max((int)work.Left + 10, Math.Min((int)(work.Right - w - 30), x));
                    y = Math.Max((int)work.Top + 10, Math.Min((int)(work.Bottom - h - 60), y));
                    SetWindowPos(hwnd, HwndTopmost, x, y, w, h, 0x0040);
                }
                else
                {
                    // 只挪位置时也要夹一下，别把大窗口推出屏幕外
                    Rect work2 = SystemParameters.WorkArea;
                    int cw = current.Right - current.Left;
                    int chh = current.Bottom - current.Top;
                    int mx = Math.Max((int)work2.Left + 10, Math.Min((int)(work2.Right - cw - 30), (int)rect.X));
                    int my = Math.Max((int)work2.Top + 10, Math.Min((int)(work2.Bottom - chh - 60), (int)rect.Y));
                    SetWindowPos(hwnd, HwndTopmost, mx, my, 0, 0, 0x0001 | 0x0040);   // NOSIZE | SHOWWINDOW
                }
            }
            catch (Exception) { }
        }

        internal static void SetRect(Rect rect)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | SwpShowWindow);
            }
            catch (Exception) { }
        }

        // 启动 + 定位 + 去标题栏，但不显示（配合形变动画）
        // 第一次压小之前记下它原本的窗口矩形：直接双击打开时还原成原样
        static void SaveOriginalOnce()
        {
            try
            {
                if (File.Exists(OrigRectFile)) return;
                RECT current;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out current)) return;
                if ((current.Right - current.Left) > 780) return;   // 大窗（原本尺寸）不记
                int w = current.Right - current.Left;
                int h = current.Bottom - current.Top;
                if (w < 300 || h < 200) return;
                File.WriteAllText(OrigRectFile, current.Left + "," + current.Top + "," + w + "," + h);
            }
            catch (Exception) { }
        }

        // 还原成它自己的窗口大小（在隐藏状态下调用，让它退出时保存的也是原尺寸）
        internal static void RestoreOriginalGeometry()
        {
            try
            {
                if (!File.Exists(OrigRectFile)) return;
                string[] parts = File.ReadAllText(OrigRectFile).Trim().Split(',');
                double left, top, width, height;
                if (parts.Length < 4) return;
                if (!double.TryParse(parts[0], out left) || !double.TryParse(parts[1], out top)) return;
                if (!double.TryParse(parts[2], out width) || !double.TryParse(parts[3], out height)) return;
                if (width < 300 || height < 200) return;
                IntPtr target = hwnd;
                if (target == IntPtr.Zero || !IsWindow(target)) return;
                SetWindowPos(target, IntPtr.Zero, (int)left, (int)top, (int)width, (int)height, 0x0001 | 0x0004 | 0x0010);
            }
            catch (Exception) { }
        }

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
                SaveOriginalOnce();   // 压小之前先记下它原本的窗口大小
                PlaceWithoutResize(rect);   // 只挪位置，不改尺寸（保画质）
                StripCaption();
                ShowWindow(hwnd, SwHide);
            }
            catch (Exception) { }
        }

        // 形变动画期间把客户端窗口按住不显示（它初始化完会自己 Show 出来）
        internal static void HoldHidden(int milliseconds)
        {
            try
            {
                IntPtr target = hwnd;
                Task.Run(delegate
                {
                    DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
                    while (DateTime.UtcNow < end)
                    {
                        try
                        {
                            if (target != IntPtr.Zero && IsWindow(target)) ShowWindow(target, SwHide);
                        }
                        catch (Exception) { }
                        System.Threading.Thread.Sleep(25);
                    }
                });
            }
            catch (Exception) { }
        }

        internal static void ShowAt(Rect rect)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                PlaceWithoutResize(rect);   // 只挪位置，不改尺寸（保画质）
                StripCaption();
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | SwpShowWindow);
                ShowWindow(hwnd, SwShowNoActivate);
                SetForegroundWindow(hwnd);
                KeeperStart();   // 持续保持置顶（客户端会改回自己的 z-order）

                // 客户端起来后会恢复自己的位置/尺寸：前 2.2 秒内再压几次
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
                            PlaceWithoutResize(LoadRect());   // 只挪位置，别改尺寸
                            StripCaption(target);
                            SetWindowPos(target, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                        }
                        catch (Exception) { }
                    }
                });
            }
            catch (Exception) { }
        }

        const int SwShowNoActivate = 4;

        // 直接双击打开的还原：客户端会把"我们压小后的尺寸"记成自己的配置，
        // 所以当它不是由小窗打开时，把它掰回一个正常大小（记过的原尺寸，或默认的 1000x700）。
        static bool overlayActive;
        static int overlayPid;
        static System.Threading.Timer directWatcher;

        internal static void StartDirectLaunchWatcher()
        {
            if (directWatcher != null) return;
            directWatcher = new System.Threading.Timer(delegate
            {
                try
                {
                    if (overlayActive) return;
                    if (process == null || process.HasExited)
                    {
                        // 用户直接双击打开的：我们这边没有进程对象，按名字找一下
                        Process found = null;
                        foreach (Process item in Process.GetProcessesByName("红果短剧")) { found = item; break; }
                        if (found == null) return;
                        process = found;
                    }
                    if (process == null || process.HasExited) return;
                    process.Refresh();
                    IntPtr target = process.MainWindowHandle;
                    if (target == IntPtr.Zero || !IsWindow(target)) return;
                    if (!IsWindowVisible(target)) return;

                    RECT current;
                    if (!GetWindowRect(target, out current)) return;
                    WatchLog("see w=" + (current.Right - current.Left) + " overlay=" + overlayActive);
                    int w = current.Right - current.Left;
                    int h = current.Bottom - current.Top;
                    if (w > 760) return;   // 已经是大窗，不用管

                    Rect orig = LoadOriginalOrFallback();
                    SetWindowPos(target, IntPtr.Zero, (int)orig.X, (int)orig.Y, (int)orig.Width, (int)orig.Height, 0x0004 | 0x0010);   // NOZORDER | NOACTIVATE（0x0001 是 NOSIZE，千万别带）
                }
                catch (Exception) { }
            }, null, 2500, 2500);
        }

        static void WatchLog(string message)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "hongguo-watch.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        static Rect LoadOriginalOrFallback()
        {
            try
            {
                if (File.Exists(OrigRectFile))
                {
                    string[] parts = File.ReadAllText(OrigRectFile).Trim().Split(',');
                    double l, tp, w, h;
                    if (parts.Length >= 4 && double.TryParse(parts[0], out l) && double.TryParse(parts[1], out tp)
                        && double.TryParse(parts[2], out w) && double.TryParse(parts[3], out h) && w > 760 && h > 400)
                    {
                        return new Rect(l, tp, w, h);
                    }
                }
            }
            catch (Exception) { }
            Rect work = SystemParameters.WorkArea;
            double width = Math.Min(1100, work.Width * 0.62);
            double height = Math.Min(760, work.Height * 0.72);
            return new Rect(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
        }

        internal static void SetOverlayActive(bool active)
        {
            overlayActive = active;
        }

        internal static void Reset()
        {
            try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch (Exception) { }
        }
    }
}
