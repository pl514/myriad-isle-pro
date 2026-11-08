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
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRgn, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct Win32Rect { public int Left, Top, Right, Bottom; }

        const uint SwpNoZorder = 0x0004;
        const uint SwpShowWindow = 0x0040;
        const int SwShow = 5;
        const int SwShowNormal = 1;
        const int SwRestore = 9;

        // 把窗口真正激活：单靠 SetForegroundWindow 从后台进程调用会被系统拒绝，
        // 表现就是"窗口出来了但像卡死"（画面不刷新、点击没反应）。
        void ForceForeground(IntPtr hwnd)
        {
            ShowWindow(hwnd, SwShowNormal);

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = 0;
            if (foreground != IntPtr.Zero)
            {
                int ignored;
                foregroundThread = (uint)GetWindowThreadProcessId(foreground, out ignored);
            }
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);

                SetForegroundWindow(hwnd);
                if (GetForegroundWindow() != hwnd) SwitchToThisWindow(hwnd, true);
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, foregroundThread, false);
            }
            MessengerLog("activate " + hwnd + " foreground=" + (GetForegroundWindow() == hwnd));
        }

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

        // 学习到的"真窗口"：QQ 同时存在多个同标题窗口，只有成为前台的那个才是真界面
        static readonly Dictionary<string, IntPtr> messengerKnownHwnd = new Dictionary<string, IntPtr>();
        DispatcherTimer messengerLearnTimer;

        void StartMessengerLearning()
        {
            if (messengerLearnTimer != null) return;
            messengerLearnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            messengerLearnTimer.Tick += delegate { LearnMessengerWindow(); };
            messengerLearnTimer.Start();
        }

        void LearnMessengerWindow()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return;
                int pid;
                GetWindowThreadProcessId(foreground, out pid);
                string app = MessengerIdByPid(pid);
                if (app == null) return;

                // 用户把微信/QQ 切到前台了：认为已经看过消息，消息小胶囊可以收了
                if (capsuleNoticeActive && capsuleNoticeApp == app) HideCapsuleNotice();

                Win32Rect rect;
                if (!GetWindowRect(foreground, out rect)) return;
                if (IsParkedRect(rect)) return;
                if (rect.Right - rect.Left < 400 || rect.Bottom - rect.Top < 300) return;

                IntPtr previous;
                if (messengerKnownHwnd.TryGetValue(app, out previous) && previous == foreground) return;
                messengerKnownHwnd[app] = foreground;
                MessengerLog("learned " + app + " hwnd=" + foreground + " " + DescribeSize(foreground));
            }
            catch (Exception) { }
        }

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
            IntPtr bestAny = IntPtr.Zero;
            long bestAnyArea = 0;

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

                // 屏幕外的"停靠窗口"（微信托盘态）也要留作候选，靠 Unpark 恢复
                if (area > bestAnyArea) { bestAnyArea = area; bestAny = hwnd; }

                if (IsWindowVisible(hwnd) && !IsParkedRect(rect))
                {
                    if (area > bestVisibleArea) { bestVisibleArea = area; bestVisible = hwnd; }
                }
                return true;
            }, IntPtr.Zero);

            return bestVisible != IntPtr.Zero ? bestVisible : bestAny;
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

        // 微信"最小化到托盘"其实把主窗口挪到 -32000,-32000 并缩成 160x28
        static bool IsParkedRect(Win32Rect rect)
        {
            if (rect.Left <= -30000 || rect.Top <= -30000) return true;
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            return w < 300 || h < 200;
        }

        // 把停靠在屏幕外的窗口恢复成正常大小，放到屏幕中间
        void UnparkMessengerWindow(IntPtr hwnd)
        {
            double dpi = DpiScale();
            Rect work = SystemParameters.WorkArea;
            double width = Math.Min(980, work.Width * 0.62);
            double height = Math.Min(700, work.Height * 0.82);

            int x = (int)Math.Round((work.Left + (work.Width - width) / 2) * dpi);
            int y = (int)Math.Round((work.Top + (work.Height - height) / 2) * dpi);
            int w = (int)Math.Round(width * dpi);
            int h = (int)Math.Round(height * dpi);

            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SwpNoZorder | SwpShowWindow);
            MessengerLog("unpark -> " + x + "," + y + " " + w + "x" + h);
        }

        const uint KeyEventKeyUp = 0x0002;

        static byte VirtualKeyOf(string token)
        {
            if (token.Length == 1 && char.IsLetterOrDigit(token[0])) return (byte)char.ToUpperInvariant(token[0]);
            if (token.Length >= 2 && token.Length <= 3 && token[0] == 'f')
            {
                int n;
                if (int.TryParse(token.Substring(1), out n) && n >= 1 && n <= 12) return (byte)(0x70 + n - 1);
            }
            switch (token)
            {
                case "space": return 0x20;
                case "tab": return 0x09;
                case "enter": return 0x0D;
                case "esc": return 0x1B;
                case "up": return 0x26;
                case "down": return 0x28;
                case "left": return 0x25;
                case "right": return 0x27;
            }
            return 0;
        }

        static void SendHotkey(string combo)
        {
            if (string.IsNullOrEmpty(combo)) return;
            string[] parts = combo.ToLowerInvariant().Split('+');
            var modifiers = new List<byte>();
            byte main = 0;
            foreach (string raw in parts)
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;
                if (token == "ctrl" || token == "control") modifiers.Add(0x11);
                else if (token == "alt") modifiers.Add(0x12);
                else if (token == "shift") modifiers.Add(0x10);
                else if (token == "win") modifiers.Add(0x5B);
                else main = VirtualKeyOf(token);
            }
            if (main == 0) return;

            foreach (byte modifier in modifiers) keybd_event(modifier, 0, 0, UIntPtr.Zero);
            keybd_event(main, 0, 0, UIntPtr.Zero);
            keybd_event(main, 0, KeyEventKeyUp, UIntPtr.Zero);
            for (int i = modifiers.Count - 1; i >= 0; i--) keybd_event(modifiers[i], 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        // data\messenger.txt 里写 wechat_hotkey=ctrl+alt+w 就能启用
        static string ReadMessengerHotkey(string appId)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "messenger.txt");
                if (!File.Exists(path)) return null;
                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == '#') continue;
                    int equals = text.IndexOf('=');
                    if (equals <= 0) continue;
                    string key = text.Substring(0, equals).Trim().ToLowerInvariant();
                    if (key != appId + "_hotkey") continue;
                    return text.Substring(equals + 1).Trim();
                }
            }
            catch (Exception) { }
            return null;
        }
        // 点提示：只在窗口本来就在桌面上时切前台。
        //
        // 不强行显示托盘里的窗口 —— 实测那样做会得到一个"白屏、点不动"的窗口
        // （客户端自己不知道要重绘）。宁可提示用户去点托盘图标。
        void OpenMessengerApp(string id)
        {
            MessengerApp app = MessengerById(id);

            IntPtr hwnd;
            if (messengerKnownHwnd.TryGetValue(app.Id, out hwnd) && hwnd != IntPtr.Zero && !IsWindow(hwnd))
            {
                hwnd = IntPtr.Zero;
            }
            if (hwnd == IntPtr.Zero) hwnd = FindMessengerWindow(app);

            bool usable = false;
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            {
                Win32Rect rect;
                if (GetWindowRect(hwnd, out rect) && !IsParkedRect(rect)) usable = true;
            }

            if (usable)
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                ForceForeground(hwnd);
                MessengerLog("open " + id + " -> foreground hwnd=" + hwnd + " size=" + DescribeSize(hwnd));
                return;
            }

            // 托盘里：先试客户端自己的"显示"热键 —— 由客户端自己执行显示，
            // 不会出现白屏；没配过就只能提示用户手动点托盘。
            string hotkey = ReadMessengerHotkey(app.Id);
            if (!string.IsNullOrEmpty(hotkey))
            {
                MessengerLog("open " + id + " -> send hotkey " + hotkey);
                SendHotkey(hotkey);

                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                retry.Tick += delegate
                {
                    retry.Stop();
                    IntPtr again = FindMessengerWindow(app);
                    Win32Rect rect;
                    if (again != IntPtr.Zero && IsWindowVisible(again) &&
                        GetWindowRect(again, out rect) && !IsParkedRect(rect))
                    {
                        ForceForeground(again);
                        MessengerLog("hotkey opened " + id + " hwnd=" + again);
                        return;
                    }
                    ShowToolMessage(app.Display, app.Display + " 的热键没能唤出窗口，点一下任务栏托盘图标。");
                };
                retry.Start();
                return;
            }

            MessengerLog("open " + id + " -> in tray, asking user");
            ShowToolMessage(app.Display,
                app.Display + " 在托盘里。点一下任务栏托盘图标就能打开，硬拉出来会白屏。");
        }
    }
}
