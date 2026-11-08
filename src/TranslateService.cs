using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IslandShot
{
    // 翻译服务：当前用 MyMemory 免费接口（无需 key，实测国内可直连）。
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

        public static async Task<string> TranslateAsync(string text, string pair)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) +
                         "&langpair=" + Uri.EscapeDataString(pair.Replace("|", "|"));

            string body = await Client.GetStringAsync(url).ConfigureAwait(false);

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
