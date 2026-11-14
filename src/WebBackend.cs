using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // W4：网页版驱动（实验功能，默认关闭）
    //
    // 一个**看不见**的 WebView2 窗口：平时挪到屏幕外 + DWM cloak，用户完全看不到；
    // 需要登录（或登录过期）时才临时揭盖让用户自己扫码。
    //
    // 安全红线（见 docs/AI网页驱动-交接包.md §7）：
    //   · 只用用户自己的登录态，不导出 cookie、不碰验证码；
    //   · 串行 + 节流（两次发送至少隔 3 秒）；
    //   · 出错一律抛"人话"异常 → 前端错误气泡 + 一键切回 API。
    // ---------------------------------------------------------------------
    internal sealed class WebSiteConfig
    {
        internal string Id;
        internal string Kind = "ds";      // ds = textarea + 固定回答选择器；db = contenteditable + 消息列表
        internal string Url;
        internal string InputSelector;
        internal string AnswerSelector;   // 回答块（取最后一个 = 最新）
        internal string LoginHint;        // 页面上出现这些字 → 认为没登录
    }

    internal sealed class WebBackend : IChatBackend
    {
        // 全进程共用一个隐藏窗口（一个站点一个，按需创建）
        static readonly System.Collections.Generic.Dictionary<string, WebChatWindow> Windows =
            new System.Collections.Generic.Dictionary<string, WebChatWindow>(StringComparer.OrdinalIgnoreCase);
        static readonly System.Collections.Generic.Dictionary<string, DateTime> LastSendAt =
            new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();

        readonly WebSiteConfig site;

        internal WebBackend(WebSiteConfig site)
        {
            this.site = site;
        }

        // 要随这条消息一起发的图片（由调用方在 SendAsync 之前塞进来）
        string[] pendingFiles;

        // 胶囊会话 ↔ 网页会话的绑定（由调用方在 SendAsync 之前塞进来）
        string pendingThreadUrl;
        bool pendingNewChat;
        Action<string> onThreadDiscovered;

        internal void SetAttachments(string[] paths)
        {
            pendingFiles = paths;
        }

        // url 为空 = 这个胶囊会话还没绑定网页会话，这次要开一条新的
        internal void SetThread(string url, bool newChat, Action<string> onDiscovered)
        {
            pendingThreadUrl = url;
            pendingNewChat = newChat;
            onThreadDiscovered = onDiscovered;
        }

        // 给上层用：按站点拿共享窗口，去归档某条网页会话
        internal static async Task<bool> TryArchiveAsync(string siteId, string threadUrl)
        {
            if (string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(threadUrl)) return false;
            WebChatWindow window;
            lock (Gate)
            {
                if (!Windows.TryGetValue(siteId, out window) || window == null) return false;
            }
            return await window.ArchiveThreadAsync(threadUrl);
        }
        // 网页版没有 system 提示可注入：这里收下但不使用（skill 只在 API 通道生效）
        public string SystemPrompt { get; set; }

        // 网页版走自己的 SetAttachments 上传，不用这个字段
        public string[] ImagePaths { get; set; }

        public async Task SendAsync(string text, Action<string> onDelta, CancellationToken token)
        {
            if (onDelta == null || site == null) return;

            // 节流：两次发送至少隔 3 秒（比真人还稳，降低风控概率）
            lock (Gate)
            {
                DateTime last;
                if (LastSendAt.TryGetValue(site.Id, out last))
                {
                    double wait = 3.0 - (DateTime.UtcNow - last).TotalSeconds;
                    if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
                }
                LastSendAt[site.Id] = DateTime.UtcNow;
            }

            WebChatWindow window;
            lock (Gate)
            {
                if (!Windows.TryGetValue(site.Id, out window) || window == null)
                {
                    window = new WebChatWindow(site);
                    Windows[site.Id] = window;
                }
            }

            // C# 5 不允许在 catch 里 await，所以先记下来再处理
            OperationCanceledException cancelled = null;
            try
            {
                if (pendingFiles != null)
                {
                    window.SetAttachments(pendingFiles);
                    pendingFiles = null;
                }
                window.SetThread(pendingThreadUrl, pendingNewChat);
                pendingThreadUrl = null;
                pendingNewChat = false;

                await window.AskAsync(text, onDelta, token);

                // 把这次落到的网页会话 URL 报回去，让上层记进 webthreads.txt
                if (onThreadDiscovered != null && !string.IsNullOrEmpty(window.DiscoveredUrl))
                {
                    onThreadDiscovered(window.DiscoveredUrl);
                }
            }
            catch (OperationCanceledException exception)
            {
                cancelled = exception;
            }
            if (cancelled != null)
            {
                // 用户按了停止：把网页那边的生成也停掉，别让它继续跑
                await window.StopPageAsync();
                throw cancelled;
            }
        }
    }

    internal sealed class WebChatWindow
    {
        const int WM_CLOAK = 13;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        readonly WebSiteConfig site;
        Window window;
        WebView2 view;
        bool ready;

        internal WebChatWindow(WebSiteConfig site)
        {
            this.site = site;
        }

        async Task<bool> EnsureReadyAsync(CancellationToken token)
        {
            if (ready && view != null && view.CoreWebView2 != null) return true;

            if (window == null)
            {
                window = new Window
                {
                    Width = 900,
                    Height = 700,
                    Left = -4000,
                    Top = -4000,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = false,
                    Background = System.Windows.Media.Brushes.White
                };
                view = new WebView2();
                window.Content = view;
                window.Show();          // 必须 Show，否则 WebView2 不加载
                Cloak(true);
            }

            if (view.CoreWebView2 == null)
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "webview2-ai", site.Id);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, folder);
                await view.EnsureCoreWebView2Async(env);
                view.CoreWebView2.Navigate(site.Url);
                await WaitDocumentReadyAsync(token);
            }

            ready = true;
            return true;
        }

        async Task WaitDocumentReadyAsync(CancellationToken token)
        {
            for (int i = 0; i < 100 && !token.IsCancellationRequested; i++)
            {
                await Task.Delay(200);
                try
                {
                    string state = await RunScript("document.readyState");
                    if (state == "complete") return;
                }
                catch (Exception) { }
            }
        }

        // ---------------------------------------------------------- 一问一答

        // 归档网页端那条会话（删除胶囊会话时调用）。
        // 只按"链接 href 完全等于这条会话的 URL"来找，不靠标题猜，避免归档错人的记录。
        internal async Task<bool> ArchiveThreadAsync(string threadUrl)
        {
            if (string.IsNullOrEmpty(threadUrl) || view == null || view.CoreWebView2 == null) return false;
            try
            {
                // 归档按钮在左侧历史列表里，先回站点根
                string root = site.Url;
                if (!string.Equals(view.CoreWebView2.Source, root, StringComparison.OrdinalIgnoreCase))
                {
                    view.CoreWebView2.Navigate(root);
                    await WaitDocumentReadyAsync(CancellationToken.None);
                    await Task.Delay(2500);
                }

                string js =
                    "(function(){" +
                    "var target='" + threadUrl.Replace("'", "\\'") + "';" +
                    "var links=document.querySelectorAll('a[href]');" +
                    "for(var i=0;i<links.length;i++){" +
                    "if(links[i].href!==target)continue;" +
                    "var node=links[i];" +
                    "for(var up=0;up<6&&node;up++){" +
                    "var btn=node.querySelector('[aria-label=\"归档对话\"],[aria-label=\"归档\"],[title=\"归档对话\"]');" +
                    "if(btn){btn.click();return 'archived';}" +
                    "node=node.parentElement;}}" +
                    "return 'not-found';})()";
                string result = await RunScript(js);
                Log("archive thread: " + result);
                return string.Equals(result, "archived", StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                Log("archive failed: " + exception.Message);
                return false;
            }
        }


        // 胶囊会话对应的网页会话 URL（空 = 还没绑定）
        internal string ThreadUrl;
        internal bool WantNewChat;
        // 发完之后网页当前所在的会话 URL（上层拿去落盘）
        internal string DiscoveredUrl;

        internal void SetThread(string url, bool newChat)
        {
            ThreadUrl = url;
            WantNewChat = newChat;
        }

        // 有绑定就导航回那条会话；没有就先开一条新对话
        async Task EnsureThreadAsync(CancellationToken token)
        {
            if (view == null || view.CoreWebView2 == null) return;
            string current = view.CoreWebView2.Source ?? "";

            if (!string.IsNullOrEmpty(ThreadUrl) && LooksLikeThreadUrl(ThreadUrl)
                && !string.Equals(current, ThreadUrl, StringComparison.OrdinalIgnoreCase))
            {
                Log("restore web thread " + ThreadUrl);
                view.CoreWebView2.Navigate(ThreadUrl);
                await WaitDocumentReadyAsync(token);
                await Task.Delay(800);   // 等历史消息渲染完（原来 1500，先 800 试）
                return;
            }

            if (WantNewChat || string.IsNullOrEmpty(ThreadUrl))
            {
                // 没有绑定却停在某条旧会话上：必须开新对话，否则会写进别人的会话里
                bool onConversation = current.IndexOf("/chat/", StringComparison.OrdinalIgnoreCase) >= 0;
                if (WantNewChat || onConversation)
                {
                    string result = await RunScript(NewChatJs());
                    Log("new chat: " + result);
                    await Task.Delay(900);
                }
            }
        }

        // 点站点自己的"新对话"（豆包/DS 文案不同，这里都覆盖）
        string NewChatJs()
        {
            return "(function(){" +
                   "var want=['新对话','开启新对话','新建对话','新会话'];" +
                   "var all=document.querySelectorAll('button,[role=button],div,span,a');" +
                   "var best=null;" +
                   "for(var i=0;i<all.length;i++){var el=all[i];" +
                   "var t=(el.innerText||'').trim();" +
                   "if(t.length===0||t.length>12)continue;" +
                   "for(var w=0;w<want.length;w++){if(t===want[w]){best=el;break;}}" +
                   "if(best)break;}" +
                   "if(!best)return 'not-found';" +
                   "var target=best;" +
                   "for(var k=0;k<3&&target;k++){" +
                   "if(target.tagName==='BUTTON'||target.getAttribute('role')==='button')break;" +
                   "target=target.parentElement;}" +
                   "if(!target)target=best;" +
                   "target.click();return 'clicked';})()";
        }

        // 发完之后把当前 URL 记下来（豆包是 /chat/<id>，DS 是 /a/chat/s/<id>）
        // 注意：刚点完发送那一瞬间 URL 还是 /chat/（没有 id），不能当会话地址用
        void CaptureThreadUrl()
        {
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                string url = view.CoreWebView2.Source;
                if (!LooksLikeThreadUrl(url)) return;
                if (string.Equals(url, DiscoveredUrl, StringComparison.OrdinalIgnoreCase)) return;
                DiscoveredUrl = url;
                Log("thread url = " + url);
            }
            catch (Exception) { }
        }

        // 会话地址 = 路径里带 /chat/，且最后一段长度 >= 6（空路径 /chat/ 不算）
        static bool LooksLikeThreadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url.IndexOf("/chat/", StringComparison.OrdinalIgnoreCase) < 0) return false;
            int end = url.Length;
            while (end > 0 && (url[end - 1] == '/' || url[end - 1] == '?')) end--;
            if (end <= 0) return false;
            int slash = url.LastIndexOf('/', end - 1);
            if (slash < 0) return false;
            return (end - slash - 1) >= 6;
        }

        static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webdriver.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal async Task AskAsync(string prompt, Action<string> onDelta, CancellationToken token)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long lastMark = 0;
            Action<string> mark = delegate(string stage)
            {
                Log("t=" + clock.ElapsedMilliseconds + "ms (+" + (clock.ElapsedMilliseconds - lastMark) + ") " + stage);
                lastMark = clock.ElapsedMilliseconds;
            };

            await EnsureReadyAsync(token);
            await EnsureLoggedInAsync(token);
            mark("ready");

            // 0) 胶囊会话 ↔ 网页会话：有记录就回那条，没有就开一条新的
            await EnsureThreadAsync(token);
            mark("thread");

            // 1) 先确保网页那边停下来：用户按 Esc 只停了我们这边的读取，网页还在继续生成。
            //    这里直接点一次"停止/发送"位置那个按钮 —— 生成中就停掉，空闲时输入框是空的，
            //    点一下不会发出空消息。（比"等文本不再变化"可靠：豆包的文本一直在变）
            await StopPageAsync();
            await Task.Delay(200);
            mark("stop-page");

            // 有图片就先塞进网页的文件输入框，等它上传完再发文字
            if (attachedFiles != null && attachedFiles.Length > 0)
            {
                string[] files = attachedFiles;
                attachedFiles = null;
                bool attached = await AttachImagesAsync(files);
                Log("attach " + files.Length + " image(s): " + attached);
                if (!attached)
                {
                    throw new InvalidOperationException("网页版没找到上传图片的入口（可能改版了）。先把图片去掉，或者手动贴进网页。");
                }
                await Task.Delay(2200);   // 等缩略图出来，别在传一半时点发送
            }

            // 等页面稳定：回答块数量连续两次一致再往下走。
            // 页面刚从旧会话切过来时 DOM 还没渲染完，这时候数块数会数成 0，
            // 于是把"旧回答"当成新回答推出去（实测踩到）。
            {
                int lastCount = -1;
                DateTime settle = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < settle)
                {
                    int c = await RunIntAsync(CountJs());
                    if (c == lastCount) break;
                    lastCount = c;
                    await Task.Delay(200);
                }
                Log("settled count=" + lastCount);
                mark("settle " + lastCount);
            }

            int before = await RunIntAsync(CountJs());
            int expected = before + (site.Kind == "db" ? 2 : 1);
            string previousBlock = site.Kind == "db" ? (await RunStringAsync(AnswerTextJs(0)) ?? "") : (await RunStringAsync(LastAnswerTextJs()) ?? "");
            Log("site=" + site.Id + " before=" + before + " expected=" + expected + " prevBlock=" + (previousBlock == null ? -1 : previousBlock.Length));

            // 2) 写入并发送
            string typed = await RunScript(TypeJs(prompt));
            await Task.Delay(120);
            string sendResult = await RunScript(SendJs());
            Log("type=" + typed + " send=" + sendResult);

            // 发出去之后 URL 会变成这条会话的地址，记下来（下次回到同一条）
            await Task.Delay(250);
            CaptureThreadUrl();
            mark("sent");
            if (sendResult != null && sendResult.StartsWith("NO-SEND", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("网页版没找到发送按钮（可能改版了），先用 API。");
            }

            // 3) 轮询最后一个回答块，按增量推给前端
            //    结束判定优先看"页面还在不在生成"（停止按钮是否还在）：
            //    停止按钮消失 + 文本没变 → 立刻结束，不再干等 5 个轮询。
            string last = "";
            int stable = 0;
            int polls = 0;
            bool firstContent = false;
            DateTime started = DateTime.UtcNow;
            while (!token.IsCancellationRequested)
            {
                double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                if (elapsed > 120)
                {
                    Log("timeout, last len=" + last.Length);
                    throw new InvalidOperationException("网页版 120 秒没有新内容，可能被限流了，先用 API。");
                }
                // 一直读不到内容：先 dump 一次页面结构（方便定位改版），20 秒直接放弃
                if (last.Length == 0 && elapsed > 3 && !dbDumped && site.Kind == "db")
                {
                    dbDumped = true;
                    try { Log("dump: " + await RunStringAsync(DbDumpJs())); } catch (Exception) { }
                }
                if (last.Length == 0 && elapsed > 60)
                {
                    // 网页版可能在思考 / 排队：给到 60 秒；真读不到再报错
                    throw new InvalidOperationException("网页版 60 秒没读到回答（页面结构可能变了，或还在排队思考）。可以重试，或先切 API 模型。");
                }

                string current;
                if (site.Kind == "db")
                {
                    current = await DbAnswerAsync(before, previousBlock, prompt);
                }
                else
                {
                    current = await RunStringAsync(AnswerTextJs(before));
                    if (current == null) current = "";
                    // 兜底：DeepSeek 改版后按下标取不到时，用「最后一条回答」和发送前的基线比对。
                    // 只要能读到新内容就继续，不至于干等 60 秒报错。
                    if (current.Length == 0)
                    {
                        string lastText = await RunStringAsync(LastAnswerTextJs());
                        if (!string.IsNullOrEmpty(lastText) && !string.Equals(lastText, previousBlock, StringComparison.Ordinal))
                        {
                            current = lastText;
                        }
                    }
                }
                polls++;
                if (polls <= 80) Log("poll " + polls + " len=" + current.Length + " t=" + (int)elapsed + "s");

                if (!firstContent && current.Length > 0)
                {
                    firstContent = true;
                    mark("first-content len=" + current.Length);
                }

                if (current != last)
                {
                    if (current.StartsWith(last, StringComparison.Ordinal))
                    {
                        onDelta(current.Substring(last.Length));
                    }
                    else if (last.Length == 0)
                    {
                        onDelta(current);
                    }
                    // 不是前缀（页面重渲染）：只更新基准，别把重复内容灌进气泡
                    last = current;
                    stable = 0;
                }
                else
                {
                    stable++;
                }

                // 生成状态：只要内容稳定下来就查一次，别等 %3
                bool generating = true;
                if (stable >= 2)
                {
                    try { generating = (await RunStringAsync(GeneratingJs())) == "1"; }
                    catch (Exception) { generating = true; }
                }

                // 同时要求「页面说停」+「连续 6 个轮询没变化」（约 1.8 秒），
                // 否则网页在思考/停顿一下就会被误判成生成完毕，答案被截断。
                if (stable >= 6 && last.Length > 0 && !generating)
                {
                    mark("finished (idle+stable, len=" + last.Length + ")");
                    return;
                }
                // 兜底：判断不了停止按钮时，至少 14 个轮询（约 4 秒）没变化才算完
                if (stable >= 14 && last.Length > 0)
                {
                    mark("finished (stable fallback, len=" + last.Length + ")");
                    return;
                }

                // 前 4 秒密集轮询（首字更快出来），之后放慢省 CPU
                await Task.Delay(elapsed < 4 ? 150 : 300);
            }
            token.ThrowIfCancellationRequested();
        }

        // 用户按了停止：网页那边其实还在生成，顺手把它也停掉，
        // 否则下一条要等它吐完（实测等了 30 秒）。
        internal void SetAttachments(string[] paths)
        {
            attachedFiles = paths;
        }

        string[] attachedFiles;
        int dbCalls;
        bool dbDumped;

        // 把本地图片塞进网页的 <input type=file> —— 走 CDP 的 DOM.setFileInputFiles，
        // 普通 JS 改不了 file input 的值（浏览器安全限制）。
        internal async Task<bool> AttachImagesAsync(string[] paths)
        {
            if (paths == null || paths.Length == 0) return true;
            if (view == null || view.CoreWebView2 == null) return false;

            try
            {
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
                string document = await view.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.getDocument", "{\"depth\":0}");
                int rootId = ExtractInt(document, "nodeId");
                if (rootId <= 0) { Log("cdp: no root node"); return false; }

                string found = await view.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.querySelector",
                    "{\"nodeId\":" + rootId + ",\"selector\":\"input[type=file]\"}");
                int nodeId = ExtractInt(found, "nodeId");
                if (nodeId <= 0) { Log("cdp: no file input"); return false; }

                var sb = new StringBuilder();
                sb.Append("{\"nodeId\":").Append(nodeId).Append(",\"files\":[");
                for (int i = 0; i < paths.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append((paths[i] ?? "").Replace("\\", "/")).Append('"');
                }
                sb.Append("]}");
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", sb.ToString());
                return true;
            }
            catch (Exception exception)
            {
                Log("attach failed: " + exception.Message);
                return false;
            }
        }

        static int ExtractInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return -1;
            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return -1;
            int colon = json.IndexOf(':', at + key.Length + 2);
            if (colon < 0) return -1;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            int value;
            if (i > start && int.TryParse(json.Substring(start, i - start), out value)) return value;
            return -1;
        }

        internal async Task StopPageAsync()
        {
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                string js = "(function(){var input=document.querySelector('" + Escape(site.InputSelector) + "');" +
                            "if(!input)return 'no-input';" +
                            "var empty=(input.value!==undefined)?((input.value||'').trim()===''):((input.innerText||'').trim()==='');" +
                            "if(!empty)return 'skip-has-text';" +
                            "var ir=input.getBoundingClientRect();" +
                            "var bs=document.querySelectorAll('button,[role=button],div[class*=button]');" +
                            "var best=null,bestRight=-1;" +
                            "for(var i=0;i<bs.length;i++){var b=bs[i];var r=b.getBoundingClientRect();" +
                            "if(r.width<20||r.height<20)continue;" +
                            "if(r.top<ir.bottom-40||r.top>ir.bottom+70)continue;" +
                            "if(r.left<ir.left+ir.width*0.6)continue;" +
                            "if(r.right>bestRight){bestRight=r.right;best=b;}}" +
                            "if(!best)return 'no-button';" +
                            "best.click();return 'stopped';})()";
                string result = await RunScript(js);
                Log("stop page: " + result);
            }
            catch (Exception exception)
            {
                Log("stop page failed: " + exception.Message);
            }
        }

        // 没登录就揭盖让用户扫码；登录完成后再盖回去
        async Task EnsureLoggedInAsync(CancellationToken token)
        {
            // 先给页面一点时间（第一次打开时 DOM 还没出来，别急着判"没登录"把窗口闪出来）
            DateTime loadDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < loadDeadline && !token.IsCancellationRequested)
            {
                if (await RunStringAsync(InputExistsJs()) == "1") return;
                await Task.Delay(500);
            }

            Log("not logged in, reveal window");
            Reveal(true);
            DateTime deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (await RunStringAsync(InputExistsJs()) == "1")
                {
                    await Task.Delay(600);
                    Reveal(false);
                    Log("login done, cloak window");
                    return;
                }
            }
            Reveal(false);
            throw new InvalidOperationException("网页版还没登录（等了三分钟），点网页版球时会把窗口临时露出来，登录一次就好。");
        }

        void Reveal(bool show)
        {
            if (window == null) return;
            try
            {
                if (show)
                {
                    window.Left = 200;
                    window.Top = 120;
                    window.ShowInTaskbar = true;
                    window.Topmost = true;
                    Cloak(false);
                    window.Activate();
                }
                else
                {
                    window.Topmost = false;
                    window.ShowInTaskbar = false;
                    Cloak(true);
                    window.Left = -4000;
                    window.Top = -4000;
                }
            }
            catch (Exception) { }
        }

        void Cloak(bool on)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int value = on ? 1 : 0;
                DwmSetWindowAttribute(handle, WM_CLOAK, ref value, sizeof(int));
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------- 脚本

        string TypeJs(string prompt)
        {
            string escaped = EscapeJson(prompt);
            if (site.Kind == "ds")
            {
                return "(function(){var el=document.querySelector('" + Escape(site.InputSelector) + "');" +
                       "if(!el)return 'NO-INPUT';" +
                       "var d=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value');" +
                       "d.set.call(el,\"" + escaped + "\");" +
                       "el.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:\"" + escaped + "\"}));" +
                       "el.focus();return 'ok';})()";
            }
            // contenteditable（豆包的 tiptap 编辑器）：只能走 execCommand，改 innerText 不会触发它的状态
            return "(function(){var el=document.querySelector('" + Escape(site.InputSelector) + "');" +
                   "if(!el)return 'NO-INPUT';el.focus();" +
                   "document.execCommand('insertText',false,\"" + escaped + "\");return 'ok';})()";
        }

        // 发送按钮：输入框下方横带里、最右边那个可点元素（两个站点通用）
        string SendJs()
        {
            return "(function(){var input=document.querySelector('" + Escape(site.InputSelector) + "');" +
                   "if(!input)return 'NO-INPUT';var ir=input.getBoundingClientRect();" +
                   "var bs=document.querySelectorAll('button,[role=button],div[class*=button]');" +
                   "var best=null,bestRight=-1;" +
                   "for(var i=0;i<bs.length;i++){var b=bs[i];var r=b.getBoundingClientRect();" +
                   "if(r.width<20||r.height<20)continue;" +
                   "if(r.top<ir.bottom-40||r.top>ir.bottom+70)continue;" +
                   "if(r.left<ir.left+ir.width*0.6)continue;" +
                   "if(b.disabled)continue;" +
                   "if(r.right>bestRight){bestRight=r.right;best=b;}}" +
                   "if(!best)return 'NO-SEND-BUTTON';" +
                   "best.click();return 'ok';})()";
        }

        string InputExistsJs()
        {
            return "(function(){return document.querySelector('" + Escape(site.InputSelector) + "')?'1':'0';})()";
        }

        // 豆包的消息列表：页面上有多个类名含 message-list 的容器
        // （"suggest-message-list-wrapper" 是推荐气泡，也算），直接 querySelector 会抓错。
        // 优先用"类名以 message-list 开头"的那个，抓不到再退回"子元素最多"的。
        // 之前只用"子元素最多"：空会话时推荐气泡（3 个）会赢 → 永远读不到正文（实测踩到）。
        const string DbListJs =
            "(function(){" +
            "var exact=document.querySelector('[class^=\"message-list\"],[class*=\" message-list\"]');" +
            "if(exact)return exact;" +
            "var a=document.querySelectorAll('[class*=message-list]');" +
            "var best=null,bestN=-1;for(var i=0;i<a.length;i++){var n=a[i].children.length;" +
            "if(n>bestN){bestN=n;best=a[i];}}return best;})()";

        // 数"回答块"个数：DeepSeek 一个回答一个块；豆包数"有文字的消息行"
        // （豆包在消息之间会插空行，按高度数会把空行也算进去）
        string CountJs()
        {
            if (site.Kind == "db")
            {
                return "(function(){var l=" + DbListJs + ";if(!l)return 0;" +
                       "var n=0;for(var i=0;i<l.children.length;i++){" +
                       "if(((l.children[i].innerText||'').trim()).length>0)n++;}return n;})()";
            }
            return "(function(){return document.querySelectorAll('" + Escape(site.AnswerSelector) + "').length;})()";
        }

        // 豆包的消息列表是"一轮一个块"（提问和回答在同一个块里），所以不能等"块数 +2"。
        // 做法：发送前记下最后一块的文本，之后只要文本变了就是新内容；再把开头回显的提问剥掉。
        async Task<string> DbAnswerAsync(int before, string previous, string prompt)
        {
            int count = await RunIntAsync(CountJs());
            string raw = await RunStringAsync(AnswerTextJs(0));
            string text = raw ?? "";
            dbCalls++;
            if (dbCalls % 10 == 1) Log("db: count=" + count + " before=" + before + " len=" + text.Length + " prev=" + (previous == null ? -1 : previous.Length));

            if (text.Length == 0) return "";

            string question = (prompt == null ? "" : prompt).Trim();
            // 关键守卫：必须先在页面里看到"我们刚发的提问"，才允许把文本当成新回答。
            // 否则列表重排/虚拟化时会把上一条回答当成新回答推出去（实测踩到）。
            int questionAt = question.Length == 0 ? -1 : text.LastIndexOf(question, StringComparison.Ordinal);
            bool hasQuestion = questionAt >= 0;

            string candidate;
            if (count >= before + 2 && previous != null && text != previous)
            {
                // 结构 A：一行一条消息 —— 最后一行就是这次的回答
                candidate = text;
            }
            else if (hasQuestion)
            {
                // 结构 B/C：提问和回答在同一块里，或者列表被重排。
                // 取提问后面的内容；还没有内容时返回空，继续等。
                string tail = text.Substring(questionAt + question.Length).Trim();
                if (tail.Length < 2) return "";
                candidate = tail;
            }
            else if (previous != null && previous.Length > 0
                     && text.Length > previous.Length
                     && text.StartsWith(previous, StringComparison.Ordinal))
            {
                // 纯粹靠前缀增长的情况（老结构），也要求比上一条长
                candidate = text.Substring(previous.Length).Trim();
            }
            else
            {
                return "";
            }

            if (candidate.StartsWith(question, StringComparison.Ordinal))
            {
                candidate = candidate.Substring(question.Length).Trim();
            }
            // 联网搜索的提示行："搜索 N 个关键词，参考 N 篇资料" / "找到 N 篇资料"
            for (int pass = 0; pass < 3; pass++)
            {
                string cleaned = SearchHintRegex.Replace(candidate, "");
                if (cleaned == candidate) break;
                candidate = cleaned.TrimStart();
            }
            if (LooksLikeStatus(candidate)) return "";
            return TrimTailNoise(candidate);
        }

        // 页面是否还在生成：站点的"停止"按钮还在 → 还在生成。
        // 比"文本连续几次不变"快得多（豆包生成长回答时中间会停顿）。
        string GeneratingJs()
        {
            return "(function(){var bs=document.querySelectorAll('button,[role=button],[aria-label]');" +
                   "for(var i=0;i<bs.length;i++){var b=bs[i];" +
                   "var t=(b.innerText||b.textContent||'').trim();" +
                   "if(t==='停止'||t==='停止生成'||t==='停止回答'||t==='Stop'||t==='Stop generating')return '1';" +
                   "var a=(b.getAttribute&&b.getAttribute('aria-label'))||'';" +
                   "if(a.indexOf('停止')>=0)return '1';" +
                   "var al=a.toLowerCase();if(al.indexOf('stop generating')>=0||al==='stop')return '1';" +
                   "var cl=(b.className&&b.className.toString?b.className.toString():'');" +
                   "if(cl.indexOf('stop')>=0&&t.length===0)return '1';" +
                   "}return '0';})()";
        }

        // 卡住时把消息列表结构写进日志：哪个容器、几个子节点、类名和文字长度
        string DbDumpJs()
        {
            return "(function(){var l=" + DbListJs + ";if(!l)return 'no-list';" +
                   "var s='list.class=['+(l.className||'')+'] children='+l.children.length;" +
                   "for(var i=0;i<l.children.length&&i<8;i++){var c=l.children[i];" +
                   "s+=' || ['+i+'] cls=['+((c.className||'').toString().slice(0,40))+'] len='+((c.innerText||'').trim().length);}" +
                   "return s;})()";
        }

        string AnswerTextJs(int index)
        {
            if (site.Kind == "db")
            {
                // 豆包：从后往前找第一条**去掉噪声后仍有文字**的消息行
                // （消息之间有空行、末尾还有只有"复制/推荐"的行，按位置取会取到它们）
                return "(function(){var l=" + DbListJs + ";if(!l)return '';" +
                       "var rows=[];for(var i=0;i<l.children.length;i++){" +
                       "if(((l.children[i].innerText||'').trim()).length>0)rows.push(l.children[i]);}" +
                       "for(var k=rows.length-1;k>=0;k--){" +
                       "var clone=rows[k].cloneNode(true);" +
                       "var junk=clone.querySelectorAll('[class*=action-bar],[class*=suggest]');" +
                       "for(var j=0;j<junk.length;j++){junk[j].parentNode.removeChild(junk[j]);}" +
                       "var text=(clone.innerText||'').trim();" +
                       "if(text.length>0)return text;}" +
                       "return '';})()";
            }
            return "(function(){var a=document.querySelectorAll('" + Escape(site.AnswerSelector) + "');" +
                   "var el=a[" + index + "];return el?el.innerText:'';})()";
        }

        // 最后一条回答的纯文本（多套选择器兜底，DeepSeek 改版也能读到）
        string LastAnswerTextJs()
        {
            string primary = Escape(site.AnswerSelector);
            string quote = "\"";
            return "(function(){var sels=['" + primary + "','.ds-markdown','[class*=" + quote + "ds-markdown" + quote + "]','[class*=" + quote + "markdown" + quote + "]','.prose'];" +
                   "for(var s=0;s<sels.length;s++){try{var a=document.querySelectorAll(sels[s]);" +
                   "if(a&&a.length){var t=(a[a.length-1].innerText||'').trim();if(t.length>0)return t;}}catch(e){}}" +
                   "return '';})()";
        }

        // 整段对话的纯文本（去掉动作条/推荐/回到底部按钮），给豆包做"快照 + 取新增尾巴"用
        string ListTextJs()
        {
            return "(function(){var l=document.querySelector('[class^=\"message-list\"],[class*=\" message-list\"]');" +
                   "if(!l)return '';" +
                   "var c=l.cloneNode(true);" +
                   "var j=c.querySelectorAll('[class*=action-bar],[class*=suggest],[class*=to-bottom]');" +
                   "for(var i=0;i<j.length;i++){j[i].parentNode.removeChild(j[i]);}" +
                   "return (c.innerText||'').trim();})()";
        }

        static bool LooksLikeStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length > 14) return false;          // 这么长的多半是真内容
            for (int i = 0; i < DbBusyHints.Length; i++)
            {
                if (text.IndexOf(DbBusyHints[i], StringComparison.Ordinal) >= 0) return true;
            }
            return text.IndexOf("正在", StringComparison.Ordinal) >= 0
                || text.IndexOf("搜索中", StringComparison.Ordinal) >= 0
                || text.IndexOf("思考中", StringComparison.Ordinal) >= 0;
        }

        // 豆包会在回答末尾挂"相关视频/相关推荐"和话题标签，从末尾切掉
        static string TrimTailNoise(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string[] marks = new string[] { "相关视频", "相关推荐", "相关问题", "相关搜索" };
            for (int i = 0; i < marks.Length; i++)
            {
                int index = text.LastIndexOf(marks[i], StringComparison.Ordinal);
                if (index > 0 && index > text.Length * 0.6)
                {
                    text = text.Substring(0, index).TrimEnd();
                }
            }
            return text;
        }

        static readonly string[] DbBusyHints = new string[]
        {
            "正在搜索", "正在思考", "深度思考中", "正在分析", "正在生成", "正在输入", "思考中"
        };

        // 豆包联网搜索会在正文前塞一行"搜索 N 个关键词，参考 N 篇资料"或"找到 N 篇资料"
        static readonly System.Text.RegularExpressions.Regex SearchHintRegex =
            new System.Text.RegularExpressions.Regex(
                @"^\s*(搜索|找到|参考)\s*\d+\s*(个关键词|篇资料|篇网页|篇来源|条结果)[，,、:：]?\s*",
                System.Text.RegularExpressions.RegexOptions.None);

        async Task<string> RunScript(string js)
        {
            if (view == null || view.CoreWebView2 == null) return null;
            string raw = await view.CoreWebView2.ExecuteScriptAsync(js);
            return Unquote(raw);
        }

        async Task<string> RunStringAsync(string js)
        {
            try { return await RunScript(js); }
            catch (Exception) { return null; }
        }

        async Task<int> RunIntAsync(string js)
        {
            try
            {
                string value = await RunScript(js);
                int parsed;
                if (int.TryParse(value, out parsed)) return parsed;
            }
            catch (Exception) { }
            return 0;
        }

        static string Escape(string selector)
        {
            return selector.Replace("'", "\\'");
        }

        static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string Unquote(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string s = json;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[++i];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 'r') sb.Append('\r');
                    else if (next == 't') sb.Append('\t');
                    else if (next == '"') sb.Append('"');
                    else if (next == '\\') sb.Append('\\');
                    else if (next == '/') sb.Append('/');
                    else if (next == 'u' && i + 4 < s.Length)
                    {
                        int code;
                        if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    else sb.Append(next);
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // 站点表：先只放 DeepSeek（选择器已验证），豆包等补齐容器选择器再加
    internal static class WebSites
    {
        internal static WebSiteConfig Find(string id)
        {
            if (string.Equals(id, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                var site = new WebSiteConfig();
                site.Id = "deepseek";
                site.Kind = "ds";
                site.Url = "https://chat.deepseek.com/";
                site.InputSelector = "textarea";
                site.AnswerSelector = ".ds-markdown.ds-assistant-message-main-content";
                site.LoginHint = "扫码";
                return site;
            }
            if (string.Equals(id, "doubao", StringComparison.OrdinalIgnoreCase))
            {
                var site = new WebSiteConfig();
                site.Id = "doubao";
                site.Kind = "db";
                site.Url = "https://www.doubao.com/chat/";
                site.InputSelector = "div.tiptap.ProseMirror";
                site.AnswerSelector = "";   // 走 message-list 的"最后一行"，不用固定选择器
                site.LoginHint = "登录";
                return site;
            }
            throw new InvalidOperationException("网页版驱动还不支持这个站点：" + id);
        }
    }
}
