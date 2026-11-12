using System;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // 红果短剧小窗的管家：和抖音那套完全一致（点球形变展开 / [—] 原地收起 / [×] 飞回胶囊）。
    internal sealed partial class IslandWindow
    {
        HongguoPlayerWindow hongguoWindow;

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
                HongguoAppOverlay.Toggle();
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
