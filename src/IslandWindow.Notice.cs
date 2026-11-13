using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 胶囊「来消息提示」：
    //   收到微信/QQ 新消息 → 胶囊变宽、眼睛淡出、显示「微信 有新消息」
    //   → 1.6 秒后自动回到眨眼形态；提示期间点胶囊 = 直接打开对应客户端。
    //
    // 消息信号来自任务栏闪烁（HSHELL_FLASH）。这是唯一不要权限的可用信号；
    // 通知内容读不到（系统要求包标识，已实测 0x80073D54）。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
        [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);

        const int HShellFlash = 0x8006;          // HSHELL_REDRAW | 0x8000
        const double CapsuleNoticeWidth = 208;
        const int NoticeHoldMs = 2000;   // 显示 2 秒后自动变回眼睛

        Grid capsuleNotice;
        TextBlock capsuleNoticeText;
        Border capsuleNoticeDot;
        DispatcherTimer capsuleNoticeTimer;
        bool capsuleNoticeActive;
        bool capsuleNoticeHidMusic;
        string capsuleNoticeApp = "wechat";
        string pendingNoticeApp = "";
        bool unreadWechat;
        DateTime suppressWechatUntil;
        DateTime suppressQqUntil;
        bool unreadQq;
        readonly List<DependencyObject> capsuleNoticeButtons = new List<DependencyObject>();

        uint shellHookMessage;
        bool shellHookRegistered;
        AudioWatcher audioWatcher;

        // 把提示层挂到胶囊里（胶囊内容是一个 Grid，里面已有 layers + 迷你播放器）
        void AttachCapsuleNotice()
        {
            if (capsule == null || capsuleNotice != null) return;
            Grid root = capsule.Child as Grid;
            if (root == null) return;

            capsuleNotice = BuildCapsuleNotice();
            root.Children.Add(capsuleNotice);

            SourceInitialized += delegate { RegisterSheetHook(); };

            StartMessengerLearning();

            // 主信号：微信/QQ 的消息提示音
            try
            {
                audioWatcher = new AudioWatcher(delegate(string appId)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        MessengerLog("audio trigger " + appId);
                        if (!capsuleNoticeActive) ShowCapsuleNotice(appId);
                    }), DispatcherPriority.Background);
                });
                audioWatcher.Start();

            }
            catch (Exception ex)
            {
                MessengerLog("audio watcher failed: " + ex.Message);
            }

            Closed += delegate
            {
                if (audioWatcher != null) audioWatcher.Dispose();
            };
        }

        // 展开面板里、三点球右边的消息胶囊：绿点 + 微信/QQ 未读消息
        Border panelNoticeChip;
        Border panelNoticeDot;
        TextBlock panelNoticeText;
        string panelNoticeApp;

        FrameworkElement BuildPanelNoticeChip()
        {
            panelNoticeDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(7, 193, 96)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            panelNoticeText = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 236, 246)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(panelNoticeDot);
            row.Children.Add(panelNoticeText);

            panelNoticeChip = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(230, 34, 38, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 12, 5),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = row,
                ToolTip = "左键：微信　右键：QQ"
            };
            // 两家都有未读：左键微信、右键 QQ；只有一家未读：左右键都打开那一家
            panelNoticeChip.MouseLeftButtonUp += delegate
            {
                if (unreadWechat) { OpenMessengerApp("wechat"); unreadWechat = false; }
                else if (unreadQq) { OpenMessengerApp("qq"); unreadQq = false; }
                SyncPendingNotice();
                RefreshPanelNoticeChip();
            };
            panelNoticeChip.MouseRightButtonUp += delegate
            {
                if (unreadQq) { OpenMessengerApp("qq"); unreadQq = false; }
                else if (unreadWechat) { OpenMessengerApp("wechat"); unreadWechat = false; }
                SyncPendingNotice();
                RefreshPanelNoticeChip();
            };
            return panelNoticeChip;
        }

        void ShowPanelNoticeChip(string appId)
        {
            if (panelNoticeChip == null) return;
            if (appId == "qq") unreadQq = true; else unreadWechat = true;
            pendingNoticeApp = appId == "qq" ? "qq" : "wechat";
            RefreshPanelNoticeChip();
        }

        // 按未读集合刷新：单个显示名字，两个都显示"微信/QQ"
        void SyncPendingNotice()
        {
            if (unreadQq) pendingNoticeApp = "qq";
            else if (unreadWechat) pendingNoticeApp = "wechat";
            else pendingNoticeApp = null;
        }

        void RefreshPanelNoticeChip()
        {
            if (panelNoticeChip == null) return;
            if (!unreadWechat && !unreadQq)
            {
                panelNoticeChip.Visibility = Visibility.Collapsed;
                return;
            }

            if (unreadWechat && unreadQq)
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(7, 193, 96));
                panelNoticeText.Text = "微信/QQ 未读消息";
            }
            else if (unreadQq)
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(18, 183, 245));
                panelNoticeText.Text = "QQ 未读消息";
            }
            else
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(7, 193, 96));
                panelNoticeText.Text = "微信 未读消息";
            }

            panelNoticeChip.Visibility = Visibility.Visible;
            panelNoticeChip.IsHitTestVisible = true;
        }

        // 只是把胶囊收起来（比如收起面板、展开功能球），未读状态保留
        void HidePanelNoticeChip()
        {
            if (panelNoticeChip == null) return;
            panelNoticeChip.Visibility = Visibility.Collapsed;
            panelNoticeApp = null;
        }

        Grid BuildCapsuleNotice()
        {
            var grid = new Grid
            {
                Visibility = Visibility.Collapsed,
                Opacity = 0,
                IsHitTestVisible = true,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            capsuleNoticeDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(7, 193, 96))
            };
            row.Children.Add(capsuleNoticeDot);

            capsuleNoticeText = new TextBlock
            {
                Text = "有新消息",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(236, 240, 248)),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(capsuleNoticeText);

            grid.Children.Add(row);
            grid.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenMessengerApp(capsuleNoticeApp);
            ClearUnreadNotice();
                HideCapsuleNotice();
            };
            capsuleNoticeButtons.Add(grid);
            capsuleNoticeButtons.Add(row);
            capsuleNoticeButtons.Add(capsuleNoticeText);
            capsuleNoticeButtons.Add(capsuleNoticeDot);
            return grid;
        }

        bool IsCapsuleNoticeSource(object source)
        {
            if (!capsuleNoticeActive || capsuleNotice == null) return false;
            DependencyObject node = source as DependencyObject;
            int guard = 0;
            while (node != null && guard++ < 32)
            {
                if (node == capsuleNotice) return true;
                if (node == capsule) return false;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        // 该客户端是否在前台：我们自己切过去时的提示音不算新消息
        bool IsMessengerForeground(string appId)
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                int pid;
                GetWindowThreadProcessId(foreground, out pid);
                return MessengerIdByPid(pid) == appId;
            }
            catch (Exception) { return false; }
        }

        // 跳转后的几秒内忽略该客户端的音效（切窗口/登录音会被误判成新消息）
        void SuppressApp(string appId, int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            if (appId == "qq") suppressQqUntil = until; else suppressWechatUntil = until;
        }

        bool IsSuppressed(string appId)
        {
            return appId == "qq" ? DateTime.UtcNow < suppressQqUntil : DateTime.UtcNow < suppressWechatUntil;
        }


        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        void ShowCapsuleNotice(string appId)
        {
            if (capsuleNotice == null) return;
            if (IsSuppressed(appId == "qq" ? "qq" : "wechat")) return;
            if (IsMessengerForeground(appId == "qq" ? "qq" : "wechat")) return;
            if (IsMessengerForeground(appId == "qq" ? "qq" : "wechat")) return;
            if (expanded)
            {
                // 面板展开时：提示出现在三点球右边的消息胶囊里
                pendingNoticeApp = appId == "qq" ? "qq" : "wechat";
                ShowPanelNoticeChip(appId);
                return;
            }
            capsuleNoticeApp = appId == "qq" ? "qq" : "wechat";
            bool isQq = capsuleNoticeApp == "qq";
            bool isDsh = capsuleNoticeApp == "dsh";

            capsuleNoticeDot.Background = new SolidColorBrush(isDsh
                ? Color.FromRgb(124, 108, 242)
                : isQq ? Color.FromRgb(18, 183, 245) : Color.FromRgb(7, 193, 96));
            capsuleNoticeText.Text = (isQq ? "QQ" : "微信") + " 有新消息";

            capsuleNoticeActive = true;
            pendingNoticeApp = capsuleNoticeApp;
            if (capsuleNoticeApp == "qq") unreadQq = true; else unreadWechat = true;
            capsuleNotice.Visibility = Visibility.Visible;
            capsuleNotice.IsHitTestVisible = true;
            Motion.Fade(capsuleNotice, 1, 140, Motion.Enter, 0, null);

            // 通知优先：正在放歌的话先把音乐迷你播放器收起来，避免两层叠在一起
            if (capsuleMiniActive && capsuleMiniPlayer != null)
            {
                capsuleNoticeHidMusic = true;
                UIElement player = capsuleMiniPlayer;
                Motion.Fade(player, 0, 100, Motion.Exit, 0, delegate
                {
                    if (capsuleNoticeActive && capsuleMiniPlayer != null) capsuleMiniPlayer.Visibility = Visibility.Collapsed;
                });
            }

            if (capsuleEyes != null && capsuleEyes.Visibility == Visibility.Visible)
            {
                UIElement eyes = capsuleEyes;
                Motion.Fade(eyes, 0, 110, Motion.Exit, 0, delegate
                {
                    if (capsuleNoticeActive) eyes.Visibility = Visibility.Collapsed;
                });
            }

            AnimateCapsuleWidth(CapsuleNoticeWidth);

            if (capsuleNoticeTimer == null)
            {
                capsuleNoticeTimer = new DispatcherTimer();
                capsuleNoticeTimer.Tick += delegate
                {
                    capsuleNoticeTimer.Stop();
                    HideCapsuleNotice();
                };
            }
            capsuleNoticeTimer.Stop();
            capsuleNoticeTimer.Interval = TimeSpan.FromMilliseconds(NoticeHoldMs);
            capsuleNoticeTimer.Start();
        }

        // 面板展开期间收到的消息：收起后再补显示 2 秒
        void ShowPendingNotice()
        {
            if (string.IsNullOrEmpty(pendingNoticeApp)) return;
            string app = pendingNoticeApp;
            pendingNoticeApp = null;
            Motion.Delay(420, delegate { if (!expanded) ShowCapsuleNotice(app); });
        }


        void HideCapsuleNotice()
        {
            if (capsuleNotice == null) return;
            capsuleNoticeActive = false;
            if (capsuleNoticeTimer != null) capsuleNoticeTimer.Stop();

            if (capsuleNotice != null)
            {
                Motion.Fade(capsuleNotice, 0, 120, Motion.Exit, 0, delegate
                {
                    if (!capsuleNoticeActive) capsuleNotice.Visibility = Visibility.Collapsed;
                });
            }

            // 音乐迷你播放器可能还开着，宽度要回到它该有的值
            AnimateCapsuleWidth(capsuleMiniActive ? CapsuleMusicWidth : CapsuleIdleWidth);

            // 恢复被通知挤掉的音乐胶囊
            if (capsuleNoticeHidMusic)
            {
                capsuleNoticeHidMusic = false;
                if (capsuleMiniActive && capsuleMiniPlayer != null && !expanded)
                {
                    capsuleMiniPlayer.Visibility = Visibility.Visible;
                    capsuleMiniPlayer.Opacity = 0;
                    Motion.Fade(capsuleMiniPlayer, 1, 150, Motion.Enter, 60, null);
                }
            }

            if (capsuleEyes != null && !capsuleMiniActive)
            {
                capsuleEyes.Visibility = Visibility.Visible;
                Motion.Fade(capsuleEyes, 1, 170, Motion.Enter, 80, null);
            }
        }

        // ---------------- 任务栏闪烁 ----------------

        void RegisterSheetHook()
        {
            if (shellHookRegistered) return;
            try
            {
                if (handle == IntPtr.Zero) return;
                shellHookMessage = RegisterWindowMessage("SHELLHOOK");
                RegisterShellHookWindow(handle);
                if (source != null) source.AddHook(ShellHookProc);
                shellHookRegistered = true;
            }
            catch (Exception) { }
        }

        IntPtr ShellHookProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (shellHookRegistered && (uint)message == shellHookMessage && wParam.ToInt32() == HShellFlash)
                {
                    int pid;
                    GetWindowThreadProcessId(lParam, out pid);
                    string appId = MessengerIdByPid(pid);
                    if (!string.IsNullOrEmpty(appId))
                    {
                        MessengerLog("flash from " + appId + " hwnd=" + lParam);
                        // 闪烁可能连发多次，节流
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (!capsuleNoticeActive) ShowCapsuleNotice(appId);
                        }), DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception) { }
            return IntPtr.Zero;
        }
    }
}
