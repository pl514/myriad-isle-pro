using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // M2：OpenAI 兼容的流式后端（智谱 / DeepSeek / 任何兼容服务商共用）
    //
    // 三条硬约束（来自 docs\AI后端-交接包.md）：
    //   1) onDelta 必须是增量，前端自己累加；
    //   2) 正常结束/被取消 正常返回，不抛异常；
    //   3) 出错抛异常，前端统一显示错误气泡。
    //
    // 还有一条 §4.6 的隐形约束：**这个文件里绝对不能出现 ConfigureAwait(false)**，
    // 否则 onDelta 会跑到线程池上，前端跨线程改 TextBlock 直接崩。
    // ---------------------------------------------------------------------
    internal sealed class OpenAiCompatBackend : IChatBackend
    {
        // HttpClient 复用一个：每次 new 会耗连接池
        static readonly HttpClient Client = CreateClient();

        readonly string endpoint;
        readonly string model;
        readonly string apiKey;
        readonly string reasoningEffort;   // 空 = 不传；否则 @"low"/"medium"/... 只对声明了 effort 的模型传

        internal OpenAiCompatBackend(string baseUrl, string model, string apiKey)
            : this(baseUrl, model, apiKey, null)
        {
        }

        internal OpenAiCompatBackend(string baseUrl, string model, string apiKey, string reasoningEffort)
        {
            this.endpoint = (baseUrl == null ? "" : baseUrl).TrimEnd('/') + "/chat/completions";
            this.model = model == null ? "" : model;
            this.apiKey = apiKey == null ? "" : apiKey;
            this.reasoningEffort = reasoningEffort == null ? "" : reasoningEffort.Trim();
        }

        static HttpClient CreateClient()
        {
            // .NET Framework 4.x 在旧系统上可能默认 TLS1.0，连现代 API 会直接握手失败
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
                    | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception) { }

            var client = new HttpClient();
            client.Timeout = Timeout.InfiniteTimeSpan;   // 流式不能用整体超时，会把长回答截断
            return client;
        }

        public async Task SendAsync(string text, Action<string> onDelta, CancellationToken token)
        {
            if (onDelta == null) return;

            string body = "{\"model\":\"" + JsonEscape(model) + "\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\""
                + JsonEscape(text) + "\"}]";
            if (reasoningEffort.Length > 0)
            {
                body += ",\"reasoning_effort\":\"" + JsonEscape(reasoningEffort) + "\"";
            }
            body += "}";

            using (var total = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                total.CancelAfter(TimeSpan.FromSeconds(180));   // 总时长

                // 429/502/503 自动重试：智谱免费档很容易触发限流，
                // 一次就失败对用户来说就是"不能用"。
                HttpResponseMessage response = null;
                int attempt = 0;
                int lastStatus = 0;
                string lastBody = "";
                while (true)
                {
                    attempt++;
                    HttpRequestMessage request = BuildRequest(body);

                    // 等响应头（≈ 连接 + 首字）最多 25 秒
                    using (var headerTimer = CancellationTokenSource.CreateLinkedTokenSource(total.Token))
                    {
                        headerTimer.CancelAfter(TimeSpan.FromSeconds(25));
                        try
                        {
                            response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimer.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (token.IsCancellationRequested) throw;   // 用户主动停：交给前端静默处理
                            throw new InvalidOperationException("等太久了（25 秒没有响应），已取消");
                        }
                        catch (HttpRequestException exception)
                        {
                            throw BuildNetworkError(exception);
                        }
                    }

                    if (response.IsSuccessStatusCode) break;

                    lastStatus = (int)response.StatusCode;
                    lastBody = "";
                    try { lastBody = await response.Content.ReadAsStringAsync(); } catch (Exception) { }
                    response.Dispose();

                    bool retryable = lastStatus == 429 || lastStatus == 502 || lastStatus == 503 || lastStatus == 504;
                    if (retryable && attempt < 3)
                    {
                        Program.Append("MyriadIslePro-error.log",
                            "retry #" + attempt + " status=" + lastStatus + " " + lastBody);
                        // 智谱的 1302 是账户级速率限制，退避要够长才有意义（1.5s → 3.5s）
                        await Task.Delay(attempt == 1 ? 1500 : 3500, token);
                        continue;
                    }
                    throw BuildHttpError(lastStatus, lastBody, attempt);
                }

                using (response)
                {

                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        bool sawAny = false;
                        while (true)
                        {
                            if (total.IsCancellationRequested)
                            {
                                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                                throw new InvalidOperationException("等太久了（超过 180 秒），已断开");
                            }

                            string line = await reader.ReadLineAsync();
                            if (line == null) break;
                            if (line.Length == 0) continue;
                            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                            string payload = line.Substring(5).Trim();
                            if (payload == "[DONE]") break;

                            string delta = ExtractDelta(payload);
                            if (string.IsNullOrEmpty(delta)) continue;
                            sawAny = true;
                            onDelta(delta);
                        }
                        if (!sawAny) throw new InvalidOperationException("服务返回了空内容，检查模型 ID 是否正确。");
                    }
                }
            }
        }

        // 每次重试都要新建 request：HttpRequestMessage 发过一次就不能复用
        HttpRequestMessage BuildRequest(string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (apiKey.Length > 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }

        // ------------------------------------------------------------ 解析

        /// <summary>从一个 SSE data 分片里取 choices[0].delta.content。</summary>
        internal static string ExtractDelta(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int deltaIndex = json.IndexOf("\"delta\"", StringComparison.Ordinal);
            if (deltaIndex < 0) return null;

            int contentIndex = json.IndexOf("\"content\"", deltaIndex, StringComparison.Ordinal);
            if (contentIndex < 0) return null;

            int colon = json.IndexOf(':', contentIndex);
            if (colon < 0) return null;

            // 关键：不能直接找下一个引号——遇到 "content":null 会一路找到 "finish_reason" 的引号，
            // 把字段名当正文吐出来。冒号后必须是引号才算字符串。
            int cursor = colon + 1;
            while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
            if (cursor >= json.Length || json[cursor] != '"') return null;

            var sb = new StringBuilder();
            for (int i = cursor + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[++i];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 't') sb.Append('\t');
                    else if (next == 'r') sb.Append('\r');
                    else if (next == '"') sb.Append('"');
                    else if (next == '\\') sb.Append('\\');
                    else if (next == '/') sb.Append('/');
                    else if (next == 'u' && i + 4 < json.Length)
                    {
                        int code;
                        if (int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ 错误文案

        static Exception BuildHttpError(int status, string body, int attempts)
        {
            string snippet = body == null ? "" : body.Trim();
            if (snippet.Length > 200) snippet = snippet.Substring(0, 200) + "…";
            string tried = attempts > 1 ? "（已自动重试 " + (attempts - 1) + " 次）" : "";

            if (status == 401) return new InvalidOperationException("API Key 无效（401）。检查 data\\keys.txt 里那一行。");
            if (status == 402) return new InvalidOperationException("余额或免费额度不足（402）。");
            if (status == 403) return new InvalidOperationException("这个 Key 没有权限访问该模型（403）。" + snippet);
            if (status == 404) return new InvalidOperationException("服务端没有这个模型，或者接口地址不对（404）。检查 models.txt 里的 model 列。");
            if (status == 429)
            {
                return new InvalidOperationException(
                    "免费额度被限流了（429）" + tried + "。\n" +
                    "智谱免费档有并发/频率上限，隔几秒再发一句，或者把 chip 切到 DeepSeek。\n" + snippet);
            }
            return new InvalidOperationException("请求失败（" + status + "）" + tried + "：" + snippet);
        }

        Exception BuildNetworkError(HttpRequestException exception)
        {
            bool local = endpoint.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0
                || endpoint.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0;

            string detail = Describe(exception);
            Program.Append("MyriadIslePro-error.log", "HTTP FAIL endpoint=" + endpoint + " \n" + detail);

            if (local)
            {
                return new InvalidOperationException(
                    "连不上本机模型服务（" + endpoint + "）。这个地址是我测试用的 mock，不该作为默认。\n" + detail);
            }
            return new InvalidOperationException("网络不通或接口地址不对（" + endpoint + "）：\n" + detail);
        }

        // 把异常链摊平，用户能直接看出是哪一层挂了
        static string Describe(Exception exception)
        {
            var sb = new StringBuilder();
            int depth = 0;
            Exception current = exception;
            while (current != null && depth < 4)
            {
                if (depth > 0) sb.Append("\n  ↑ 由 ");
                sb.Append(current.GetType().Name).Append(": ").Append(current.Message);
                current = current.InnerException;
                depth++;
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------------
    // 按当前模型配置造后端。配置不完整时抛出"人话"异常，前端直接显示成错误气泡。
    // ---------------------------------------------------------------------
    internal static class ChatBackends
    {
        static readonly Dictionary<string, WebBackend> webCache = new Dictionary<string, WebBackend>(StringComparer.OrdinalIgnoreCase);

        internal static IChatBackend Resolve(AiModelConfig model)
        {
            return Resolve(model, null);
        }

        // effort：推理等级（"低/中/高/极高/最高" 映射成 minimal/low/medium/high/xhigh），
        // 只有模型在 models.txt 里声明了 reasoning=effort 才会真的塞进请求体。
        internal static IChatBackend Resolve(AiModelConfig model, string effort)
        {
            if (model == null)
            {
                throw new InvalidOperationException("还没有选择模型：点底栏的模型 chip 选一个。");
            }
            if (string.IsNullOrEmpty(model.BaseUrl))
            {
                throw new InvalidOperationException("这个模型没有配接口地址，检查 data\\models.txt 的 baseUrl 列。");
            }

            // 实验功能：baseUrl 写成 web://站点 就走网页版驱动（不需要 key）
            if (model.BaseUrl.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
            {
                string siteId = model.BaseUrl.Substring(6).Trim().TrimEnd('/');
                // 复用同一个 WebBackend：每次新建都会重新初始化 WebView2（实测多约 4 秒）
                lock (webCache)
                {
                    WebBackend cached;
                    if (!webCache.TryGetValue(siteId, out cached))
                    {
                        cached = new WebBackend(WebSites.Find(siteId));
                        webCache[siteId] = cached;
                    }
                    return cached;
                }
            }

            string key = null;
            if (!string.IsNullOrEmpty(model.KeyRef))
            {
                key = AiConfigStore.GetKey(model.KeyRef);
                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException(
                        "这个模型还没填 API Key。点底栏的模型 chip →「编辑当前模型」→ 把 Key 填进去保存。\n"
                        + "（也可以手改 data\\keys.txt 加一行：" + model.KeyRef + "|plain:你的key）");
                }
            }
            return new OpenAiCompatBackend(model.BaseUrl, model.Model, key,
                string.Equals(model.Reasoning, "effort", StringComparison.OrdinalIgnoreCase) ? effort : null);
        }
    }
}
