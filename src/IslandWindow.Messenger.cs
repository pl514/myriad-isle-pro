using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 微信 / QQ 小窗：不嵌内容，只"摆"别人的窗口。
    //
    // 为什么只能摆：它们是没有 web 层的原生客户端，内容塞不进分层窗口。
    // 但把窗口移到胶囊下方、改成手机比例、再一键还原，体验上已经够用，
    // 而且零权限、零封号风险。
    //
    // 已验证（2026-11-07，微信 4.2 / QQ）：
    //   · 两个都接受 SetWindowPos 缩放到 420×760；
    //   · 微信有两个同名窗口，必须按"可见 + 面积最大"挑主窗口。
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

        [StructLayout(LayoutKind.Sequential)]
        struct Win32Rect { public int Left, Top, Right, Bottom; }

        const uint SwpNoZorder = 0x0004;
        const uint SwpShowWindow = 0x0040;
        const int SwShow = 5;
        const int SwRestore = 9;

        sealed class MessengerApp
        {
            public string Id;
            public string Display;
            public string[] ProcessNames;
            public string ClassHint;
            public string[] TitleHints;
        }

        static readonly MessengerApp WeChatApp = new MessengerApp
        {
            Id = "wechat",
            Display = "微信",
            ProcessNames = new string[] { "Weixin", "WeChat" },
            ClassHint = "QWindowIcon",
            TitleHints = new string[] { "微信", "Weixin", "WeChat" }
        };

        static readonly MessengerApp QqApp = new MessengerApp
        {
            Id = "qq",
            Display = "QQ",
            ProcessNames = new string[] { "QQ", "QQNT" },
            ClassHint = "Chrome_WidgetWin_1",
            TitleHints = new string[] { "QQ" }
        };

        // 当前吸附着的窗口（没吸附时 hwnd 为零）
        IntPtr messengerHwnd;
        string messengerActiveId;
        Win32Rect messengerOriginalRect;
        bool messengerOriginalValid;

        static MessengerApp MessengerById(string id)
        {
            if (id == "qq") return QqApp;
            return WeChatApp;
        }

        // 点球 / 点胶囊提示时调用：已吸附就还原，否则吸过来
        void ToggleMessengerWindow(string id)
        {
            MessengerApp app = MessengerById(id);

            if (messengerHwnd != IntPtr.Zero && messengerActiveId == id)
            {
                RestoreMessengerWindow();
                ShowToolMessage(app.Display, "已还原窗口");
                return;
            }

            if (messengerHwnd != IntPtr.Zero) RestoreMessengerWindow();

            IntPtr hwnd = FindMessengerWindow(app);
            if (hwnd == IntPtr.Zero)
            {
                ShowToolMessage(app.Display, "没有检测到 " + app.Display + " 的窗口，先把它打开。");
                SetToolRunning(FindTool(id), false);
                return;
            }

            // 最小化到托盘时窗口是隐藏的，得先叫出来
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

            messengerHwnd = hwnd;
            messengerActiveId = id;
            SetToolRunning(FindTool(id), true);
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
            }
            if (messengerActiveId != null) SetToolRunning(FindTool(messengerActiveId), false);
            messengerHwnd = IntPtr.Zero;
            messengerActiveId = null;
            messengerOriginalValid = false;
        }

        // 吸附目标：胶囊正下方，手机比例
        Rect MessengerTargetRect()
        {
            Rect capsuleRect = ScreenRectOf(capsule);
            Rect work = SystemParameters.WorkArea;
            double width = Math.Max(360, Math.Min(460, work.Width * 0.24));
            double height = Math.Min(work.Height - 40, work.Height * 0.78);

            double left = capsuleRect.IsEmpty
                ? work.Left + (work.Width - width) / 2
                : capsuleRect.Left + (capsuleRect.Width - width) / 2;
            left = Math.Max(work.Left + 8, Math.Min(left, work.Right - width - 8));

            double top = capsuleRect.IsEmpty ? work.Top + 60 : capsuleRect.Bottom + 10;
            top = Math.Min(top, work.Bottom - height - 8);

            return new Rect(left, top, Math.Max(320, width), Math.Max(420, height));
        }

        // 找主窗口：同名进程 + 类名匹配，优先可见、再按面积最大
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
                if (app.ClassHint != null && cls.ToString().IndexOf(app.ClassHint, StringComparison.OrdinalIgnoreCase) < 0) return true;

                Win32Rect rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area < 200 * 200) return true;   // 小浮窗、提示窗全部排除

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

        // 给"有新消息"提示用：客户端是否正在运行
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
    }
}
