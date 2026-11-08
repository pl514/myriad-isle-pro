using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 原生扫码登录：二维码画在面板里，不打开网页。
    //
    // 目前只有网易云（MusicPlatform.SupportsNativeQr）：
    //   1. POST /api/login/qrcode/unikey        → 拿到 unikey（纯文本，不用加密）
    //   2. 二维码内容 = https://music.163.com/login?codekey=<unikey>
    //      → 自己用 QrEncoder 画出来（所以不用网页）
    //   3. 每 2 秒 GET /api/login/qrcode/client/login?key=...
    //        801 等待扫码 / 802 已扫码待确认 / 803 成功（这时的响应带 MUSIC_U）/ 800 过期
    //   4. 成功：把 cookie 存到 data\music\cookies-<平台Id>.txt，交给上层切状态
    //
    // 另外两个平台（QQ / 酷狗 / 汽水）暂时还走网页登录：QQ 虽然也有
    // ssl.ptlogin2.qq.com/ptqrshow 能直接给二维码图片，但扫完还要走 QQ互联的
    // OAuth 换票，y.qq.com 的 qm_keyst 不是一步能拿到的，没验证过就不硬上。
    // ---------------------------------------------------------------------
    internal sealed class MusicQrLogin
    {
        const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 NeteaseMusicDesktop/3.1.17.204416";

        readonly MusicPlatform platform;

        // 二维码流程的调试日志：data\music\qr.log
        internal static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "qr.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
        readonly CookieContainer jar = new CookieContainer();
        readonly HttpClient client;
        DispatcherTimer timer;
        string neteaseKey = "";
        bool active;

        /// <summary>二维码画好了（已经可以直接塞进 Image）。</summary>
        internal event Action<ImageSource> QrReady;

        /// <summary>状态文案，"等待扫码"这类。</summary>
        internal event Action<string> StatusChanged;

        /// <summary>二维码过期，需要用户点刷新。</summary>
        internal event Action Expired;

        /// <summary>失败（网络 / 接口变了），文案已经是可以直接显示的中文。</summary>
        internal event Action<string> Failed;

        /// <summary>登录成功，带回来的 cookie（name → value），由上层落盘并切状态。</summary>
        internal event Action<Dictionary<string, string>> Succeeded;

        internal MusicQrLogin(MusicPlatform platform)
        {
            this.platform = platform;
            var handler = new HttpClientHandler
            {
                CookieContainer = jar,
                UseCookies = false
            };
            client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        internal bool SupportsNativeQr
        {
            get { return platform != null && platform.NativeQr; }
        }

        internal void Start()
        {
            if (!SupportsNativeQr)
            {
                Raise(Failed, "这个平台还没有原生扫码接口，走网页登录。");
                return;
            }
            active = true;
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += delegate { Poll(); };
            }
            SeedNeteaseCookies();
            StartNetEase();
        }

        internal void Stop()
        {
            active = false;
            if (timer != null) timer.Stop();
        }

        // ------------------------------------------------------------ 网易云

        // Folia 同款：请求带着"官方 PC 客户端"的默认 cookie，否则扫码确认会被判非法
        string neteaseCookieHeader = "";

        void SeedNeteaseCookies()
        {
            try
            {
                var random = new Random();
                string nuid = Guid.NewGuid().ToString("N");
                long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                const string chars = "abcdefghijklmnopqrstuvwxyz";
                var builder = new StringBuilder();
                for (int i = 0; i < 6; i++) builder.Append(chars[random.Next(chars.Length)]);
                string wnmcid = builder.ToString() + "." + ms + ".01.0";

                var pairs = new List<string>
                {
                    "__remember_me=true",
                    "ntes_kaola_ad=1",
                    "_ntes_nuid=" + nuid,
                    "_ntes_nnid=" + nuid + "," + ms,
                    "WNMCID=" + wnmcid,
                    "WEVNSM=1.0.0",
                    "os=pc",
                    "appver=3.1.17.204416",
                    "osver=Microsoft-Windows-10-Professional-build-19045-64bit",
                    "channel=netease",
                    "deviceId=" + Guid.NewGuid().ToString("N").ToUpperInvariant()
                };
                neteaseCookieHeader = string.Join("; ", pairs.ToArray());
                Log("netease cookie header ready");
            }
            catch (Exception exception) { Log("netease seed failed: " + exception.Message); }
        }
        async void StartNetEase()
        {
            Raise(StatusChanged, "正在生成二维码…");
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com/api/login/qrcode/unikey"))
                {
                    request.Content = new StringContent("type=3", Encoding.UTF8, "application/x-www-form-urlencoded");
                    request.Headers.Referrer = new Uri("https://music.163.com/");
                    if (!string.IsNullOrEmpty(neteaseCookieHeader))
                    {
                        request.Headers.TryAddWithoutValidation("Cookie", neteaseCookieHeader);
                    }
                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        string key = ReadJsonString(body, "unikey");
                        Log("netease unikey=" + key);
                        if (string.IsNullOrEmpty(key))
                        {
                            Raise(Failed, "网易云没有返回二维码（接口可能变了）：" + Shorten(body));
                            active = false;
                            return;
                        }
                        neteaseKey = key;

                        // 新版网易云要求二维码里带 chainId：v1_<sDeviceId>_web_login_<毫秒时间戳>
                        string chainId = BuildChainId();
                        bool[,] matrix = QrEncoder.Encode(
                            "https://music.163.com/login?codekey=" + key + "&chainId=" + chainId);
                        Raise(QrReady, RenderMatrix(matrix, 4, 3));
                        Raise(StatusChanged, "打开网易云音乐 App，扫一扫登录");
                        if (timer != null) timer.Start();
                    }
                }
            }
            catch (Exception exception)
            {
                active = false;
                Raise(Failed, "生成二维码失败：" + Explain(exception));
            }
        }

        async void Poll()
        {
            if (!active || string.IsNullOrEmpty(neteaseKey)) return;
            try
            {
                string url = "https://music.163.com/api/login/qrcode/client/login?type=3&key=" + Uri.EscapeDataString(neteaseKey);
                string body;
                var responseCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var pollRequest = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    pollRequest.Headers.Referrer = new Uri("https://music.163.com/");
                    if (!string.IsNullOrEmpty(neteaseCookieHeader))
                    {
                        pollRequest.Headers.TryAddWithoutValidation("Cookie", neteaseCookieHeader);
                    }
                    using (HttpResponseMessage pollResponse = await client.SendAsync(pollRequest))
                    {
                        body = await pollResponse.Content.ReadAsStringAsync();
                        // 803 的登录 cookie 在 Set-Cookie 里，自己收（容器关了）
                        System.Collections.Generic.IEnumerable<string> setCookies;
                        if (pollResponse.Headers.TryGetValues("Set-Cookie", out setCookies))
                        {
                            foreach (string line in setCookies) ParseSetCookie(line, responseCookies);
                        }
                    }
                }
                int code = ReadJsonInt(body, "code");
                Log("netease poll code=" + code + " key=" + (neteaseKey.Length > 8 ? neteaseKey.Substring(0, 8) : neteaseKey) + " setCookies=" + responseCookies.Count);

                if (code == 801)
                {
                    Raise(StatusChanged, "等待扫码…");
                }
                else if (code == 802)
                {
                    Raise(StatusChanged, "已扫码，请在手机上点确认");
                    // 已扫码：把轮询加快到 0.3 秒，尽量在手机端页面把 803 消费掉之前抢到
                    if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(300);
                }
                else if (code == 803)
                {
                    FinishNetEase(responseCookies);
                }
                else if (code == 8821)
                {
                    // 8821 = 网易云判定这张码失效 / 确认被拒（常见于登录保护）
                    Stop();
                    Raise(StatusChanged, "扫码确认被拒绝（8821），可以点「改用网页登录」");
                    Raise(Expired);
                }
                else
                {
                    Raise(StatusChanged, "等待扫码…");
                }
            }
            catch (Exception)
            {
                // 网络抖一下不当作失败，下一轮接着问
            }
        }

        // Folia 同款：v1_<sDeviceId>_web_login_<timestamp>
        static string BuildChainId()
        {
            string deviceId = "";
            try
            {
                var cookies = MusicCookieStore.Load("netease");
                if (cookies != null) cookies.TryGetValue("sDeviceId", out deviceId);
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(deviceId))
            {
                var random = new Random();
                deviceId = "unknown-" + random.Next(1000000);
            }
            long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            return "v1_" + deviceId + "_web_login_" + ms;
        }

        // Set-Cookie: NAME=VALUE; Path=/; Domain=...
        static void ParseSetCookie(string line, Dictionary<string, string> target)
        {
            if (string.IsNullOrEmpty(line) || target == null) return;
            int semicolon = line.IndexOf(';');
            string pair = semicolon >= 0 ? line.Substring(0, semicolon) : line;
            int equals = pair.IndexOf('=');
            if (equals <= 0) return;
            string name = pair.Substring(0, equals).Trim();
            string value = pair.Substring(equals + 1).Trim();
            if (name.Length == 0 || value.Length == 0) return;
            target[name] = value;
        }

        // responseCookies：803 响应 Set-Cookie 里解析出来的（关掉自动容器后只能自己收）
        void FinishNetEase(Dictionary<string, string> responseCookies)
        {
            Stop();
            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                CookieCollection collection = jar.GetCookies(new Uri("https://music.163.com/"));
                foreach (Cookie cookie in collection)
                {
                    if (cookie == null || string.IsNullOrEmpty(cookie.Value)) continue;
                    cookies[cookie.Name] = cookie.Value;
                }
            }
            catch (Exception) { }
            if (responseCookies != null)
            {
                foreach (KeyValuePair<string, string> pair in responseCookies)
                {
                    if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
                    cookies[pair.Key] = pair.Value;
                }
            }
            Log("netease 803 cookies=" + cookies.Count + " hasMusicU=" + cookies.ContainsKey("MUSIC_U"));

            if (!cookies.ContainsKey("MUSIC_U"))
            {
                Raise(Failed, "扫码成功但没拿到登录 cookie，请重试或改用网页登录。");
                return;
            }
            Raise(StatusChanged, "登录成功");
            Raise(Succeeded, cookies);
        }

        // ------------------------------------------------------------ 画二维码

        /// <summary>把模块矩阵画成位图：黑块白底 + quietZone 个模块的静区。</summary>
        internal static ImageSource RenderMatrix(bool[,] matrix, int modulePixels, int quietZone)
        {
            int size = matrix.GetLength(0);
            int total = (size + quietZone * 2) * modulePixels;
            int stride = total * 4;
            var pixels = new byte[total * stride];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;      // B
                pixels[i + 1] = 255;  // G
                pixels[i + 2] = 255;  // R
                pixels[i + 3] = 255;  // A
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!matrix[y, x]) continue;
                    int left = (x + quietZone) * modulePixels;
                    int top = (y + quietZone) * modulePixels;
                    for (int dy = 0; dy < modulePixels; dy++)
                    {
                        int rowStart = (top + dy) * stride + left * 4;
                        for (int dx = 0; dx < modulePixels; dx++)
                        {
                            pixels[rowStart + dx * 4] = 0;
                            pixels[rowStart + dx * 4 + 1] = 0;
                            pixels[rowStart + dx * 4 + 2] = 0;
                        }
                    }
                }
            }

            var bitmap = new WriteableBitmap(total, total, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, total, total), pixels, stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------ 小工具

        static string ReadJsonString(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string marker = "\"" + name + "\"";
            int at = json.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return "";
            int colon = json.IndexOf(':', at + marker.Length);
            if (colon < 0) return "";
            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return "";
            int end = json.IndexOf('"', start + 1);
            if (end < 0) return "";
            return json.Substring(start + 1, end - start - 1);
        }

        static int ReadJsonInt(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            string marker = "\"" + name + "\"";
            int at = json.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return -1;
            int colon = json.IndexOf(':', at + marker.Length);
            if (colon < 0) return -1;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\r' || json[i] == '\n')) i++;
            int start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            if (i <= start) return -1;
            int value;
            return int.TryParse(json.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }

        static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(空响应)";
            return text.Length <= 120 ? text : text.Substring(0, 120) + "…";
        }

        static string Explain(Exception exception)
        {
            if (exception == null) return "未知错误";
            if (exception is TaskCanceledException) return "请求超时，检查一下网络";
            if (exception is HttpRequestException && exception.InnerException != null) return exception.InnerException.Message;
            return exception.Message;
        }

        static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null) return;
            try { handler(value); } catch (Exception) { }
        }

        static void Raise(Action handler)
        {
            if (handler == null) return;
            try { handler(); } catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------------
    // 扫码登录拿到的 cookie 存在这里：data\music\cookies-<平台Id>.txt
    // 一行一个 name=value。下一步接歌单时要用它把登录态塞进 WebView2 的 profile，
    // 这样"扫码登录"和"网页登录"最终落到同一个地方。
    // 文件只在本机 data 目录，不进仓库、不打日志。
    // ---------------------------------------------------------------------
    internal static class MusicCookieStore
    {
        static string PathOf(string platformId)
        {
            return Path.Combine(AiConfigStore.DataDir, "music", "cookies-" + platformId + ".txt");
        }

        internal static void Save(string platformId, Dictionary<string, string> cookies)
        {
            if (string.IsNullOrEmpty(platformId) || cookies == null) return;
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                lines.Add("# 扫码登录拿到的 cookie（一行一个 name=value），只在本机");
                foreach (KeyValuePair<string, string> pair in cookies)
                {
                    if (string.IsNullOrEmpty(pair.Key)) continue;
                    lines.Add(pair.Key + "=" + pair.Value);
                }
                File.WriteAllLines(PathOf(platformId), lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal static Dictionary<string, string> Load(string platformId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(platformId)) return result;
            try
            {
                string path = PathOf(platformId);
                if (!File.Exists(path)) return result;
                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line == null ? "" : line.Trim();
                    if (text.Length == 0 || text.StartsWith("#")) continue;
                    int split = text.IndexOf('=');
                    if (split <= 0) continue;
                    result[text.Substring(0, split)] = text.Substring(split + 1);
                }
            }
            catch (Exception) { }
            return result;
        }
    }
}
