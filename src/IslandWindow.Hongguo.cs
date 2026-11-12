using System;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // 红果短剧小窗的管家：和抖音那套完全一致（点球形变展开 / [—] 原地收起 / [×] 飞回胶囊）。
    internal sealed partial class IslandWindow
    {
        HongguoPlayerWindow hongguoWindow;

        // 红果是独立客户端：只做小窗联动（置顶横屏 + 显隐 + 位置记忆），不做形变动画
        void OpenHongguoOverlay()
        {
            Rect to = HongguoAppOverlay.PlannedRect();

            if (expanded) SetExpanded(false);
            SetToolRunning(FindTool("hongguo"), true);

            MessengerLog("hongguo overlay open to=" + to);
            System.Threading.Tasks.Task.Run(delegate
            {
                HongguoAppOverlay.Prepare(to);
                MessengerLog("hongguo prepared hwnd=" + HongguoAppOverlay.Handle);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (HongguoAppOverlay.Handle == IntPtr.Zero)
                    {
                        // 客户端没起来：退回网页窗口
                        ShowHongguoWindow();
                        return;
                    }
                    HongguoAppOverlay.ShowAt(to);
                    StartHongguoWatch();
                }), DispatcherPriority.Background);
            });
        }

        // 客户端自己点 × 退出时把状态复位（不播动画）
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
                    SetToolRunning(FindTool("hongguo"), false);
                };
            }
            hongguoWatch.Stop();
            hongguoWatch.Start();
        }

        // 收起（不退出进程）
        void HideHongguoOverlay()
        {
            HongguoAppOverlay.SaveRect();
            HongguoAppOverlay.Hide();
            SetToolRunning(FindTool("hongguo"), false);
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
