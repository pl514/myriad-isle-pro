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
        const int NoticeHoldMs = 1600;

        Grid capsuleNotice;
        TextBlock capsuleNoticeText;
        Border capsuleNoticeDot;
        DispatcherTimer capsuleNoticeTimer;
        bool capsuleNoticeActive;
        string capsuleNoticeApp = "wechat";
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

        void ShowCapsuleNotice(string appId)
        {
            if (capsuleNotice == null || expanded) return;
            capsuleNoticeApp = appId == "qq" ? "qq" : "wechat";
            bool isQq = capsuleNoticeApp == "qq";

            capsuleNoticeDot.Background = new SolidColorBrush(isQq
                ? Color.FromRgb(18, 183, 245)
                : Color.FromRgb(7, 193, 96));
            capsuleNoticeText.Text = (isQq ? "QQ" : "微信") + " 有新消息";

            capsuleNoticeActive = true;
            capsuleNotice.Visibility = Visibility.Visible;
            capsuleNotice.IsHitTestVisible = true;
            Motion.Fade(capsuleNotice, 1, 140, Motion.Enter, 0, null);

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
