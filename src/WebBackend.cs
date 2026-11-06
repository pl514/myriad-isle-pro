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

            await window.AskAsync(text, onDelta, token);
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
            await EnsureReadyAsync(token);
            await EnsureLoggedInAsync(token);

            // 1) 先数一下现在有几个回答块；新回答会排在后面
            int before = await RunIntAsync(CountJs());
            int expected = before + (site.Kind == "db" ? 2 : 1);
            // 豆包要记下"上一条回答"，否则会先把旧回答当成新的推出去
            string previous = site.Kind == "db" ? (await RunStringAsync(AnswerTextJs(before)) ?? "") : null;
            Log("site=" + site.Id + " before=" + before + " expected=" + expected + " prevLen=" + (previous == null ? -1 : previous.Length));

            // 2) 写入并发送
            string typed = await RunScript(TypeJs(prompt));
            await Task.Delay(120);
            string sendResult = await RunScript(SendJs());
            Log("type=" + typed + " send=" + sendResult);
            if (sendResult != null && sendResult.StartsWith("NO-SEND", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("网页版没找到发送按钮（可能改版了），先用 API。");
            }

            // 3) 轮询最后一个回答块，按增量推给前端
            string last = "";
            int stable = 0;
            int polls = 0;
            DateTime started = DateTime.UtcNow;
            while (!token.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - started).TotalSeconds > 120)
                {
                    Log("timeout, last len=" + last.Length);
                    throw new InvalidOperationException("网页版 120 秒没有新内容，可能被限流了，先用 API。");
                }

                string current = await RunStringAsync(AnswerTextJs(before));
                if (current == null) current = "";
                if (site.Kind == "db" && current == previous) current = "";   // 还是上一条回答 = 新的还没出来
                polls++;
                if (polls % 10 == 1) Log("poll " + polls + " len=" + current.Length);

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
                    if (stable >= 5 && last.Length > 0) return;   // 约 1.5 秒没变化 → 结束
                }

                await Task.Delay(300);
            }
            token.ThrowIfCancellationRequested();
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

        // 数"回答块"个数：DeepSeek 一个回答一个块；豆包是消息行里较高的一行算一行
        string CountJs()
        {
            if (site.Kind == "db")
            {
                return "(function(){var l=document.querySelector('[class*=message-list]');if(!l)return 0;" +
                       "var n=0;for(var i=0;i<l.children.length;i++){" +
                       "if(l.children[i].getBoundingClientRect().height>40)n++;}return n;})()";
            }
            return "(function(){return document.querySelectorAll('" + Escape(site.AnswerSelector) + "').length;})()";
        }

        string AnswerTextJs(int index)
        {
            if (site.Kind == "db")
            {
                // 豆包：取最后一条消息行，克隆后删掉动作条和推荐气泡，剩下的就是回答正文
                return "(function(){var l=document.querySelector('[class*=message-list]');if(!l)return '';" +
                       "var rows=[];for(var i=0;i<l.children.length;i++){" +
                       "if(l.children[i].getBoundingClientRect().height>40)rows.push(l.children[i]);}" +
                       "if(!rows.length)return '';" +
                       "var row=rows[rows.length-1];var clone=row.cloneNode(true);" +
                       "var junk=clone.querySelectorAll('[class*=action-bar],[class*=suggest]');" +
                       "for(var j=0;j<junk.length;j++){junk[j].parentNode.removeChild(junk[j]);}" +
                       "return (clone.innerText||'').trim();})()";
            }
            return "(function(){var a=document.querySelectorAll('" + Escape(site.AnswerSelector) + "');" +
                   "var el=a[" + index + "];return el?el.innerText:'';})()";
        }

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
