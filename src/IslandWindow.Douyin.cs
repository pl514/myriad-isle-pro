using System;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 抖音小窗的"管家"：创建、显示、收起、退出、以及两段形变动画都在这里。
    //
    // 形变规则（用户定的）：
    //   · 点球启动：面板矩形 → 抖音矩形，380ms，然后揭开抖音；
    //   · 点 [—] / 热键：原地收起，再回来还在原地，不重播形变；
    //   · 点 [×]：抖音矩形 → 胶囊矩形，340ms，结束后销毁并重置位置；
    //   · 重置后下次启动，又从胶囊长出来，落回面板展开的位置。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        DouyinPlayerWindow douyinWindow;

        CapsuleTool FindTool(string id)
        {
            foreach (CapsuleTool tool in orbTools)
            {
                if (tool.Id == id) return tool;
            }
            return null;
        }

        // 点球：新建（或唤回已有）抖音窗口
        void ShowDouyinWindow()
        {
            // 已经在跑（比如被 [—] 收起了）：原地唤回，不播形变
            if (douyinWindow != null)
            {
                douyinWindow.SetHotkeyText(DouyinHotkeyDisplay());
                douyinWindow.ShowPlayer();
                SetToolRunning(FindTool("douyin"), true);
                return;
            }

            Rect from = ScreenRectOf(panel);
            if (from.IsEmpty) from = ScreenRectOf(capsule);

            douyinWindow = new DouyinPlayerWindow();
            douyinWindow.ExitRequested = delegate { ExitDouyinWithMorph(); };
            douyinWindow.UserClosed += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    douyinWindow = null;
                    SetToolRunning(FindTool("douyin"), false);
                }), DispatcherPriority.Background);
            };
            douyinWindow.SetHotkeyText(DouyinHotkeyDisplay());

            Rect to = douyinWindow.ScreenRect();
            douyinWindow.PrepareCloaked();

            // 面板收起（形变外壳盖着，看不出来）
            if (expanded) SetExpanded(false);

            SetToolRunning(FindTool("douyin"), true);

            DouyinPlayerWindow window = douyinWindow;
            MorphShellWindow.Play(from, to, 380,
                delegate { window.Reveal(); },
                null);
        }

        // 热键 / 顶栏 [—]：收起与显示切换（原地，不重播形变）
        void ToggleDouyinWindow()
        {
            if (douyinWindow == null) return;
            douyinWindow.SetHotkeyText(DouyinHotkeyDisplay());
            douyinWindow.ToggleVisible();
        }

        // 顶栏 [×]：飞回胶囊，结束后销毁
        void ExitDouyinWithMorph()
        {
            if (douyinWindow == null) return;
            Rect from = douyinWindow.ScreenRect();
            Rect to = ScreenRectOf(capsule);
            if (to.IsEmpty) { ShutdownDouyinWindow(); return; }

            DouyinPlayerWindow window = douyinWindow;
            window.HideInstant();
            MorphShellWindow.Play(from, to, 340, null, delegate
            {
                window.ResetPosition();
                window.Finish();
            });
        }

        void ShutdownDouyinWindow()
        {
            if (douyinWindow == null) return;
            DouyinPlayerWindow window = douyinWindow;
            douyinWindow = null;
            try { window.Finish(); } catch (Exception) { }
            SetToolRunning(FindTool("douyin"), false);
        }

        string DouyinHotkeyDisplay()
        {
            if (hotkeys == null) return "";
            foreach (HotkeyBinding item in hotkeys)
            {
                if (item.Action == HotkeyAction.DouyinToggle) return item.Display;
            }
            return "";
        }
    }
}
