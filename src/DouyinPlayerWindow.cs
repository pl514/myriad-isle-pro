using System;
using System.IO;
using Path = System.IO.Path;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 抖音小窗：一个独立的无边框置顶窗口，里面是 WebView2 打开抖音网页版。
    //
    // 为什么不放进胶囊窗口：胶囊是分层透明窗口（AllowsTransparency），
    // WebView2 是子 HWND，放进去一点画面都不会有。这一点在验证阶段已经实测确认。
    //
    // 和主窗口的分工：
    //   · 热键（可自定义）由主窗口统一注册，回调到这里 ToggleVisible；
    //   · 顶栏 [—] = 收起/显示（等价于按热键）；
    //   · 顶栏 [×] = 退出抖音（销毁 WebView2，下次要点绿球重新启动）；
    //   · 滚轮在窗口范围内被底层钩子截获，转发给 WebView2，游戏里也能刷。
    // ---------------------------------------------------------------------
    internal sealed class DouyinPlayerWindow : Window
    {
        const int WM_MOUSEWHEEL = 0x020A;
        const int WH_MOUSE_LL = 14;

        [DllImport("user32.dll")] static extern bool ClipCursor(IntPtr rect);
        [DllImport("user32.dll")] static extern int ShowCursor(bool show);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string lpModuleName);

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public event EventHandler UserClosed;

        // 真窗口从"隐形"变成可见时触发，主窗口据此收掉形变外壳
        public event EventHandler Revealed;

        // 点 [×] 时交给主窗口去做"飞回胶囊"动画；没设就自己直接关
        public Action<DouyinPlayerWindow> ExitRequested;

        readonly WebView2 view;
        readonly TextBlock statusText;
        readonly TextBlock hotkeyText;

        IntPtr handle;
        IntPtr childHwnd = IntPtr.Zero;
        IntPtr mouseHook = IntPtr.Zero;
        LowLevelMouseProc mouseProc;
        int cursorShown;
        bool cloaked;
        bool roundedApplied;
        bool contentReady;
        bool revealPending;
        DispatcherTimer revealTimer;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        bool dragging;
        double dragDpi = 1.0, dragCursorX, dragCursorY, dragStartLeft, dragStartTop;
        double defaultLeft, defaultTop;

        static string PositionFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "douyin.txt"); }
        }

        static string UserDataFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "webview2"); }
        }

        public DouyinPlayerWindow()
        {
            Title = "抖音";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(13, 15, 21));
            FontFamily = new FontFamily("Microsoft YaHei UI");

            // 尺寸按屏幕比例算：宽约 22%，高约 72%，居左
            Rect work = SystemParameters.WorkArea;
            double w = Math.Max(320, Math.Min(520, work.Width * 0.22));
            double h = Math.Max(560, Math.Min(900, work.Height * 0.72));
            Width = w;
            Height = h;
            defaultLeft = work.Left + (work.Width - w) / 2;
            defaultTop = work.Top + 8;
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

            var title = new TextBlock
            {
                Text = "抖音",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 218)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                IsHitTestVisible = false
            };
            headerGrid.Children.Add(title);

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
                Margin = new Thickness(0, 0, 6, 0)
            };
            buttons.Children.Add(BuildHeaderButton(false, "收起到后台（跟热键一样）", ToggleVisible));
            buttons.Children.Add(BuildHeaderButton(true, "退出抖音", ExitPlayer));
            headerGrid.Children.Add(buttons);
            header.Child = headerGrid;

            // ---- 内容 ----
            view = new WebView2();
            // 关键：默认背景是白色，页面还没画出来时会闪一下白。改成和窗口一样的深色。
            view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(13, 15, 21);
            statusText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 140, 140)),
                Background = new SolidColorBrush(Color.FromArgb(235, 20, 22, 30)),
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            var body = new Grid();
            body.Children.Add(view);
            body.Children.Add(statusText);

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(body);
            Content = root;

            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                ApplyRoundedCorners();
                InstallMouseHook();
            };
            Loaded += delegate { InitWebView(); };
            Closing += delegate
            {
                ReleaseCursor();
                if (mouseHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(mouseHook);
                    mouseHook = IntPtr.Zero;
                }
            };
        }

        // ------------------------------------------------------------ 顶栏按钮

        FrameworkElement BuildHeaderButton(bool close, string tip, Action click)
        {
            var button = new Border
            {
                Width = 30,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Margin = new Thickness(2, 0, 0, 0)
            };
            var icon = new Grid();
            var line = new Rectangle
            {
                Width = 9,
                Height = 1.4,
                RadiusX = 0.7,
                RadiusY = 0.7,
                Fill = new SolidColorBrush(Color.FromRgb(190, 196, 210)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            if (close)
            {
                line.RenderTransform = new RotateTransform(45);
                line.Width = 11;
                icon.Children.Add(line);
                icon.Children.Add(new Rectangle
                {
                    Width = 11,
                    Height = 1.4,
                    RadiusX = 0.7,
                    RadiusY = 0.7,
                    Fill = new SolidColorBrush(Color.FromRgb(190, 196, 210)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = new RotateTransform(-45),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                });
            }
            else
            {
                icon.Children.Add(line);
            }
            button.Child = icon;
            button.MouseEnter += delegate
            {
                button.Background = new SolidColorBrush(Color.FromArgb(
                    (byte)(close ? 46 : 30),
                    (byte)(close ? 232 : 255),
                    (byte)(close ? 92 : 255),
                    (byte)(close ? 98 : 255)));
            };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate
            {
                if (click != null) click();
            };
            return button;
        }

        // ------------------------------------------------------------ 拖动（限制在屏内）

        void DragDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            dragging = true;
            dragDpi = Dpi();
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            dragCursorX = cursor.X / dragDpi;
            dragCursorY = cursor.Y / dragDpi;
            dragStartLeft = Left;
            dragStartTop = Top;
            ((Border)sender).CaptureMouse();
            e.Handled = true;
        }

        void DragMoveHandler(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(sender); return; }
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            double left = dragStartLeft + (cursor.X / dragDpi - dragCursorX);
            double top = dragStartTop + (cursor.Y / dragDpi - dragCursorY);
            ClampToWorkArea(ref left, ref top);
            Left = left;
            Top = top;
            e.Handled = true;
        }

        void DragUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            EndDrag(sender);
            e.Handled = true;
        }

        void EndDrag(object sender)
        {
            dragging = false;
            Border zone = sender as Border;
            if (zone != null) { try { zone.ReleaseMouseCapture(); } catch (Exception) { } }
            SavePosition();
        }

        void ClampToWorkArea(ref double left, ref double top)
        {
            try
            {
                double dpi = Dpi();
                var probe = new System.Drawing.Point(
                    (int)Math.Round((left + Width / 2) * dpi),
                    (int)Math.Round((top + Height / 2) * dpi));
                System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(probe).WorkingArea;
                double minLeft = work.Left / dpi;
                double minTop = work.Top / dpi;
                double maxLeft = work.Right / dpi - Width;
                double maxTop = work.Bottom / dpi - Height;
                if (maxLeft < minLeft) maxLeft = minLeft;
                if (maxTop < minTop) maxTop = minTop;
                left = Math.Max(minLeft, Math.Min(maxLeft, left));
                top = Math.Max(minTop, Math.Min(maxTop, top));
            }
            catch (Exception) { }
        }

        double Dpi()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source == null ? 1.0 : source.CompositionTarget.TransformToDevice.M11;
        }

        void LoadPosition()
        {
            try
            {
                if (!File.Exists(PositionFile)) return;
                string[] parts = File.ReadAllText(PositionFile).Trim().Split(',');
                if (parts.Length != 2) return;
                double left, top;
                if (!double.TryParse(parts[0], out left) || !double.TryParse(parts[1], out top)) return;
                ClampToWorkArea(ref left, ref top);
                Left = left;
                Top = top;
            }
            catch (Exception) { }
        }

        void SavePosition()
        {
            try
            {
                string folder = Path.GetDirectoryName(PositionFile);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(PositionFile, ((int)Math.Round(Left)) + "," + ((int)Math.Round(Top)));
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ 显示 / 收起 / 退出

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
            GrabMouse();
        }

        public void HidePlayer()
        {
            ReleaseCursor();
            Hide();
        }

        public void HideInstant()
        {
            try { ReleaseCursor(); Hide(); } catch (Exception) { }
        }

        /// <summary>当前窗口在屏幕上的矩形（DIP）。</summary>
        public Rect ScreenRect()
        {
            return new Rect(Left, Top, Width, Height);
        }

        /// <summary>先以"隐形"方式显示：WebView2 能初始化、能加载，但用户看不见。</summary>
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

        /// <summary>退出后重置位置：下次点球回到"面板展开位置"。</summary>
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

        // Win11 原生圆角；Win10 不支持就保持直角（不会崩）
        void ApplyRoundedCorners()
        {
            if (roundedApplied) return;
            roundedApplied = true;
            try
            {
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
            }
            catch (Exception) { }
        }

        void ExitPlayer()
        {
            if (ExitRequested != null)
            {
                // 交给主窗口演"飞回胶囊"
                try { ExitRequested(this); }
                catch (Exception) { }
                return;
            }
            Finish();
        }

        /// <summary>真正销毁。由主窗口在动画结束后调用。</summary>
        public void Finish()
        {
            ReleaseCursor();
            try { if (view != null && view.CoreWebView2 != null) view.Dispose(); }
            catch (Exception) { }
            try { Close(); }
            catch (Exception) { }
            if (UserClosed != null) { try { UserClosed(this, EventArgs.Empty); } catch (Exception) { } }
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
                if (core == null) { ShowStatus("抖音窗口初始化失败：WebView2 没有就绪。"); return; }

                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                // mobile 模式：换成手机 UA，让抖音给移动版布局（更接近"手机刷抖音"）
                string mode = ReadCleanMode();
                DebugLog("init mode=" + mode);
                if (mode == "mobile")
                {
                    try
                    {
                        core.Settings.UserAgent =
                            "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) " +
                            "Chrome/124.0.0.0 Mobile Safari/537.36";
                    }
                    catch (Exception) { }
                }
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
                core.Navigate("https://www.douyin.com/");
            }
            catch (Exception ex)
            {
                ShowStatus("抖音窗口启动失败：" + ex.Message);
            }
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

        // 只做一件最保守的事：把"贴左、竖长、窄"的那条导航栏隐藏掉。
        // 绝不动容器的宽高和位移——之前把容器撑宽导致抖音按"填满"重算，视频就被裁切了。
        //
        // 另外这里会顺手把页面的真实尺寸写进 data\douyin-layout.log，
        // 用来判断"画面偏右"到底是视口问题还是页面自己的布局问题。
        void CleanLayout()
        {
            if (view == null || view.CoreWebView2 == null) return;
            try
            {
                string mode = ReadCleanMode();
                string measure =
                    "(function(){try{var v=document.querySelector('video');var r=v?v.getBoundingClientRect():null;" +
                    "return JSON.stringify({iw:innerWidth,ih:innerHeight,sw:document.body.scrollWidth," +
                    "cw:document.documentElement.clientWidth,z:window.devicePixelRatio," +
                    "vx:r?Math.round(r.left):-1,vy:r?Math.round(r.top):-1,vw:r?Math.round(r.width):-1,vh:r?Math.round(r.height):-1});" +
                    "}catch(e){return '{\"err\":1}';}})()";

                ExecuteAndLog(measure, "measure");

                // 探测：右边缘几个高度上，鼠标下压着的是哪些元素（用来精确定位那条黑条）
                string probe =
                    "(function(){try{var out=[];var ys=[0.15,0.35,0.55,0.75,0.90];" +
                    "for(var k=0;k<ys.length;k++){var y=Math.round(innerHeight*ys[k]);" +
                    "var list=document.elementsFromPoint(innerWidth-6,y)||[];" +
                    "for(var i=0;i<Math.min(3,list.length);i++){var e=list[i];var r=e.getBoundingClientRect();" +
                    "var cls='';try{cls=String(e.className||'').slice(0,36);}catch(x){}" +
                    "out.push(Math.round(r.left)+'/'+Math.round(r.width)+'x'+Math.round(r.height)+' '+(e.tagName||'')+'.'+cls);}}" +
                    "return out.join(' || ');}catch(e){return 'err';}})()";
                ExecuteAndLog(probe, "probe");
                DebugLog("wpf view=" + (int)view.ActualWidth + "x" + (int)view.ActualHeight
                    + " win=" + (int)Width + "x" + (int)Height + " dpi=" + Dpi().ToString("0.00")
                    + " mode=" + mode);

                if (mode == "off") return;
                if (mode == "zoom") AutoZoom(0);
                if (mode == "tight")
                {
                    // 布局宽 580 = 左边留白 72 + 视频 448 + 右边留白 60。
                    // 视频只有 77% 宽，右侧那 60px 就是"黑条"。先把左边留白清掉，
                    // 让布局瘦到 ~508，再重新缩放，视频+右侧图标列就能占满窗口。
                    ExecuteAndLog(
                        "(function(){try{var v=document.querySelector('video');if(!v)return 'no-video';" +
                        "var n=0,el=v.parentElement;while(el&&el!==document.body){" +
                        "var cs=getComputedStyle(el);" +
                        "if(parseFloat(cs.paddingLeft)>0){el.style.paddingLeft='0px';n++;}" +
                        "if(parseFloat(cs.marginLeft)>0){el.style.marginLeft='0px';n++;}" +
                        "el=el.parentElement;}return 'tight '+n;}catch(e){return 'err '+e.message;}})()", "tight");

                    var zoomLater = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    zoomLater.Tick += delegate
                    {
                        zoomLater.Stop();
                        AutoZoom(0);
                    };
                    zoomLater.Start();
                }

                string js =
                    "(function(){try{" +
                    // ① 滚动条宽度归零（不影响滚动，只是不显示）
                    "if(!document.getElementById('dsh-css')){var st=document.createElement('style');st.id='dsh-css';" +
                    "st.textContent='::-webkit-scrollbar{width:0!important;height:0!important}'+" +
                    "'html,body{scrollbar-width:none!important;-ms-overflow-style:none!important}'+" +
                    // 播放器右侧那条「上一条/下一条」黑条（日志实测：div.xgplayer-playswitch 36x80，距右边 11px）
                    "'.xgplayer-playswitch,[class*=playswitch]{display:none!important}';" +
                    "document.head.appendChild(st);}" +
                    // ② 隐藏左侧那条竖长导航
                    "var nav=0;var els=document.querySelectorAll('div,aside,nav');" +
                    "for(var i=0;i<els.length;i++){var r=els[i].getBoundingClientRect();" +
                    "if(r.height>innerHeight*0.45&&r.width>24&&r.width<180&&r.left<50){" +
                    "els[i].style.display='none';nav++;}}" +
                    // ③ 隐藏贴右边缘的窄条（上下翻页键所在的那条黑边），点赞那列宽约 50 不受影响
                    "var right=[];var els2=document.querySelectorAll('div,span');" +
                    "for(var j=0;j<els2.length;j++){var q=els2[j].getBoundingClientRect();" +
                    "if(q.width>0&&q.width<=30&&q.height>40&&q.height<innerHeight*0.95&&(innerWidth-q.right)<=8){" +
                    "var cs=getComputedStyle(els2[j]);if(cs.position!=='static'){" +
                    "els2[j].style.display='none';right.push(Math.round(q.width)+'x'+Math.round(q.height)+'@'+Math.round(q.top));}}}" +
                    "return 'nav='+nav+' right='+right.join('|');}catch(e){return 'err '+e.message;}})()";
                ExecuteAndLog(js, "clean");

                // 缩放稳定后再探测一次：右边缘 40px 内所有窄元素全列出来，不做任何筛选
                var late = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                late.Tick += delegate
                {
                    late.Stop();
                    string dump =
                        "(function(){try{var out=[];var iw=innerWidth,ih=innerHeight;" +
                        "var els=document.querySelectorAll('div,span,section,aside');" +
                        "for(var j=0;j<els.length;j++){var q=els[j].getBoundingClientRect();" +
                        "if(q.width<=0||q.width>200||q.height<12)continue;" +
                        "var gap=Math.round(iw-q.right);if(gap<-2||gap>40)continue;" +
                        "var cls='';try{cls=String(els[j].className||'').slice(0,28);}catch(x){}" +
                        "out.push('t'+Math.round(q.top)+' h'+Math.round(q.height)+' w'+Math.round(q.width)+' gap'+gap+' '+(els[j].tagName||'')+'.'+cls);" +
                        "if(out.length>=18)break;}" +
                        "return 'iw'+iw+' :: '+out.join(' || ');}catch(e){return 'err '+e.message;}})()";
                    ExecuteAndLog(dump, "dump");

                    // 播上一条/下一条的按钮已经隐藏，但它的容器还在画黑底：
                    // 从 .xgplayer-playswitch 往上找第一个"有背景色、宽度≤80"的祖先，把它也藏了。
                    string strip =
                        "(function(){try{var sw=document.querySelector('.xgplayer-playswitch');if(!sw)return 'no-switch';" +
                        "var out=[];var el=sw.parentElement;var d=0;" +
                        "while(el&&d<6){var cs=getComputedStyle(el);var r=el.getBoundingClientRect();" +
                        "var cls=String(el.className||'').slice(0,22);" +
                        "out.push(cls+' w'+Math.round(r.width)+' bg'+cs.backgroundColor);" +
                        "var opaque=cs.backgroundColor&&cs.backgroundColor!=='rgba(0, 0, 0, 0)'&&cs.backgroundColor!=='transparent';" +
                        "if(opaque&&r.width<=80&&r.height>20){el.style.display='none';out.push('HID');return out.join(' | ');}" +
                        "el=el.parentElement;d++;}" +
                        "return 'none '+out.join(' | ');}catch(e){return 'err '+e.message;}})()";
                    ExecuteAndLog(strip, "strip");
                };
                late.Start();
            }
            catch (Exception) { }
        }

        // zoom 模式：抖音桌面版有约 580px 的最小布局宽度，窄窗装不下，
        // 所以按 scrollWidth/innerWidth 把整页等比缩小，视频就完整了。
        async void AutoZoom(int depth)
        {
            if (depth > 3) return;
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                string raw = await view.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var s=document.body.scrollWidth,i=innerWidth;var r=(s>i+4)?(i/s):1;return r.toFixed(3);})()");
                double ratio;
                if (!double.TryParse(raw.Trim('"'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out ratio)) return;
                if (ratio >= 0.999) return;
                double next = Math.Max(0.45, Math.Min(1.0, view.ZoomFactor * ratio));
                if (Math.Abs(next - view.ZoomFactor) < 0.01) return;
                view.ZoomFactor = next;
                DebugLog("zoom -> " + next.ToString("0.000") + " ratio=" + ratio.ToString("0.000"));
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    AutoZoom(depth + 1);
                };
                timer.Start();
            }
            catch (Exception) { }
        }

        async void ExecuteAndLog(string script, string tag)
        {
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                string result = await view.CoreWebView2.ExecuteScriptAsync(script);
                DebugLog(tag + " -> " + result);
            }
            catch (Exception) { }
        }

        static string ReadCleanMode()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "douyin-clean.txt");
                if (!File.Exists(path)) return "zoom";
                string text = File.ReadAllText(path).Trim().ToLowerInvariant();
                return string.IsNullOrEmpty(text) ? "nav" : text;
            }
            catch (Exception) { return "nav"; }
        }

        internal static void DebugLog(string message)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "douyin-layout.log");
                if (File.Exists(path) && new FileInfo(path).Length > 200 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ 滚轮转发

        void InstallMouseHook()
        {
            try
            {
                mouseProc = MouseProc;
                mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
            }
            catch (Exception) { }
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && IsVisible && !cloaked && wParam.ToInt32() == WM_MOUSEWHEEL)
                {
                    MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (IsInsideWindow(data.pt))
                    {
                        short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                        ForwardWheel(data.pt, delta);
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception) { }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        bool IsInsideWindow(POINT pt)
        {
            double dpi = Dpi();
            int left = (int)Math.Round(Left * dpi);
            int top = (int)Math.Round(Top * dpi);
            int right = (int)Math.Round((Left + Width) * dpi);
            int bottom = (int)Math.Round((Top + Height) * dpi);
            return pt.x >= left && pt.x <= right && pt.y >= top && pt.y <= bottom;
        }

        void ForwardWheel(POINT pt, int delta)
        {
            IntPtr target = WindowFromPoint(pt);
            if (target == IntPtr.Zero || target == handle) target = FindWebViewHwnd();
            if (target == IntPtr.Zero || target == handle) return;
            int wparam = (delta & 0xFFFF) << 16;
            int lparam = ((pt.y & 0xFFFF) << 16) | (pt.x & 0xFFFF);
            PostMessage(target, WM_MOUSEWHEEL, new IntPtr(wparam), new IntPtr(lparam));
        }

        IntPtr FindWebViewHwnd()
        {
            if (childHwnd != IntPtr.Zero) return childHwnd;
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(handle, delegate(IntPtr h, IntPtr param)
            {
                var name = new System.Text.StringBuilder(256);
                GetClassName(h, name, 256);
                if (name.ToString().IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            childHwnd = found;
            return found;
        }

        // 抢回光标：游戏把光标藏起来/锁住时，让鼠标能用
        void GrabMouse()
        {
            try
            {
                ClipCursor(IntPtr.Zero);
                for (int i = 0; i < 3; i++) { ShowCursor(true); cursorShown++; }
                double dpi = Dpi();
                SetCursorPos(
                    (int)Math.Round((Left + Width / 2) * dpi),
                    (int)Math.Round((Top + Height * 0.45) * dpi));
            }
            catch (Exception) { }
        }

        void ReleaseCursor()
        {
            try
            {
                while (cursorShown > 0) { ShowCursor(false); cursorShown--; }
            }
            catch (Exception) { }
        }
    }
}
