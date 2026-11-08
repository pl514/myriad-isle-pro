using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace IslandShot
{
    // 用应用里已经配置好的 AI 模型（OpenAI 兼容接口）来翻译。
    // 好处：没有 MyMemory 那种按 IP 限流，质量也高得多（尤其是整段/整页）。
    // 没有配置任何带 key 的模型时，IsAvailable 返回 false，调用方自动退回 MyMemory。
    internal static class AiTranslateService
    {
        static readonly HttpClient Client = CreateClient();
        static bool probed;
        static AiModelConfig model;
        static string apiKey;
        internal static string LastError = "";

        static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception) { }
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(40);
            return client;
        }

        static void Ensure()
        {
            if (probed) return;
            probed = true;
            try
            {
                foreach (AiModelConfig item in AiConfigStore.LoadModels())
                {
                    if (string.IsNullOrEmpty(item.BaseUrl) || string.IsNullOrEmpty(item.Model)) continue;
                    string key = string.IsNullOrEmpty(item.KeyRef) ? "" : AiConfigStore.GetKey(item.KeyRef);
                    if (string.IsNullOrEmpty(key)) continue;
                    model = item;
                    apiKey = key;
                    return;
                }
            }
            catch (Exception) { }
        }

        // 列出面板里配了 key 的模型，供翻译页选择
        internal static List<AiModelConfig> AvailableModels()
        {
            var list = new List<AiModelConfig>();
            try
            {
                foreach (AiModelConfig item in AiConfigStore.LoadModels())
                {
                    if (string.IsNullOrEmpty(item.BaseUrl) || string.IsNullOrEmpty(item.Model)) continue;
                    if (item.BaseUrl.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(item.KeyRef)) continue;
                    if (!AiConfigStore.HasKey(item.KeyRef)) continue;
                    list.Add(item);
                }
            }
            catch (Exception) { }
            return list;
        }

        // 指定模型（模型 id）；传空表示自动挑第一个可用的
        internal static void SelectModel(string modelId)
        {
            probed = true;
            model = null;
            apiKey = null;
            try
            {
                foreach (AiModelConfig item in AvailableModels())
                {
                    if (!string.IsNullOrEmpty(modelId) && !string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase)) continue;
                    string key = AiConfigStore.GetKey(item.KeyRef);
                    if (string.IsNullOrEmpty(key)) continue;
                    model = item;
                    apiKey = key;
                    return;
                }
            }
            catch (Exception) { }
        }

        internal static bool IsAvailable()
        {
            Ensure();
            return model != null && !string.IsNullOrEmpty(apiKey);
        }

        internal static bool UsingModel(string modelId)
        {
            Ensure();
            return model != null && string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase);
        }

        internal static string DisplayName()
        {
            Ensure();
            return model == null ? "" : model.DisplayName();
        }

        // 同步调用（在后台线程里用）。失败返回 null，调用方可以退回 MyMemory。
        internal static string Translate(string text, string pair)
        {
            Ensure();
            if (model == null || string.IsNullOrEmpty(text)) return null;

            bool toChinese = pair != null && pair.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            string system = toChinese
                ? "你是专业翻译。把用户给出的每一行文本翻译成简体中文。"
                : "你是专业翻译。把用户给出的每一行文本翻译成英文。";
            system += "严格逐行对应输出：输入几行就输出几行，不要加编号、不要解释、不要合并、不要空行。原文里的数字和标点保留。";

            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var payload = new Dictionary<string, object>
            {
                { "model", model.Model },
                { "temperature", 0 },
                { "stream", false },
                {
                    "messages", new object[]
                    {
                        new Dictionary<string, object> { { "role", "system" }, { "content", system } },
                        new Dictionary<string, object> { { "role", "user" }, { "content", text } }
                    }
                }
            };
            string body = serializer.Serialize(payload);

            string endpoint = model.BaseUrl.TrimEnd('/');
            if (endpoint.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) < 0)
            {
                endpoint += "/chat/completions";
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        using (HttpResponseMessage response = Client.SendAsync(request).GetAwaiter().GetResult())
                        {
                            string raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            if (!response.IsSuccessStatusCode) throw new Exception("HTTP " + (int)response.StatusCode);
                            string content = ExtractContent(raw);
                            if (!string.IsNullOrWhiteSpace(content)) return content.Trim();
                            throw new Exception("empty");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    if (attempt == 0) Thread.Sleep(600);
                }
            }
            return null;
        }

        // 从 chat/completions 响应里取 choices[0].message.content（对数组类型做兼容）
        static string ExtractContent(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null) { LastError = "返回不是 JSON"; return null; }

                object choices;
                if (!root.TryGetValue("choices", out choices) || choices == null)
                {
                    object error;
                    if (root.TryGetValue("error", out error) && error != null) LastError = "服务报错：" + error.ToString();
                    return null;
                }

                var list = choices as System.Collections.IList;
                if (list == null || list.Count == 0) { LastError = "choices 为空"; return null; }
                var first = list[0] as Dictionary<string, object>;
                if (first == null) { LastError = "choices[0] 类型异常"; return null; }

                object message;
                if (!first.TryGetValue("message", out message)) { LastError = "无 message"; return null; }
                var messageDict = message as Dictionary<string, object>;
                if (messageDict == null) { LastError = "message 类型异常"; return null; }

                object content;
                if (!messageDict.TryGetValue("content", out content) || content == null) { LastError = "content 为空"; return null; }
                return content.ToString();
            }
            catch (Exception ex)
            {
                LastError = "解析失败：" + ex.Message;
                return null;
            }
        }    }
}
