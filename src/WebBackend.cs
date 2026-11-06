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

        internal void SetAttachments(string[] paths)
        {
            pendingFiles = paths;
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

            // C# 5 不允许在 catch 里 await，所以先记下来再处理
            OperationCanceledException cancelled = null;
            try
            {
                if (pendingFiles != null)
                {
                    window.SetAttachments(pendingFiles);
                    pendingFiles = null;
                }
                await window.AskAsync(text, onDelta, token);
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

            // 1) 先确保网页那边停下来：用户按 Esc 只停了我们这边的读取，网页还在继续生成。
            //    这里直接点一次"停止/发送"位置那个按钮 —— 生成中就停掉，空闲时输入框是空的，
            //    点一下不会发出空消息。（比"等文本不再变化"可靠：豆包的文本一直在变）
            await StopPageAsync();
            await Task.Delay(500);

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
                DateTime settle = DateTime.UtcNow.AddSeconds(6);
                while (DateTime.UtcNow < settle)
                {
                    int c = await RunIntAsync(CountJs());
                    if (c == lastCount) break;
                    lastCount = c;
                    await Task.Delay(400);
                }
                Log("settled count=" + lastCount);
            }

            int before = await RunIntAsync(CountJs());
            int expected = before + (site.Kind == "db" ? 2 : 1);
            // 豆包：给整段对话拍个快照，之后只取"新增的尾巴"。
            // 之前取"最后一行"会把上一轮的提问/回答一起带进来（用户实测踩到，回答里混着旧内容）。
            string snapshot = site.Kind == "db" ? (await RunStringAsync(ListTextJs()) ?? "") : null;
            Log("site=" + site.Id + " before=" + before + " expected=" + expected + " snapshot=" + (snapshot == null ? -1 : snapshot.Length));

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

                string current;
                if (site.Kind == "db")
                {
                    current = await DbTailAsync(snapshot, prompt);
                }
                else
                {
                    current = await RunStringAsync(AnswerTextJs(before));
                    if (current == null) current = "";
                }
                polls++;
                if (polls <= 80) Log("poll " + polls + " len=" + current.Length);

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

        // 用户按了停止：网页那边其实还在生成，顺手把它也停掉，
        // 否则下一条要等它吐完（实测等了 30 秒）。
        internal void SetAttachments(string[] paths)
        {
            attachedFiles = paths;
        }

        string[] attachedFiles;

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

        // 豆包：拿"快照之后新增的那一段"，并去掉开头对提问的回显。
        // 不能要求整段严格 StartsWith —— 页面可能在别处插入（时间分隔、推荐等），
        // 一旦不匹配就整段丢弃，会表现为"回答只出几个字就停"（实测踩到）。
        async Task<string> DbTailAsync(string snapshot, string prompt)
        {
            string now = await RunStringAsync(ListTextJs());
            if (string.IsNullOrEmpty(now)) return "";
            if (snapshot == null) snapshot = "";
            if (now.Length <= snapshot.Length) return now == snapshot ? "" : "";

            int common = 0;
            while (common < snapshot.Length && common < now.Length && snapshot[common] == now[common]) common++;
            // 公共前缀太短说明这一屏已经换了（比如滚动/虚拟化），先不推，等稳定
            if (snapshot.Length > 40 && common < snapshot.Length / 2) return "";

            string tail = now.Substring(common).Trim();
            string question = (prompt == null ? "" : prompt).Trim();
            if (question.Length > 0 && tail.StartsWith(question, StringComparison.Ordinal))
            {
                tail = tail.Substring(question.Length).Trim();
            }
            // 联网搜索会在回答前面塞提示："搜索 2 个关键词，参考 11 篇资料" / "找到 2 篇资料"
            for (int pass = 0; pass < 3; pass++)
            {
                string cleaned = SearchHintRegex.Replace(tail, "");
                if (cleaned == tail) break;
                tail = cleaned.TrimStart();
            }
            // 「正在搜索图片 / 正在思考」这类是状态行，不是回答（豆包开了联网搜索会先显示一下）。
            // 之前只做精确匹配，"正在搜索图片"这种变体照样被当成回答推出去、还会被判成"回答结束"。
            if (LooksLikeStatus(tail)) return "";
            return TrimTailNoise(tail);
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
