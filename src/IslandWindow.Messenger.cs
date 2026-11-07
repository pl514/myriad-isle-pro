using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 微信 / QQ 小窗：不嵌内容，只"摆"别人的窗口。
    //
    // 实测（2026-11-07，微信 4.2 / QQ）：
    //   · 两个都接受 SetWindowPos 缩放到 420×760；
    //   · 微信最小化到托盘后，主窗口会消失，只剩一个 176×199 的 Weixin 浮窗，
    //     这时必须先"激活应用"（再跑一次 exe），等主窗口回来再吸；
    //   · QQ 是 Electron，被外部移动后容易不重绘 → 移动后强制 RedrawWindow。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        // ---------------- Win32 ----------------
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRgn, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct Win32Rect { public int Left, Top, Right, Bottom; }

        const uint SwpNoZorder = 0x0004;
        const uint SwpShowWindow = 0x0040;
        const int SwShow = 5;
        const int SwRestore = 9;

        const uint RdwInvalidate = 0x0001;
        const uint RdwFrame = 0x0400;
        const uint RdwAllChildren = 0x0080;
        const uint RdwUpdateNow = 0x0100;

        // 小于这个尺寸的窗口不算"主窗口"（微信托盘态剩的那个浮窗是 176×199）
        const int MinMainWidth = 400;
        const int MinMainHeight = 300;

        sealed class MessengerApp
        {
            public string Id;
            public string Display;
            public string[] ProcessNames;
            public string ClassHint;
            public string InstallKey;       // 注册表里 InstallPath 的位置
            public string ExeName;
        }

        static readonly MessengerApp WeChatApp = new MessengerApp
        {
            Id = "wechat",
            Display = "微信",
            ProcessNames = new string[] { "Weixin", "WeChat" },
            ClassHint = "QWindowIcon",
            InstallKey = @"Software\Tencent\Weixin",
            ExeName = "Weixin.exe"
        };

        static readonly MessengerApp QqApp = new MessengerApp
        {
            Id = "qq",
            Display = "QQ",
            ProcessNames = new string[] { "QQ", "QQNT" },
            ClassHint = "Chrome_WidgetWin_1",
            InstallKey = @"Software\Tencent\QQNT",
            ExeName = "QQ.exe"
        };

        IntPtr messengerHwnd;
        string messengerActiveId;
        Win32Rect messengerOriginalRect;
        bool messengerOriginalValid;

        static MessengerApp MessengerById(string id)
        {
            return id == "qq" ? QqApp : WeChatApp;
        }

        static string MessengerLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "messenger.log"); }
        }

        static void MessengerLog(string message)
        {
            try
            {
                string folder = Path.GetDirectoryName(MessengerLogPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(MessengerLogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        // 点球：已吸附就还原，否则吸过来
        void ToggleMessengerWindow(string id)
        {
            MessengerApp app = MessengerById(id);

            if (messengerHwnd != IntPtr.Zero && messengerActiveId == id)
            {
                RestoreMessengerWindow();
                return;
            }
            if (messengerHwnd != IntPtr.Zero) RestoreMessengerWindow();

            IntPtr hwnd = FindMessengerWindow(app);
            MessengerLog(app.Id + " find -> hwnd=" + hwnd + " size=" + DescribeSize(hwnd));

            if (hwnd == IntPtr.Zero || !IsMainSized(hwnd))
            {
                // 最小化到托盘时主窗口不在了：先把应用激活，再等它自己冒出来
                MessengerLog(app.Id + " -> activate app");
                ActivateApp(app);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
                    retry.Tick += delegate
                    {
                        retry.Stop();
                        IntPtr again = FindMessengerWindow(app);
                        MessengerLog(app.Id + " retry -> hwnd=" + again + " size=" + DescribeSize(again));
                        if (again == IntPtr.Zero || !IsMainSized(again))
                        {
                            ShowToolMessage(app.Display,
                                app.Display + " 现在在托盘里（窗口被关掉了）。先点一下任务栏托盘图标把它打开，再点球。");
                            SetToolRunning(FindTool(app.Id), false);
                            return;
                        }
                        SnapWindow(app, again);
                    };
                    retry.Start();
                }), DispatcherPriority.Background);
                return;
            }

            SnapWindow(app, hwnd);
        }

        void SnapWindow(MessengerApp app, IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SwShow);
                ShowWindow(hwnd, SwRestore);
            }

            Win32Rect before;
            messengerOriginalValid = GetWindowRect(hwnd, out before);
            messengerOriginalRect = before;

            Rect target = MessengerTargetRect();
            double dpi = DpiScale();
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(target.Left * dpi),
                (int)Math.Round(target.Top * dpi),
                (int)Math.Round(target.Width * dpi),
                (int)Math.Round(target.Height * dpi),
                SwpNoZorder | SwpShowWindow);

            // Electron / Qt 被外部移动后可能不重绘，这里强制刷新一次
            SetForegroundWindow(hwnd);
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwFrame | RdwAllChildren | RdwUpdateNow);

            messengerHwnd = hwnd;
            messengerActiveId = app.Id;
            SetToolRunning(FindTool(app.Id), true);
            MessengerLog(app.Id + " snapped -> " + target.Left + "," + target.Top + " " + target.Width + "x" + target.Height);
        }

        void RestoreMessengerWindow()
        {
            if (messengerHwnd == IntPtr.Zero) return;
            if (messengerOriginalValid)
            {
                SetWindowPos(messengerHwnd, IntPtr.Zero,
                    messengerOriginalRect.Left,
                    messengerOriginalRect.Top,
                    messengerOriginalRect.Right - messengerOriginalRect.Left,
                    messengerOriginalRect.Bottom - messengerOriginalRect.Top,
                    SwpNoZorder);
                RedrawWindow(messengerHwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwFrame | RdwAllChildren | RdwUpdateNow);
                MessengerLog(messengerActiveId + " restored");
            }
            if (messengerActiveId != null) SetToolRunning(FindTool(messengerActiveId), false);
            messengerHwnd = IntPtr.Zero;
            messengerActiveId = null;
            messengerOriginalValid = false;
        }

        // 吸附目标：胶囊正下方，手机比例（默认 330×700，可在 data\messenger.txt 里改）
        Rect MessengerTargetRect()
        {
            Rect capsuleRect = ScreenRectOf(capsule);
            Rect work = SystemParameters.WorkArea;

            double width = 330;
            double height = 700;
            ReadMessengerSizeOverride(ref width, ref height);

            // 高度不能超出工作区
            double maxHeight = work.Height - 80;
            if (height > maxHeight) height = maxHeight;
            if (width < 240) width = 240;
            if (width > work.Width * 0.4) width = work.Width * 0.4;

            double left = capsuleRect.IsEmpty
                ? work.Left + (work.Width - width) / 2
                : capsuleRect.Left + (capsuleRect.Width - width) / 2;
            left = Math.Max(work.Left + 8, Math.Min(left, work.Right - width - 8));

            double top = capsuleRect.IsEmpty ? work.Top + 60 : capsuleRect.Bottom + 10;
            top = Math.Min(top, work.Bottom - height - 8);

            return new Rect(left, top, width, height);
        }

        // data\messenger.txt 里写 "330x700" 就能自定义小窗尺寸
        static void ReadMessengerSizeOverride(ref double width, ref double height)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "messenger.txt");
                if (!File.Exists(path)) return;
                string text = File.ReadAllText(path).Trim().ToLowerInvariant();
                int x = text.IndexOf('x');
                if (x <= 0) return;
                double w, h;
                if (!double.TryParse(text.Substring(0, x), out w)) return;
                if (!double.TryParse(text.Substring(x + 1), out h)) return;
                if (w >= 200 && h >= 300) { width = w; height = h; }
            }
            catch (Exception) { }
        }

        string DescribeSize(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(none)";
            Win32Rect rect;
            if (!GetWindowRect(hwnd, out rect)) return "(?)";
            return (rect.Right - rect.Left) + "x" + (rect.Bottom - rect.Top);
        }

        bool IsMainSized(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            Win32Rect rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            return (rect.Right - rect.Left) >= MinMainWidth && (rect.Bottom - rect.Top) >= MinMainHeight;
        }

        // 找窗口：同名进程 + 类名匹配，可见优先、面积最大
        IntPtr FindMessengerWindow(MessengerApp app)
        {
            var pids = new HashSet<int>();
            foreach (string name in app.ProcessNames)
            {
                try
                {
                    foreach (Process process in Process.GetProcessesByName(name)) pids.Add(process.Id);
                }
                catch (Exception) { }
            }
            if (pids.Count == 0) return IntPtr.Zero;

            IntPtr bestVisible = IntPtr.Zero;
            long bestVisibleArea = 0;
            IntPtr bestHidden = IntPtr.Zero;
            long bestHiddenArea = 0;

            EnumWindows(delegate(IntPtr hwnd, IntPtr param)
            {
                int pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (!pids.Contains(pid)) return true;

                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, 256);
                if (app.ClassHint != null &&
                    cls.ToString().IndexOf(app.ClassHint, StringComparison.OrdinalIgnoreCase) < 0) return true;

                Win32Rect rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area < 100 * 100) return true;

                if (IsWindowVisible(hwnd))
                {
                    if (area > bestVisibleArea) { bestVisibleArea = area; bestVisible = hwnd; }
                }
                else
                {
                    if (area > bestHiddenArea) { bestHiddenArea = area; bestHidden = hwnd; }
                }
                return true;
            }, IntPtr.Zero);

            return bestVisible != IntPtr.Zero ? bestVisible : bestHidden;
        }

        // 再跑一次 exe，让单实例的客户端把主窗口叫出来
        void ActivateApp(MessengerApp app)
        {
            string exe = FindAppExecutable(app);
            if (string.IsNullOrEmpty(exe))
            {
                MessengerLog(app.Id + " exe not found");
                return;
            }
            try
            {
                var info = new ProcessStartInfo(exe) { UseShellExecute = true };
                Process.Start(info);
                MessengerLog(app.Id + " launched " + exe);
            }
            catch (Exception ex)
            {
                MessengerLog(app.Id + " launch failed: " + ex.Message);
            }
        }

        string FindAppExecutable(MessengerApp app)
        {
            // 1) 正在运行的进程自带路径最可靠
            foreach (string name in app.ProcessNames)
            {
                try
                {
                    Process[] list = Process.GetProcessesByName(name);
                    if (list.Length == 0) continue;
                    try
                    {
                        string path = list[0].MainModule.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                    catch (Exception) { }
                }
                catch (Exception) { }
            }

            // 2) 注册表里的 InstallPath
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(app.InstallKey))
                {
                    if (key != null)
                    {
                        string install = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(install))
                        {
                            string candidate = Path.Combine(install, app.ExeName);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            catch (Exception) { }

            return null;
        }

        // 给"有新消息"提示用：客户端是否在运行
        bool IsMessengerRunning(string id)
        {
            MessengerApp app = MessengerById(id);
            foreach (string name in app.ProcessNames)
            {
                try { if (Process.GetProcessesByName(name).Length > 0) return true; }
                catch (Exception) { }
            }
            return false;
        }

        // 闪烁窗口的进程号 → 是哪个客户端
        static string MessengerIdByPid(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    string name = process.ProcessName;
                    foreach (string candidate in WeChatApp.ProcessNames)
                    {
                        if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return "wechat";
                    }
                    foreach (string candidate in QqApp.ProcessNames)
                    {
                        if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return "qq";
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        // 点提示：把客户端窗口叫到前台；找不到就启动它
        void OpenMessengerApp(string id)
        {
            MessengerApp app = MessengerById(id);
            IntPtr hwnd = FindMessengerWindow(app);
            if (hwnd != IntPtr.Zero && IsMainSized(hwnd))
            {
                if (!IsWindowVisible(hwnd))
                {
                    ShowWindow(hwnd, SwShow);
                    ShowWindow(hwnd, SwRestore);
                }
                SetForegroundWindow(hwnd);
                MessengerLog("open " + id + " -> foreground hwnd=" + hwnd);
                return;
            }
            MessengerLog("open " + id + " -> no window, activate app");
            ActivateApp(app);
        }
    }
}
