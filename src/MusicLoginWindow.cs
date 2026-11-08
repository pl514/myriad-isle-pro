using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Path = System.IO.Path;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 音乐登录窗：一个独立的小窗，里面是 WebView2 打开平台的官方网页。
    //
    // 为什么不用胶囊面板里的网页：胶囊窗口是分层透明窗口（AllowsTransparency），
    // WebView2 是子 HWND，塞进去一点画面都不会有——抖音窗、AI 网页版都是这么绕开的。
    //
    // 职责边界（第一步只做这么多）：
    //   · 打开官方登录页，让用户自己扫码 / 输账号密码（不碰验证码、不代登录）；
    //   · 定时看 cookie，命中登录判据就自动关窗并回调；
    //   · 「我已登录」是兜底：判据没命中时用户可以手动确认。
    //
    // cookie 由 WebView2 存在 data\music\webview2-<平台Id>\ 里，下次打开直接是登录态。
    // cookie 只在进程内用来判断"登没登录"，不外发、不落日志。
    // ---------------------------------------------------------------------
    internal sealed class MusicLoginWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // 真实鼠标点击：抖音系页面会检查事件 isTrusted，脚本合成的点击一律忽略，
        // 只能让系统替我们点一下（点完把光标挪回去，别抢用户的鼠标）。
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
        const uint MouseLeftDown = 0x0002;
        const uint MouseLeftUp = 0x0004;
        const double HeaderHeight = 36;

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint { public int X; public int Y; }

        readonly MusicPlatform platform;
        readonly WebView2 view;
        readonly TextBlock statusText;
        readonly DispatcherTimer poll;
        bool finished;
        bool roundedApplied;

        /// <summary>登录成功（cookie 命中或用户点了「我已登录」）。参数是被登录的平台。</summary>
        internal event Action<MusicPlatform> LoggedIn;

        /// <summary>窗口关掉（不管登没登上），主窗口据此清掉引用。</summary>
        internal event Action Dismissed;

        internal MusicLoginWindow(MusicPlatform platform)
        {
            this.platform = platform;

            Title = "登录 " + platform.Name;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 这几个平台的网页版都是桌面布局（扫码框常在页面中右侧），窗口太窄会把
            // 扫码区域挤出可视范围。QQ 音乐走的是独立的授权卡，所以给它一个窄一点的窗口。
            Width = platform.LoginWidth > 0 ? platform.LoginWidth : 900;
            Height = platform.LoginHeight > 0 ? platform.LoginHeight : 800;
            Background = new SolidColorBrush(Color.FromRgb(19, 22, 28));
            FontFamily = new FontFamily("Microsoft YaHei UI");

            Rect work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + Math.Max(20, (work.Height - Height) / 2 - 40);

            // ---------------------------------------------------------- 顶栏
            var header = new Border
            {
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(26, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 47, 58)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var headerGrid = new Grid();
            var dragZone = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            dragZone.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                try { DragMove(); } catch (Exception) { }
            };
            headerGrid.Children.Add(dragZone);

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                IsHitTestVisible = false
            };
            titleRow.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(platform.ColorFrom, platform.ColorTo, new Point(0, 0), new Point(1, 1)),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "登录 " + platform.Name,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(206, 212, 224)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            headerGrid.Children.Add(titleRow);

            var close = BuildHeadButton("×", "关闭（不登录）");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 0, 8, 0);
            headerGrid.Children.Add(close);
            header.Child = headerGrid;

            // ---------------------------------------------------------- 网页
            view = new WebView2();
            // 页面还没画出来时默认是白底，闪一下很扎眼，改成和窗口一样的深色
            view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(19, 22, 28);
            var body = new Grid();
            body.Children.Add(view);

            // ---------------------------------------------------------- 底栏
            var footer = new Border
            {
                Height = 48,
                Background = new SolidColorBrush(Color.FromRgb(26, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 47, 58)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 0, 10, 0)
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            statusText = new TextBlock
            {
                Text = "正在打开登录页…",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(138, 146, 162)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0)
            };
            Grid.SetColumn(statusText, 0);
            footerGrid.Children.Add(statusText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var browser = BuildFootButton("在浏览器打开", false);
            browser.Click += delegate { OpenInBrowser(); };
            buttons.Children.Add(browser);
            var confirm = BuildFootButton("我已登录", true);
            confirm.Click += delegate { FinishLoggedIn(true); };
            buttons.Children.Add(confirm);
            Grid.SetColumn(buttons, 1);
            footerGrid.Children.Add(buttons);

            footer.Child = footerGrid;

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(header);
            root.Children.Add(footer);
            root.Children.Add(body);
            Content = root;

            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            poll.Tick += delegate { PollCookies(); };

            SourceInitialized += delegate { ApplyRoundedCorners(); };
            Loaded += delegate { InitWebView(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        // ------------------------------------------------------------ 顶栏 / 底栏

        Border BuildHeadButton(string glyph, string tip)
        {
            var button = new Border
            {
                Width = 28,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                VerticalAlignment = VerticalAlignment.Center
            };
            var text = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 210)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            button.Child = text;
            button.MouseEnter += delegate { button.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate { Close(); };
            return button;
        }

        Button BuildFootButton(string text, bool primary)
        {
            var button = new Button
            {
                Content = text,
                Height = 26,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(206, 212, 224)),
                Background = primary
                    ? (Brush)new LinearGradientBrush(Color.FromRgb(52, 211, 153), Color.FromRgb(20, 150, 220), new Point(0, 0), new Point(1, 0))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(primary ? (byte)0 : (byte)34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' " +
                "BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='13'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter Property='Opacity' Value='0.86'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            return button;
        }

        // ------------------------------------------------------------ WebView2

        async void InitWebView()
        {
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music", "webview2-" + platform.Id);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, folder);
                await view.EnsureCoreWebView2Async(environment);
                CoreWebView2 core = view.CoreWebView2;
                if (core == null)
                {
                    statusText.Text = "网页控件没有就绪，可以点「在浏览器打开」登录。";
                    return;
                }

                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                if (platform.Zoom > 0)
                {
                    try { view.ZoomFactor = platform.Zoom; } catch (Exception) { }
                }

                // 登录页里点「用 QQ 登录 / 用抖音登录」会开新窗口：一律在本窗口里跳，
                // 否则新弹出的窗口不受我们管理，关窗时会留下孤儿进程。
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                    try { core.Navigate(args.Uri); } catch (Exception) { }
                };

                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (!args.IsSuccess)
                    {
                        statusText.Text = "页面加载失败（" + args.WebErrorStatus + "），检查网络或点「在浏览器打开」。";
                        return;
                    }
                    statusText.Text = "在网页里完成登录，成功后会自动回到面板。";
                    PollCookies();   // 有些平台是跳转回来就带 cookie，先查一次再等定时器
                    AutoOpenLogin();
                };

                // 清掉这个平台之前留下的登录态：否则换账号时窗口一开就命中旧 cookie，
                // 立刻被判成"已登录"自动关窗，二维码根本出不来。
                // 这个 WebView2 版本没有 DeleteAllCookiesAsync，只能逐个删。
                int cleared = 0;
                try
                {
                    System.Collections.Generic.List<CoreWebView2Cookie> stale =
                        await core.CookieManager.GetCookiesAsync(platform.CookieUri);
                    if (stale != null)
                    {
                        for (int i = 0; i < stale.Count; i++)
                        {
                            try
                            {
                                core.CookieManager.DeleteCookie(stale[i]);
                                cleared++;
                            }
                            catch (Exception) { }
                        }
                    }
                }
                catch (Exception) { }
                Log("cleared cookies before login [" + platform.Id + "]: " + cleared);

                core.Navigate(platform.LoginUrl);
                poll.Start();
            }
            catch (Exception exception)
            {
                statusText.Text = "网页控件启动失败：" + exception.Message;
            }
        }

        void PollCookies()
        {
            if (finished || view == null || view.CoreWebView2 == null) return;
            CheckCookiesAsync();
        }

        // ------------------------------------------------------------ 裁页面（把登录页做成"登录卡片"）

        // 平台网页版的首页都有一堆导航/推荐位，直接嵌进来像浏览器。
        // 这里做两件事：1）自动把「登录」点了，让二维码弹出来；2）注入 CSS 把外壳藏掉。
        // 全程走平台自己的登录流程，不碰它的协议，所以接口怎么变都不受影响。
        async void AutoOpenLogin()
        {
            if (finished || view == null || view.CoreWebView2 == null) return;
            try
            {
                string clicked = await view.CoreWebView2.ExecuteScriptAsync(ClickLoginScript);
                Log("auto-login synthetic [" + platform.Id + "]: " + clicked);
                await Task.Delay(1400);
                if (finished) return;

                string probe = await view.CoreWebView2.ExecuteScriptAsync(ProbeScript);
                if (!HasOverlay(probe))
                {
                    // 合成点击没被认（isTrusted=false）：按按钮位置补一次真实点击
                    string rect = await view.CoreWebView2.ExecuteScriptAsync(FindLoginRectScript);
                    Log("auto-login rect [" + platform.Id + "]: " + rect);
                    if (RealClick(rect))
                    {
                        await Task.Delay(1800);
                        if (finished) return;
                        probe = await view.CoreWebView2.ExecuteScriptAsync(ProbeScript);
                    }
                }
                DumpProbe(probe);

                await view.CoreWebView2.ExecuteScriptAsync(TrimScript);
            }
            catch (Exception exception)
            {
                Log("auto-login failed [" + platform.Id + "]: " + exception.Message);
            }
        }

        static bool HasOverlay(string probeJson)
        {
            // 探针里出现 "Z<z-index> ..." 就说明有浮层弹出来了
            if (string.IsNullOrEmpty(probeJson)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(probeJson, "Z\\d+\\s");
        }

        // 在按钮的屏幕坐标上发一次真实左键点击（点完把光标放回原处）
        bool RealClick(string rectJson)
        {
            double x = 0, y = 0, w = 0, h = 0;
            if (!TryReadNumber(rectJson, "\"x\":", ref x)) return false;
            if (!TryReadNumber(rectJson, "\"y\":", ref y)) return false;
            if (!TryReadNumber(rectJson, "\"w\":", ref w)) return false;
            if (!TryReadNumber(rectJson, "\"h\":", ref h)) return false;
            if (w <= 0 || h <= 0) return false;

            try
            {
                double zoom = 1.0;
                try { zoom = view.ZoomFactor; } catch (Exception) { }
                DpiScale scale = VisualTreeHelper.GetDpi(this);
                double pixelX = (Left + (x + w / 2) * zoom) * scale.DpiScaleX;
                double pixelY = (Top + HeaderHeight + (y + h / 2) * zoom) * scale.DpiScaleY;

                NativePoint before;
                GetCursorPos(out before);
                SetCursorPos((int)Math.Round(pixelX), (int)Math.Round(pixelY));
                System.Threading.Thread.Sleep(80);
                mouse_event(MouseLeftDown, 0, 0, 0, IntPtr.Zero);
                System.Threading.Thread.Sleep(40);
                mouse_event(MouseLeftUp, 0, 0, 0, IntPtr.Zero);
                System.Threading.Thread.Sleep(120);
                SetCursorPos(before.X, before.Y);
                Log("auto-login real click [" + platform.Id + "]: " + (int)pixelX + "," + (int)pixelY);
                return true;
            }
            catch (Exception exception)
            {
                Log("real click failed: " + exception.Message);
                return false;
            }
        }

        static bool TryReadNumber(string json, string marker, ref double value)
        {
            if (string.IsNullOrEmpty(json)) return false;
            int at = json.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return false;
            int i = at + marker.Length;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-' || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+')) i++;
            if (i <= start) return false;
            return double.TryParse(json.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        void DumpProbe(string json)
        {
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "login-dom-" + platform.Id + ".txt");
                File.WriteAllText(path, Unquote(json), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static string Unquote(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string text = json;
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"') text = text.Substring(1, text.Length - 2);
            return text.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        static void Log(string message)
        {
            try { DragNative.Log(message); } catch (Exception) { }
        }

        // 找页面上带「登录」字样的最深元素（"登录"/"登录注册"/"登录/注册" 都能命中），
        // 并用真实的鼠标事件去点——有些页面只监听 mousedown/mouseup，不认 .click()
        const string ClickLoginScript =
            "(function(){" +
            "var cands=[];var els=document.querySelectorAll('a,div,span,button,li,i');" +
            "for(var i=0;i<els.length;i++){var e=els[i];" +
            "if(!e.offsetWidth||!e.offsetHeight)continue;" +
            "var t=((e.textContent||'').trim());" +
            "if(t.indexOf('登录')<0)continue;if(t.length>8)continue;" +
            "var d=0,p=e;while(p&&p!==document.body){d++;p=p.parentNode;}" +
            "cands.push({e:e,d:d,t:t});}" +
            "if(!cands.length)return 'not-found';" +
            "cands.sort(function(a,b){return b.d-a.d;});" +
            "var el=cands[0].e;" +
            "try{var r=el.getBoundingClientRect();var x=r.left+r.width/2,y=r.top+r.height/2;" +
            "var o={bubbles:true,cancelable:true,clientX:x,clientY:y,view:window};" +
            "el.dispatchEvent(new MouseEvent('mousedown',o));" +
            "el.dispatchEvent(new MouseEvent('mouseup',o));" +
            "el.dispatchEvent(new MouseEvent('click',o));}" +
            "catch(err){try{el.click();}catch(e2){}}" +
            "return 'clicked:'+el.tagName+'.'+(el.className||'')+':'+cands[0].t;})()";

        // 找「登录」按钮的视口坐标（真实点击要用），找不到返回 null
        const string FindLoginRectScript =
            "(function(){" +
            "var best=null,bestDepth=-1;" +
            "var els=document.querySelectorAll('a,div,span,button,li,i');" +
            "for(var i=0;i<els.length;i++){var e=els[i];" +
            "if(!e.offsetWidth||!e.offsetHeight)continue;" +
            "var t=((e.textContent||'').trim());" +
            "if(t.indexOf('登录')<0)continue;if(t.length>8)continue;" +
            "var d=0,p=e;while(p&&p!==document.body){d++;p=p.parentNode;}" +
            "if(d>bestDepth){best=e;bestDepth=d;}}" +
            "if(!best)return 'null';" +
            "var r=best.getBoundingClientRect();" +
            "return '{\"x\":'+r.left+',\"y\":'+r.top+',\"w\":'+r.width+',\"h\":'+r.height+'}';})()";

        // 把弹窗结构 dump 出来，方便照着写裁剪规则（写到 data\music\login-dom-<平台>.txt）
        const string ProbeScript =
            "(function(){" +
            "function cls(e){var c=e.className;return (typeof c==='string')?c:(e.id||'');}" +
            "var out=[];out.push('URL '+location.href);" +
            "var all=document.querySelectorAll('*');var fixed=[];" +
            "for(var i=0;i<all.length;i++){var e=all[i];" +
            "if(!e.offsetWidth||!e.offsetHeight)continue;" +
            "var cs=getComputedStyle(e);" +
            "if(cs.position!=='fixed'&&cs.position!=='absolute')continue;" +
            "var z=parseInt(cs.zIndex||'0',10);if(!z||z<100)continue;" +
            "fixed.push({z:z,tag:e.tagName,cls:cls(e),w:e.offsetWidth,h:e.offsetHeight," +
            "t:(e.textContent||'').replace(/\\s+/g,' ').substring(0,30)});}" +
            "fixed.sort(function(a,b){return b.z-a.z;});" +
            "for(var j=0;j<Math.min(fixed.length,10);j++){var f=fixed[j];" +
            "out.push('Z'+f.z+' '+f.tag+'.'+f.cls+' '+f.w+'x'+f.h+' | '+f.t);}" +
            "var bk=document.body.children;" +
            "for(var k=0;k<bk.length;k++){out.push('BODY '+bk[k].tagName+'.'+cls(bk[k])+' '+(bk[k].offsetWidth||0)+'x'+(bk[k].offsetHeight||0));}" +
            "var fms=document.querySelectorAll('iframe');" +
            "for(var m=0;m<fms.length;m++){out.push('IFRAME '+fms[m].offsetWidth+'x'+fms[m].offsetHeight+' '+(fms[m].src||''));}" +
            "return out.join('\\n');})()";

        // 注入的样式与裁剪：只留登录弹窗，其余页面内容全部隐藏，顺便把卡片放大一点。
        // 选择器来自探针（data\music\login-dom-<平台>.txt），QQ 音乐是 .yqq-dialog-wrap / .yqq-dialog-mask。
        const string TrimScript =
            "(function(){" +
            "if(!document.getElementById('capsule-music-trim')){" +
            "var css='html,body{overflow:hidden !important}" +
            "::-webkit-scrollbar{display:none !important;width:0 !important;height:0 !important}';" +
            "var s=document.createElement('style');s.id='capsule-music-trim';s.textContent=css;" +
            "(document.head||document.documentElement).appendChild(s);}" +
            "var wrap=document.querySelector('.yqq-dialog-wrap');" +
            "if(!wrap)return 'styled-only';" +
            // 找到 body 下包着弹窗的那一层，其余兄弟节点全部藏掉（脚本除外）
            "var node=wrap;while(node.parentElement&&node.parentElement!==document.body){node=node.parentElement;}" +
            "var kids=document.body.children;" +
            "for(var i=0;i<kids.length;i++){var el=kids[i];" +
            "if(el===node)continue;if(el.tagName==='SCRIPT')continue;el.style.display='none';}" +
            "var mask=document.querySelector('.yqq-dialog-mask');" +
            "if(mask){mask.style.position='fixed';mask.style.left='0';mask.style.top='0';" +
            "mask.style.width='100%';mask.style.height='100%';mask.style.background='#0e1014';" +
            "mask.style.opacity='1';}" +
            "node.style.position='fixed';node.style.left='0';node.style.top='0';" +
            "node.style.width='100%';node.style.height='100%';node.style.background='#0e1014';" +
            "node.style.overflow='hidden';" +
            "wrap.style.width='100%';wrap.style.height='100%';" +
            // 卡片整体放大：里面的二维码是跨域 iframe，只能靠缩放父节点把它放大
            "var card=wrap.querySelector('.yqq-dialog')||wrap.firstElementChild||wrap;" +
            "card.style.position='fixed';card.style.left='50%';card.style.top='50%';" +
            "card.style.margin='0';card.style.borderRadius='16px';" +
            "card.style.boxShadow='0 24px 60px rgba(0,0,0,.55)';" +
            "card.style.transform='translate(-50%,-50%) scale(1.55)';" +
            "card.style.transformOrigin='center center';" +
            "wrap.style.overflow='hidden';wrap.style.background='#0e1014';" +
            "return 'trimmed';})()";

        async void CheckCookiesAsync()
        {
            if (finished || view == null || view.CoreWebView2 == null) return;
            try
            {
                System.Collections.Generic.List<CoreWebView2Cookie> cookies =
                    await view.CoreWebView2.CookieManager.GetCookiesAsync(platform.CookieUri);
                if (cookies == null) return;

                // 命中登录判据时，把所有 cookie 存一份给 C# 侧的接口用（网易云/QQ 都是这么读的）
                bool hit = false;
                var dump = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cookies.Count; i++)
                {
                    CoreWebView2Cookie cookie = cookies[i];
                    if (cookie == null || string.IsNullOrEmpty(cookie.Value)) continue;
                    dump[cookie.Name] = cookie.Value;
                    for (int j = 0; j < platform.CookieNames.Length; j++)
                    {
                        if (string.Equals(cookie.Name, platform.CookieNames[j], StringComparison.OrdinalIgnoreCase)) hit = true;
                    }
                }
                if (!hit) return;
                try { MusicCookieStore.Save(platform.Id, dump); } catch (Exception) { }
                FinishLoggedIn(false);
            }
            catch (Exception) { }
        }

        void FinishLoggedIn(bool manual)
        {
            if (finished) return;
            finished = true;
            try { poll.Stop(); } catch (Exception) { }
            if (statusText != null) statusText.Text = manual ? "已确认登录，正在返回面板…" : "检测到登录成功，正在返回面板…";

            if (LoggedIn != null)
            {
                try { LoggedIn(platform); } catch (Exception) { }
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { Close(); } catch (Exception) { }
            }), DispatcherPriority.Background);
        }

        void OpenInBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo(platform.LoginUrl) { UseShellExecute = true });
                if (statusText != null) statusText.Text = "已用浏览器打开。浏览器和这里不共享登录态，那边登录完请回来点「我已登录」。";
            }
            catch (Exception exception)
            {
                if (statusText != null) statusText.Text = "打开浏览器失败：" + exception.Message;
            }
        }

        void ApplyRoundedCorners()
        {
            if (roundedApplied) return;
            roundedApplied = true;
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
            }
            catch (Exception) { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (poll != null) poll.Stop(); } catch (Exception) { }
            try { if (view != null && view.CoreWebView2 != null) view.Dispose(); } catch (Exception) { }
            base.OnClosed(e);
            if (Dismissed != null)
            {
                try { Dismissed(); } catch (Exception) { }
            }
        }
    }
}
