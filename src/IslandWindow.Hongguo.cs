using System;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // 红果短剧小窗的管家：和抖音那套完全一致（点球形变展开 / [—] 原地收起 / [×] 飞回胶囊）。
    internal sealed partial class IslandWindow
    {
        HongguoPlayerWindow hongguoWindow;

        // 外部客户端小窗：启动（后台）→ 形变 → 显示 → 自绘顶栏
        void OpenHongguoOverlay()
        {
            Rect from = ScreenRectOf(panel);
            if (from.IsEmpty) from = ScreenRectOf(capsule);
            Rect to = HongguoAppOverlay.PlannedRect();

            if (expanded) SetExpanded(false);
            SetToolRunning(FindTool("hongguo"), true);

            MessengerLog("hongguo overlay open from=" + from + " to=" + to);
            System.Threading.Tasks.Task.Run(delegate
            {
                HongguoAppOverlay.Prepare(to);
                MessengerLog("hongguo prepared hwnd=" + HongguoAppOverlay.Handle);   // 启动/定位/去标题栏（会等窗口创建，放后台别卡 UI）
                HongguoAppOverlay.HoldHidden(460);   // 形变期间按住它，别让它抢先出现在外壳上面
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (HongguoAppOverlay.Handle == IntPtr.Zero)
                    {
                        // 客户端没起来：退回网页窗口
                        ShowHongguoWindow();
                        return;
                    }
                    MorphShellWindow shell = null;
                    shell = MorphShellWindow.Create(from, to, 380, delegate
                    {
                        HongguoAppOverlay.ShowAt(to);
                        if (shell != null) shell.Dismiss();
                        StartHongguoWatch();
                    });
                }), DispatcherPriority.Background);
            });
        }

        // 客户端自己点 × 退出时也要演"飞回胶囊"：这里定时看着它的窗口
        DispatcherTimer hongguoWatch;

        void StartHongguoWatch()
        {
            if (hongguoWatch == null)
            {
                hongguoWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                hongguoWatch.Tick += delegate
                {
                    if (HongguoAppOverlay.Handle != IntPtr.Zero) return;
                    hongguoWatch.Stop();
                    Rect last = HongguoAppOverlay.CurrentRect();
                    Rect home = ScreenRectOf(capsule);
                    SetToolRunning(FindTool("hongguo"), false);
                    if (home.IsEmpty) return;
                    MorphShellWindow back = null;
                    back = MorphShellWindow.Create(last, home, 340, delegate
                    {
                        if (back != null) back.Dismiss();
                    });
                };
            }
            hongguoWatch.Stop();
            hongguoWatch.Start();
        }

        // 收起（不退出进程）：外部窗口 + 顶栏一起藏起来
        void HideHongguoOverlay()
        {
            HongguoAppOverlay.SaveRect();
            HongguoAppOverlay.Hide();
            SetToolRunning(FindTool("hongguo"), false);
        }

        // 关闭：飞回胶囊（进程留着，下次点球继续）
        void CloseHongguoOverlayWithMorph()
        {
            Rect from = HongguoAppOverlay.CurrentRect();
            Rect to = ScreenRectOf(capsule);
            HongguoAppOverlay.SaveRect();
            HongguoAppOverlay.Hide();
            SetToolRunning(FindTool("hongguo"), false);

            if (to.IsEmpty) return;
            MorphShellWindow shell = null;
            shell = MorphShellWindow.Create(from, to, 340, delegate
            {
                if (shell != null) shell.Dismiss();
            });
        }

        void ShowHongguoWindow()
        {
            if (hongguoWindow != null)
            {
                hongguoWindow.SetHotkeyText(HongguoHotkeyDisplay());
                hongguoWindow.ShowPlayer();
                SetToolRunning(FindTool("hongguo"), true);
                return;
            }

            Rect from = ScreenRectOf(panel);
            if (from.IsEmpty) from = ScreenRectOf(capsule);

            hongguoWindow = new HongguoPlayerWindow();
            hongguoWindow.ExitRequested = delegate { ExitHongguoWithMorph(); };
            hongguoWindow.UserClosed += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    hongguoWindow = null;
                    SetToolRunning(FindTool("hongguo"), false);
                }), DispatcherPriority.Background);
            };
            hongguoWindow.SetHotkeyText(HongguoHotkeyDisplay());

            Rect to = hongguoWindow.ScreenRect();
            hongguoWindow.PrepareCloaked();

            if (expanded) SetExpanded(false);
            SetToolRunning(FindTool("hongguo"), true);

            HongguoPlayerWindow window = hongguoWindow;
            MorphShellWindow shell = null;
            shell = MorphShellWindow.Create(from, to, 380, delegate { window.Reveal(); });
            window.Revealed += delegate
            {
                if (shell != null) shell.Dismiss();
            };

            var safety = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            safety.Tick += delegate
            {
                safety.Stop();
                if (shell != null) shell.Dismiss();
            };
            safety.Start();
        }

        void ToggleHongguoWindow()
        {
            // 有外部客户端就优先控制它（全集在里面）
            if (HongguoAppOverlay.Available)
            {
                if (HongguoAppOverlay.IsShown) HideHongguoOverlay();
                else OpenHongguoOverlay();
                return;
            }
            // 没有客户端：用内置网页窗口
            if (hongguoWindow == null)
            {
                ShowHongguoWindow();
                return;
            }
            hongguoWindow.SetHotkeyText(HongguoHotkeyDisplay());
            hongguoWindow.ToggleVisible();
        }

        void ExitHongguoWithMorph()
        {
            if (hongguoWindow == null) return;
            Rect from = hongguoWindow.ScreenRect();
            Rect to = ScreenRectOf(capsule);
            if (to.IsEmpty) { ShutdownHongguoWindow(); return; }

            HongguoPlayerWindow window = hongguoWindow;
            window.HideInstant();

            MorphShellWindow shell = null;
            shell = MorphShellWindow.Create(from, to, 340, delegate
            {
                try
                {
                    window.ResetPosition();
                    window.Finish();
                }
                catch (Exception) { }
                if (shell != null) shell.Dismiss();
            });
        }

        void ShutdownHongguoWindow()
        {
            if (hongguoWindow == null) return;
            HongguoPlayerWindow window = hongguoWindow;
            hongguoWindow = null;
            try { window.Finish(); } catch (Exception) { }
            SetToolRunning(FindTool("hongguo"), false);
        }

        string HongguoHotkeyDisplay()
        {
            if (hotkeys == null) return "";
            foreach (HotkeyBinding item in hotkeys)
            {
                if (item.Action == HotkeyAction.HongguoToggle) return item.Display;
            }
            return "";
        }
    }
}
