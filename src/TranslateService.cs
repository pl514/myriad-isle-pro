using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Web.Script.Serialization;

namespace IslandShot
{
    // 翻译引擎选择：auto（有 key 就用 AI）/ free（只用 MyMemory）/ 具体模型 id
    internal static class TranslateSettings
    {
        internal const string Auto = "auto";
        internal const string Free = "free";

        static string Path_
        {
            get
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "translate-engine.txt");
            }
        }

        internal static string Engine
        {
            get
            {
                try
                {
                    if (!System.IO.File.Exists(Path_)) return Auto;
                    string text = System.IO.File.ReadAllText(Path_, Encoding.UTF8).Trim();
                    return text.Length == 0 ? Auto : text;
                }
                catch (Exception) { return Auto; }
            }
            set
            {
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(Path_);
                    if (!string.IsNullOrEmpty(folder) && !System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllText(Path_, string.IsNullOrEmpty(value) ? Auto : value, new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }
    }

    // 翻译服务：优先用应用里配置的 AI 模型；没有 key 时退回 MyMemory 免费接口。
    // 以后要换供应商，只需要改 TranslateAsync 里的实现。
    internal static class TranslateService
    {
        static readonly HttpClient Client = CreateClient();

        static TranslateService()
        {
            // .NET Framework 默认可能只协商 TLS 1.0/1.1，接口要求 1.2
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception) { }
            ServicePointManager.Expect100Continue = false;
        }

        static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            return client;
        }

        // 文本里含中日韩字符 → 认为源语言是中文
        public static bool LooksChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        public static string PairFor(string text, bool forceChineseToEnglish)
        {
            bool chinese = LooksChinese(text);
            if (forceChineseToEnglish) return "zh-CN|en";
            return chinese ? "zh-CN|en" : "en|zh-CN";
        }

        public static string TargetLabel(string pair)
        {
            return pair == "zh-CN|en" ? "英文" : "中文";
        }

        // 译文和原文几乎一样（去掉空白/标点后相同）→ 视为没翻译
        internal static bool LooksUnchanged(string source, string target)
        {
            return Strip(source) == Strip(target);
        }

        static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        // 只用免费通道（AI 结果无效时兜底）
        public static async Task<string> TranslateFreeAsync(string text, string pair)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) +
                         "&langpair=" + Uri.EscapeDataString(pair) +
                         "&de=myriadisle%40outlook.com";
            string body = await Client.GetStringAsync(url).ConfigureAwait(false);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var root = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(body);
            if (root == null) return "";
            object data;
            if (!root.TryGetValue("responseData", out data) || data == null) return "";
            var dataDict = data as System.Collections.Generic.Dictionary<string, object>;
            if (dataDict == null) return "";
            object translated;
            if (!dataDict.TryGetValue("translatedText", out translated) || translated == null) return "";
            return WebUtility.HtmlDecode(translated.ToString());
        }

        // 界面显示当前实际用的是哪个引擎
        internal static string EngineLabel()
        {
            if (string.Equals(TranslateSettings.Engine, TranslateSettings.Free, StringComparison.OrdinalIgnoreCase))
                return "免费 MyMemory";
            if (AiTranslateService.IsAvailable()) return AiTranslateService.DisplayName();
            return "免费 MyMemory";
        }

        public static async Task<string> TranslateAsync(string text, string pair)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // 按设置选择引擎：auto = 有 key 就用 AI；free = 只用 MyMemory；其他 = 指定模型
            string engine = TranslateSettings.Engine;
            if (!string.Equals(engine, TranslateSettings.Free, StringComparison.OrdinalIgnoreCase))
            {
                string wanted = string.Equals(engine, TranslateSettings.Auto, StringComparison.OrdinalIgnoreCase) ? "" : engine;
                AiTranslateService.SelectModel(wanted);
            }
            if (!string.Equals(engine, TranslateSettings.Free, StringComparison.OrdinalIgnoreCase) && AiTranslateService.IsAvailable())
            {
                string ai = await Task.Run(delegate { return AiTranslateService.Translate(text, pair); }).ConfigureAwait(false);
                // AI 有时会把原文原样吐回来（等于没翻），这种结果不算成功，继续走免费通道
                if (!string.IsNullOrWhiteSpace(ai) && !LooksUnchanged(text, ai)) return ai.Trim();
            }

            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) +
                         "&langpair=" + Uri.EscapeDataString(pair.Replace("|", "|")) +
                         "&de=myriadisle%40outlook.com";

            string body = "";
            for (int quotaRetry = 0; quotaRetry < 2; quotaRetry++)
            {
                body = await Client.GetStringAsync(url).ConfigureAwait(false);
                // 额度用完时接口会把提示语当译文返回，等一下再试一次
                if (body.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) < 0 &&
                    body.IndexOf("QUOTA", StringComparison.OrdinalIgnoreCase) < 0) break;
                if (quotaRetry == 0) await Task.Delay(2500).ConfigureAwait(false);
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var root = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(body);
            if (root == null) return "";

            object status;
            if (root.TryGetValue("responseStatus", out status) && Convert.ToInt32(status) != 200)
            {
                string detail = "";
                object details;
                if (root.TryGetValue("responseDetails", out details) && details != null) detail = details.ToString();
                throw new Exception("翻译服务返回错误：" + (detail == "" ? status.ToString() : detail));
            }

            object data;
            if (!root.TryGetValue("responseData", out data) || data == null) return "";
            var dataDict = data as System.Collections.Generic.Dictionary<string, object>;
            if (dataDict == null) return "";

            object translated;
            if (!dataDict.TryGetValue("translatedText", out translated) || translated == null) return "";
            string result = translated.ToString();

            // MyMemory 偶尔返回全大写的提示语，原样抛给界面不方便辨认
            if (result.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new Exception("今天的免费翻译额度用完了，明天再试。");
            }
            return WebUtility.HtmlDecode(result);
        }
    }
}
