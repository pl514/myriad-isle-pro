using System;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 抖音小窗的"管家"：谁创建、谁显示、谁销毁，都集中在这里，
    // 免得 IslandWindow 里到处散着 if (douyinWindow != null)。
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

        // 绿球点击：没有就创建，有就显示到前面
        void ShowDouyinWindow()
        {
            if (douyinWindow == null)
            {
                douyinWindow = new DouyinPlayerWindow();
                douyinWindow.UserClosed += delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        douyinWindow = null;
                        SetToolRunning(FindTool("douyin"), false);
                    }), DispatcherPriority.Background);
                };
            }
            douyinWindow.SetHotkeyText(DouyinHotkeyDisplay());
            douyinWindow.ShowPlayer();
            SetToolRunning(FindTool("douyin"), true);
        }

        // 热键 / 顶栏 [—]：收起与显示之间切换
        void ToggleDouyinWindow()
        {
            if (douyinWindow == null) return;
            douyinWindow.SetHotkeyText(DouyinHotkeyDisplay());
            douyinWindow.ToggleVisible();
        }

        // 顶栏 [×] 或关闭后：彻底释放
        void ShutdownDouyinWindow()
        {
            if (douyinWindow == null) return;
            try { douyinWindow.Close(); } catch (Exception) { }
            douyinWindow = null;
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
