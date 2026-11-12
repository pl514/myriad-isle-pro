using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    // 红果短剧小窗：横屏（尺寸等于抖音窗口旋转 90°），里面是 WebView2 打开红果官网。
    // 交互和抖音窗口一致：顶栏拖动、[—] 收起、[×] 飞回胶囊（动画由主窗口演）。
    internal sealed class HongguoPlayerWindow : Window
    {
        public event EventHandler UserClosed;
        public event EventHandler Revealed;
        public Action<HongguoPlayerWindow> ExitRequested;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        readonly WebView2 view;
        readonly TextBlock statusText;
        readonly TextBlock hotkeyText;

        IntPtr handle;
        bool cloaked;
        bool roundedApplied;
        bool contentReady;
        bool revealPending;
        DispatcherTimer revealTimer;

        bool dragging;
        double dragDpi = 1.0, dragCursorX, dragCursorY, dragStartLeft, dragStartTop;
        double defaultLeft, defaultTop;

        static string PositionFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hongguo.txt"); }
        }

        static string UserDataFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "webview2-hongguo"); }
        }

        public HongguoPlayerWindow()
        {
            Title = "红果短剧";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(13, 15, 21));
            FontFamily = new FontFamily("Microsoft YaHei UI");

            // 横屏：宽高取抖音窗口的"旋转 90°"（抖音是竖长，这里换成横向）
            Rect work = SystemParameters.WorkArea;
            double w = Math.Max(560, Math.Min(900, work.Height * 0.72));
            double h = Math.Max(320, Math.Min(520, work.Width * 0.22));
            Width = w;
            Height = h;
            defaultLeft = work.Left + (work.Width - w) / 2;
            defaultTop = work.Top + Math.Max(8, (work.Height - h) / 2 - 60);
            Left = defaultLeft;
            Top = defaultTop;
            LoadPosition();

            // ---- 顶栏 ----
            var header = new Border
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(22, 24, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 42, 54)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var headerGrid = new Grid();

            var dragZone = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.SizeAll };
            dragZone.MouseLeftButtonDown += DragDown;
            dragZone.MouseMove += DragMoveHandler;
            dragZone.MouseLeftButtonUp += DragUp;
            headerGrid.Children.Add(dragZone);

            headerGrid.Children.Add(new TextBlock
            {
                Text = "红果短剧",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 218)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                IsHitTestVisible = false
            });

            hotkeyText = new TextBlock
            {
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 127, 145)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 86, 0),
                IsHitTestVisible = false
            };
            headerGrid.Children.Add(hotkeyText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            buttons.Children.Add(BuildHeaderButton(false, "收起到后台（跟热键一样）", ToggleVisible));
            buttons.Children.Add(BuildHeaderButton(true, "关闭并飞回胶囊", ExitPlayer));
            headerGrid.Children.Add(buttons);
            header.Child = headerGrid;

            statusText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 218)),
                Background = new SolidColorBrush(Color.FromArgb(200, 22, 24, 32)),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            view = new WebView2();
            var body = new Grid();
            body.Children.Add(view);
            body.Children.Add(statusText);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(header);
            Grid.SetRow(header, 0);
            root.Children.Add(body);
            Grid.SetRow(body, 1);
            Content = root;

            Loaded += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                ApplyRoundedCorners();
                InitWebView();
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                SavePosition();
            };
            SizeChanged += delegate { SavePosition(); };
            LocationChanged += delegate { SavePosition(); };
            MouseLeftButtonDown += delegate { if (!dragging) Activate(); };
        }

        Border BuildHeaderButton(bool close, string tip, Action click)
        {
            var icon = new Grid { Width = 12, Height = 12, IsHitTestVisible = false };
            if (close)
            {
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(-45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                icon.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }

            var button = new Border
            {
                Width = 30,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = icon
            };
            button.MouseEnter += delegate { button.Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate { click(); };
            return button;
        }

        // ------------------------------------------------------------ 显隐

        public void SetHotkeyText(string text)
        {
            if (hotkeyText != null) hotkeyText.Text = string.IsNullOrEmpty(text) ? "" : text;
        }

        public void ToggleVisible()
        {
            if (IsVisible) HidePlayer(); else ShowPlayer();
        }

        public void ShowPlayer()
        {
            Show();
            Reveal();
            Activate();
        }

        public void HidePlayer()
        {
            Hide();
        }

        public void HideInstant()
        {
            try { Hide(); } catch (Exception) { }
        }

        public Rect ScreenRect()
        {
            return new Rect(Left, Top, Width, Height);
        }

        public void PrepareCloaked()
        {
            contentReady = false;
            revealPending = false;
            Show();
            Cloak(true);
        }

        public void Reveal()
        {
            if (!cloaked) return;
            if (contentReady) { UncloakNow(); return; }
            revealPending = true;
            if (revealTimer == null)
            {
                revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
                revealTimer.Tick += delegate
                {
                    revealTimer.Stop();
                    revealPending = false;
                    UncloakNow();
                };
            }
            revealTimer.Stop();
            revealTimer.Start();
        }

        void UncloakNow()
        {
            if (!cloaked) return;
            Cloak(false);
            if (Revealed != null) { try { Revealed(this, EventArgs.Empty); } catch (Exception) { } }
        }

        public void ResetPosition()
        {
            try { if (File.Exists(PositionFile)) File.Delete(PositionFile); } catch (Exception) { }
            Left = defaultLeft;
            Top = defaultTop;
        }

        void Cloak(bool on)
        {
            cloaked = on;
            try
            {
                if (handle == IntPtr.Zero) handle = new WindowInteropHelper(this).Handle;
                int value = on ? 1 : 0;
                DwmSetWindowAttribute(handle, 13, ref value, sizeof(int));
            }
            catch (Exception) { }
        }

        void ApplyRoundedCorners()
        {
            if (roundedApplied) return;
            roundedApplied = true;
            try
            {
                int preference = 2;
                DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
            }
            catch (Exception) { }
        }

        void ExitPlayer()
        {
            if (ExitRequested != null)
            {
                try { ExitRequested(this); } catch (Exception) { }
                return;
            }
            Finish();
        }

        public void Finish()
        {
            try { if (view != null && view.CoreWebView2 != null) view.Dispose(); } catch (Exception) { }
            try { Close(); } catch (Exception) { }
            if (UserClosed != null) { try { UserClosed(this, EventArgs.Empty); } catch (Exception) { } }
        }

        // ------------------------------------------------------------ 拖动

        void DragDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            dragging = true;
            dragDpi = Dpi();
            dragCursorX = System.Windows.Forms.Cursor.Position.X / dragDpi;
            dragCursorY = System.Windows.Forms.Cursor.Position.Y / dragDpi;
            dragStartLeft = Left;
            dragStartTop = Top;
            ((UIElement)sender).CaptureMouse();
        }

        void DragMoveHandler(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { dragging = false; return; }
            Left = dragStartLeft + (System.Windows.Forms.Cursor.Position.X / dragDpi - dragCursorX);
            Top = dragStartTop + (System.Windows.Forms.Cursor.Position.Y / dragDpi - dragCursorY);
        }

        void DragUp(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            try { ((UIElement)sender).ReleaseMouseCapture(); } catch (Exception) { }
            SavePosition();
        }

        double Dpi()
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1.0, g.DpiX / 96.0);
            }
            catch (Exception) { return 1.0; }
        }

        void LoadPosition()
        {
            try
            {
                if (!File.Exists(PositionFile)) return;
                string[] parts = File.ReadAllText(PositionFile).Trim().Split(',');
                double left, top;
                if (parts.Length >= 2 && double.TryParse(parts[0], out left) && double.TryParse(parts[1], out top))
                {
                    Rect work = SystemParameters.WorkArea;
                    Left = Math.Max(work.Left - 40, Math.Min(work.Right - 80, left));
                    Top = Math.Max(work.Top - 20, Math.Min(work.Bottom - 60, top));
                }
            }
            catch (Exception) { }
        }

        void SavePosition()
        {
            try
            {
                string folder = Path.GetDirectoryName(PositionFile);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(PositionFile, ((int)Left).ToString() + "," + ((int)Top).ToString());
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ WebView2

        async void InitWebView()
        {
            try
            {
                string folder = UserDataFolder;
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, folder, options);
                await view.EnsureCoreWebView2Async(env);
                CoreWebView2 core = view.CoreWebView2;
                if (core == null) { ShowStatus("红果窗口初始化失败：WebView2 没有就绪。"); return; }

                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                    try { core.Navigate(args.Uri); } catch (Exception) { }
                };
                core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (!args.IsSuccess) ShowStatus("页面加载失败（" + args.WebErrorStatus + "），检查一下网络。");
                    else HideStatus();
                    contentReady = true;
                    if (revealPending && cloaked)
                    {
                        revealPending = false;
                        if (revealTimer != null) revealTimer.Stop();
                        UncloakNow();
                    }
                    Dispatcher.BeginInvoke(new Action(CleanLayout), DispatcherPriority.Background);
                };

                core.Navigate("https://hongguoduanju.com/");
            }
            catch (Exception ex)
            {
                ShowStatus("红果窗口启动失败：" + ex.Message);
            }
        }

        // 横屏样式：去掉滚动条、把播放器区域的圆角/边距收紧
        void CleanLayout()
        {
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                view.CoreWebView2.ExecuteScriptAsync(
                    "(function(){try{if(!document.getElementById('dsh-hg')){var st=document.createElement('style');" +
                    "st.id='dsh-hg';st.textContent='::-webkit-scrollbar{width:0!important;height:0!important}" +
                    "html,body{scrollbar-width:none!important;-ms-overflow-style:none!important}';" +
                    "document.head.appendChild(st);}return 'ok';}catch(e){return 'err';}})()");
            }
            catch (Exception) { }
        }

        void ShowStatus(string text)
        {
            statusText.Text = text;
            statusText.Visibility = Visibility.Visible;
        }

        void HideStatus()
        {
            statusText.Visibility = Visibility.Collapsed;
        }
    }
}
