// ---------------------------------------------------------------------------
// 协议实现来源与免责（详见仓库根目录 THIRD-PARTY-NOTICES.txt 第五、六节）：
//   本文件是对第三方开源项目公开「协议事实」的独立 C# 重写，
//   不包含也不分发任何音乐内容、音源文件或解密密钥；
//   不做付费/加密绕过（无明文音源时直接跳过）；
//   与各音乐平台无关联，商标归各自权利人；使用时请遵守平台服务条款。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    /// <summary>
    /// 酷狗音乐歌单导入。
    ///
    /// 接口来自酷狗的网关（Folia 用的是同一套 kugoumusicapi 协议）：
    ///   歌单列表：POST https://gateway.kugou.com/v7/get_all_list        (x-router: cloudlist.service.kugou.com)
    ///   歌单歌曲：GET  https://gateway.kugou.com/pubsongs/v2/get_other_list_file_nofilt
    ///
    /// 签名：MD5(盐 + 按 key 排序的 k=v 串 + body + 盐)，盐 = OIlwieks28dk2k092lksi2UIkp。
    /// 登录信息（token / userid / dfid）从登录用的 WebView2 配置里读 cookie + localStorage。
    /// </summary>
    internal static class KugouApi
    {
        // Folia 用的是「概念版(lite)」参数：appid 3116 / clientver 11440 / 专用盐和公钥
        const string Salt = "LnT6xpN3khm36zse0QzvmgTZ3waWdRSA";
        const string Gateway = "https://gateway.kugou.com";
        const int AppId = 3116;
        const int ClientVer = 11440;

        internal sealed class KugouSession
        {
            public string Token = "";
            public string UserId = "";
            public string Dfid = "";
            public string Mid = "";
            public bool Ready { get { return !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(UserId); } }
        }

        static KugouSession cachedSession;

        // 设备 mid 必须全程一致（扫码时用的哪个，之后注册/请求也得用哪个）
        static string deviceMid;

        static string DevicePath { get { return Path.Combine(AiConfigStore.DataDir, "music", "kugou-device.txt"); } }

        static string DeviceMid
        {
            get
            {
                if (!string.IsNullOrEmpty(deviceMid)) return deviceMid;
                try { if (File.Exists(DevicePath)) deviceMid = File.ReadAllText(DevicePath).Trim(); } catch (Exception) { }
                if (string.IsNullOrEmpty(deviceMid))
                {
                    deviceMid = CalculateMid(Guid.NewGuid().ToString());
                    try { File.WriteAllText(DevicePath, deviceMid, new UTF8Encoding(false)); } catch (Exception) { }
                }
                return deviceMid;
            }
        }
        internal static string LastError = "";
        internal static string LastBody = "";

        /// <summary>缓存登录信息：每次点歌单不用重新开窗口读 cookie。</summary>
        internal static void GetSession(MusicPlatform platform, Action<KugouSession> done, Action<string> failed)
        {
            if (cachedSession != null && cachedSession.Ready)
            {
                if (done != null) done(cachedSession);
                return;
            }
            KugouSession saved = LoadSession();
            if (saved != null)
            {
                cachedSession = saved;
                if (done != null) done(saved);
                return;
            }
            ReadSession(platform, delegate(KugouSession session)
            {
                cachedSession = session;
                if (done != null) done(session);
            }, failed);
        }

        /// <summary>退出登录 / 换账号后清缓存。</summary>
        internal static void ResetSession()
        {
            cachedSession = null;
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch (Exception) { }
        }

        static string SessionPath { get { return Path.Combine(AiConfigStore.DataDir, "music", "kugou-session.txt"); } }

        /// <summary>扫码登录成功后写入会话（token/userid 由官方扫码接口返回），并落盘供下次启动使用。</summary>
        internal static void SetSession(string token, string userId)
        {
            cachedSession = new KugouSession { Token = token ?? "", UserId = userId ?? "", Dfid = "-", Mid = "-" };
            SaveSessionFile();
        }

        /// <summary>扫码登录后补设备号：网页 profile 里已有合法的 dfid / kg_mid。</summary>
        internal static async void FillDevice(MusicPlatform platform, Action done)
        {
            var window = new Window
            {
                Width = 800,
                Height = 600,
                WindowStyle = WindowStyle.None,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = System.Windows.Media.Brushes.Black
            };
            var view = new WebView2();
            window.Content = view;
            try
            {
                window.Show();
                string folder = Path.Combine(AiConfigStore.DataDir, "music", "webview2-" + platform.Id);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder);
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.Navigate("https://www.kugou.com/");
                await Task.Delay(3000);

                var cookies = await view.CoreWebView2.CookieManager.GetCookiesAsync("https://www.kugou.com/");
                string dfid = "";
                string mid = "";
                if (cookies != null)
                {
                    for (int i = 0; i < cookies.Count; i++)
                    {
                        var cookie = cookies[i];
                        if (cookie == null) continue;
                        string name = (cookie.Name ?? "").ToLowerInvariant();
                        if (name == "dfid") dfid = cookie.Value;
                        else if ((name == "kg_mid" || name == "mid") && mid.Length == 0) mid = cookie.Value;
                    }
                }
                if (cachedSession != null)
                {
                    if (!string.IsNullOrEmpty(dfid)) cachedSession.Dfid = dfid;
                    if (!string.IsNullOrEmpty(mid)) cachedSession.Mid = mid;
                    SaveSessionFile();
                }
            }
            catch (Exception) { }
            finally
            {
                try { view.Dispose(); } catch (Exception) { }
                try { window.Close(); } catch (Exception) { }
            }
            if (done != null) done();
        }

        static void SaveSessionFile()
        {
            if (cachedSession == null) return;
            try
            {
                File.WriteAllText(SessionPath,
                    "token=" + cachedSession.Token + Environment.NewLine +
                    "userid=" + cachedSession.UserId + Environment.NewLine +
                    "dfid=" + cachedSession.Dfid + Environment.NewLine +
                    "mid=" + cachedSession.Mid + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static KugouSession LoadSession()
        {
            try
            {
                if (!File.Exists(SessionPath)) return null;
                var session = new KugouSession();
                foreach (string line in File.ReadAllLines(SessionPath))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();
                    if (key == "token") session.Token = value;
                    else if (key == "userid") session.UserId = value;
                    else if (key == "dfid") session.Dfid = value;
                    else if (key == "mid") session.Mid = value;
                }
                return session.Ready ? session : null;
            }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- 扫码登录（官方接口）

        const string WebSalt = "NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
        const string SrcAppId = "2919";
        const string LoginBase = "https://login-user.kugou.com";

        static SortedDictionary<string, string> WebParams(SortedDictionary<string, string> extra)
        {
            long clientTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "dfid", "-" },
                { "mid", DeviceMid },
                { "uuid", "-" },
                { "appid", "1001" },
                { "clientver", "20489" },
                { "clienttime", clientTime.ToString() },
                { "srcappid", SrcAppId }
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> pair in extra) map[pair.Key] = pair.Value;
            }
            return map;
        }

        static string WebSign(SortedDictionary<string, string> map)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in map) builder.Append(pair.Key).Append('=').Append(pair.Value ?? "");
            string raw = WebSalt + builder + WebSalt;
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var text = new StringBuilder();
                foreach (byte b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        static string WebGet(string url, SortedDictionary<string, string> map)
        {
            map["signature"] = WebSign(map);
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    return client.DownloadString(BuildUrl(url, map));
                }
            }
            catch (Exception exception) { LastError = exception.Message; return null; }
        }

        /// <summary>取一个登录二维码：自己按 key 画码（官方返回的 qrcode_img 绑的是另一个 appid，扫出来的 token 网关不认）。</summary>
        internal static bool RequestLoginQr(out string qrcode, out System.Windows.Media.ImageSource image)
        {
            qrcode = "";
            image = null;
            var map = WebParams(new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "type", "1" },
                { "plat", "4" },
                { "qrcode_txt", "https://h5.kugou.com/apps/loginQRCode/html/index.html?appid=3116&" }
            });
            string raw = WebGet(LoginBase + "/v2/qrcode", map);
            if (string.IsNullOrEmpty(raw)) return false;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                qrcode = Text(data, "qrcode");
                if (string.IsNullOrEmpty(qrcode)) return false;
                bool[,] matrix = QrEncoder.Encode("https://h5.kugou.com/apps/loginQRCode/html/index.html?qrcode=" + qrcode);
                image = MusicQrLogin.RenderMatrix(matrix, 8, 4);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>轮询扫码状态：0 过期 1 等待 2 待确认 4 成功（带 token/userid）。</summary>
        internal static int PollLoginQr(string qrcode, out string token, out string userId)
        {
            token = "";
            userId = "";
            if (string.IsNullOrEmpty(qrcode)) return -1;
            // 轮询必须用 lite 的 appid 3116（取 key 时才用 1001），否则发出来的 token 网关不认
            var map = WebParams(new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "plat", "4" },
                { "appid", "3116" },
                { "qrcode", qrcode }
            });
            string raw = WebGet(LoginBase + "/v2/get_userinfo_qrcode", map);
            if (string.IsNullOrEmpty(raw)) return -1;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                int status = Int(data, "status");
                if (status == 4)
                {
                    token = Text(data, "token");
                    userId = Text(data, "userid");
                }
                return status;
            }
            catch (Exception) { return -1; }
        }

        // ---------------------------------------------------------------- 会话

        internal static async void ReadSession(MusicPlatform platform, Action<KugouSession> done, Action<string> failed)
        {
            var window = new Window
            {
                Width = 900,
                Height = 700,
                WindowStyle = WindowStyle.None,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = System.Windows.Media.Brushes.Black
            };
            var view = new WebView2();
            window.Content = view;

            bool finished = false;
            Action close = delegate
            {
                if (finished) return;
                finished = true;
                try { view.Dispose(); } catch (Exception) { }
                try { window.Close(); } catch (Exception) { }
            };

            try
            {
                window.Show();
                string folder = Path.Combine(AiConfigStore.DataDir, "music", "webview2-" + platform.Id);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder);
                await view.EnsureCoreWebView2Async(environment);

                view.CoreWebView2.Navigate("https://www.kugou.com/");
                await Task.Delay(4500);

                var session = new KugouSession();

                // HttpOnly 的 cookie 只有 CookieManager 能看到
                try
                {
                    var cookies = await view.CoreWebView2.CookieManager.GetCookiesAsync("https://www.kugou.com/");
                    if (cookies != null)
                    {
                        for (int i = 0; i < cookies.Count; i++)
                        {
                            var cookie = cookies[i];
                            if (cookie == null || string.IsNullOrEmpty(cookie.Value)) continue;
                            string name = (cookie.Name ?? "").ToLowerInvariant();
                            if (name == "token") session.Token = cookie.Value;
                            else if (name == "userid" || name == "user_id") session.UserId = cookie.Value;
                            else if (name == "dfid") session.Dfid = cookie.Value;
                            else if (name == "kg_mid" || name == "mid") { if (session.Mid.Length == 0) session.Mid = cookie.Value; }
                        }
                    }
                }
                catch (Exception) { }

                // localStorage 里可能还存着一份
                try
                {
                    string script =
                        "(function(){try{var ls={};for(var i=0;i<localStorage.length;i++){var k=localStorage.key(i);ls[k]=localStorage.getItem(k);}" +
                        "return JSON.stringify(ls);}catch(e){return '{}';}})()";
                    string raw = await view.CoreWebView2.ExecuteScriptAsync(script);
                    Dictionary<string, string> storage = ParseStorage(raw);
                    if (storage != null)
                    {
                        foreach (KeyValuePair<string, string> pair in storage)
                        {
                            string key = (pair.Key ?? "").ToLowerInvariant();
                            if (session.Token.Length == 0 && key.Contains("token")) session.Token = pair.Value;
                            if (session.UserId.Length == 0 && key.Contains("userid")) session.UserId = pair.Value;
                            if (session.Dfid.Length == 0 && key.Contains("dfid")) session.Dfid = pair.Value;
                        }
                    }
                }
                catch (Exception) { }

                close();
                if (!session.Ready)
                {
                    if (failed != null) failed("没读到酷狗登录信息（cookie 里没有 token/userid，可能登录态没种到 www.kugou.com）");
                    return;
                }
                if (done != null) done(session);
            }
            catch (Exception exception)
            {
                close();
                if (failed != null) failed("读取酷狗登录信息失败：" + exception.Message);
            }
        }

        static Dictionary<string, string> ParseStorage(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string json = raw;
            try
            {
                var outer = new JavaScriptSerializer().Deserialize<string>(raw);
                if (!string.IsNullOrEmpty(outer)) json = outer;
            }
            catch (Exception) { }

            try
            {
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (map == null) return null;
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in map)
                {
                    result[pair.Key] = Convert.ToString(pair.Value);
                }
                return result;
            }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- 设备注册（换 dfid）

        // 酷狗网关要求会话先注册设备：AES-CBC 加密设备信息、RSA(PKCS#1 v1.5) 包密钥，
        // 服务端校验后才给 dfid。以下是同一套协议（只取事实，代码自己写）。
        const string RegisterPublicKey =
            "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDECi0Np2UR87scwrvTr72L6oO01rBbbBPriSDFPxr3Z5syug0O24QyQO8bg27+0+4kBzTBTBOZ/WWU0WryL1JSXRTXLgFVxtzIY41Pe7lPOgsfTCn5kZcvKhYKJesKnnJDNr5/abvTGf+rHG3YRwsCHcQ08/q6ifSioBszvb3QiwIDAQAB";

        static bool RegisterDevice(KugouSession session)
        {
            if (session == null || !session.Ready) return false;
            try
            {
                // mid 由 DeviceMid 统一提供，注册前后保持一致
                string key = RandomKey(6);
                string md5 = Md5Hex(key);
                byte[] aesKey = Encoding.UTF8.GetBytes(md5.Substring(0, 16));
                byte[] iv = Encoding.UTF8.GetBytes(md5.Substring(16, 16));

                string deviceJson =
                    "{\"availableRamSize\":4983533568,\"availableRomSize\":48114719,\"availableSDSize\":48114717," +
                    "\"basebandVer\":\"\",\"batteryLevel\":100,\"batteryStatus\":3,\"brand\":\"Redmi\"," +
                    "\"buildSerial\":\"unknown\",\"device\":\"marble\",\"imei\":\"\",\"imsi\":\"\"," +
                    "\"manufacturer\":\"Xiaomi\",\"uuid\":\"" + Guid.NewGuid().ToString() + "\"," +
                    "\"accelerometer\":false,\"accelerometerValue\":\"\",\"gravity\":false,\"gravityValue\":\"\"," +
                    "\"gyroscope\":false,\"gyroscopeValue\":\"\",\"light\":false,\"lightValue\":\"\"," +
                    "\"magnetic\":false,\"magneticValue\":\"\",\"orientation\":false,\"orientationValue\":\"\"," +
                    "\"pressure\":false,\"pressureValue\":\"\",\"step_counter\":false,\"step_counterValue\":\"\"," +
                    "\"temperature\":false,\"temperatureValue\":\"\"}";

                string body = AesEncryptBase64(deviceJson, aesKey, iv);
                string payload = "{\"aes\":" + Quote(key) + ",\"uid\":" + Quote(session.UserId) +
                                 ",\"token\":" + Quote(session.Token) + "}";
                string p = RsaEncryptHex(payload);

                var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    { "part", "1" },
                    { "platid", "1" },
                    { "p", p }
                };

                byte[] response = PostRaw("https://userservice.kugou.com/risk/v2/r_register_dev", query, body, session);
                if (response == null || response.Length == 0)
                {
                    LastError = "register-dev: no response (" + LastError + ")";
                    return false;
                }
                LastError = "register-dev raw(" + response.Length + "): " + (response.Length > 60 ? Encoding.UTF8.GetString(response, 0, 60) : Encoding.UTF8.GetString(response, 0, response.Length));

                string plain = AesDecryptBase64(Convert.ToBase64String(response), aesKey, iv);
                if (string.IsNullOrEmpty(plain)) return false;

                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(plain);
                var data = Dig(root, "data");
                string dfid = Text(data, "dfid");
                if (string.IsNullOrEmpty(dfid))
                {
                    LastError = "register-dev plain: " + (plain.Length > 120 ? plain.Substring(0, 120) : plain);
                    return false;
                }

                session.Dfid = dfid;
                SaveSessionFile();
                return true;
            }
            catch (Exception exception)
            {
                LastError = "register-dev: " + exception.Message;
                return false;
            }
        }

        static string RandomKey(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var builder = new StringBuilder();
            for (int i = 0; i < length; i++) builder.Append(chars[random.Next(chars.Length)]);
            return builder.ToString();
        }

        static string Md5Hex(string text)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder();
                foreach (byte b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        // calculateMid：MD5 十六进制转十进制（大整数）
        static string CalculateMid(string text)
        {
            string hex = Md5Hex(text);
            var value = System.Numerics.BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
            return value.ToString();
        }

        static string AesEncryptBase64(string plain, byte[] key, byte[] iv)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.KeySize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] data = Encoding.UTF8.GetBytes(plain);
                    return Convert.ToBase64String(encryptor.TransformFinalBlock(data, 0, data.Length));
                }
            }
        }

        static string AesDecryptBase64(string base64, byte[] key, byte[] iv)
        {
            try
            {
                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.KeySize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] data = Convert.FromBase64String(base64);
                        byte[] plain = decryptor.TransformFinalBlock(data, 0, data.Length);
                        return Encoding.UTF8.GetString(plain);
                    }
                }
            }
            catch (Exception) { return ""; }
        }

        static string RsaEncryptHex(string plain)
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(ParseSpki(Convert.FromBase64String(RegisterPublicKey)));
            byte[] encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(plain), RSAEncryptionPadding.Pkcs1);
            var builder = new StringBuilder();
            foreach (byte b in encrypted) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        // 解析 SubjectPublicKeyInfo（.NET Framework 4.8 没有 ImportSubjectPublicKeyInfo）
        static RSAParameters ParseSpki(byte[] der)
        {
            int pos = 0;
            if (der[pos++] != 0x30) throw new InvalidOperationException("spki: not sequence");
            ReadDerLength(der, ref pos);

            if (der[pos++] != 0x30) throw new InvalidOperationException("spki: algorithm");
            int algLen = ReadDerLength(der, ref pos);
            pos += algLen;   // ReadDerLength 后 pos 已在内容起始处

            if (der[pos++] != 0x03) throw new InvalidOperationException("spki: bitstring");
            ReadDerLength(der, ref pos);
            pos++;  // unused bits

            if (der[pos++] != 0x30) throw new InvalidOperationException("spki: rsa sequence");
            ReadDerLength(der, ref pos);

            if (der[pos++] != 0x02) throw new InvalidOperationException("spki: modulus");
            int modLen = ReadDerLength(der, ref pos);
            byte[] modulus = new byte[modLen];
            Array.Copy(der, pos, modulus, 0, modLen);
            pos += modLen;

            if (der[pos++] != 0x02) throw new InvalidOperationException("spki: exponent");
            int expLen = ReadDerLength(der, ref pos);
            byte[] exponent = new byte[expLen];
            Array.Copy(der, pos, exponent, 0, expLen);

            return new RSAParameters { Modulus = TrimLeadingZeros(modulus), Exponent = TrimLeadingZeros(exponent) };
        }

        static byte[] TrimLeadingZeros(byte[] data)
        {
            int start = 0;
            while (start < data.Length - 1 && data[start] == 0) start++;
            if (start == 0) return data;
            byte[] result = new byte[data.Length - start];
            Array.Copy(data, start, result, 0, result.Length);
            return result;
        }

        static int ReadDerLength(byte[] der, ref int pos)
        {
            int first = der[pos++];
            if (first < 0x80) return first;
            int count = first & 0x7F;
            int length = 0;
            for (int i = 0; i < count; i++) length = (length << 8) | der[pos++];
            return length;
        }

        static int LengthBytes(int length)
        {
            if (length < 0x80) return 1;
            int bytes = 0;
            while (length > 0)
            {
                bytes++;
                length >>= 8;
            }
            return bytes + 1;
        }

        static byte[] PostRaw(string baseUrl, SortedDictionary<string, string> query, string body, KugouSession session)
        {
            long clientTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var all = WithDefaults(session, clientTime);
            foreach (KeyValuePair<string, string> pair in query) all[pair.Key] = pair.Value;
            string signature = Sign(all, body);
            var signed = new SortedDictionary<string, string>(all, StringComparer.Ordinal);
            signed["signature"] = signature;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi";
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    client.Headers[HttpRequestHeader.Cookie] = CookieHeader(session);
                    ApplyDeviceHeaders(client, session, clientTime);
                    return client.UploadData(BuildUrl(baseUrl, signed), "POST", Encoding.UTF8.GetBytes(body));
                }
            }
            catch (Exception exception) { LastError = exception.Message; return null; }
        }

        // ---------------------------------------------------------------- 歌单

        internal static List<MusicPlaylistInfo> GetPlaylists(KugouSession session)
        {
            // 网关要求设备注册：没 dfid 先换一个
            if (session != null && (string.IsNullOrEmpty(session.Dfid) || session.Dfid == "-"))
            {
                RegisterDevice(session);
            }

            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "plat", "1" },
                { "userid", session.UserId },
                { "token", session.Token }
            };
            string body = "{\"userid\":" + Quote(session.UserId) + ",\"token\":" + Quote(session.Token) +
                          ",\"total_ver\":979,\"type\":2,\"page\":1,\"pagesize\":50}";

            string raw = Post(Gateway + "/v7/get_all_list", query, body, "cloudlist.service.kugou.com", session);
            if (string.IsNullOrEmpty(raw)) return new List<MusicPlaylistInfo>();

            var list = new List<MusicPlaylistInfo>();
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                var info = AsList(Dig(data, "info"));
                if (info == null) { LastError = "info null; data keys=" + DataKeys(data); return list; }
                foreach (object item in info)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    string id = Text(row, "global_collection_id");
                    if (string.IsNullOrEmpty(id)) id = Text(row, "listid");
                    if (string.IsNullOrEmpty(id)) continue;
                    list.Add(new MusicPlaylistInfo
                    {
                        Id = id,
                        DirId = Text(row, "listid"),
                        Name = FirstNonEmpty(Text(row, "listname"), Text(row, "name")),
                        Count = Int(row, "songcount"),
                        Url = Text(row, "list_pic")
                    });
                }
            }
            catch (Exception exception) { LastError = "parse: " + exception.Message; }
            LastError = "rows=" + list.Count + (LastError.Length > 0 ? " " + LastError : "");
            return list;
        }

        static string DataKeys(object data)
        {
            var map = data as Dictionary<string, object>;
            if (map == null) return "not-dict";
            var keys = new List<string>();
            foreach (var pair in map) keys.Add(pair.Key);
            return string.Join(",", keys.ToArray());
        }

        internal static List<MusicSongInfo> GetSongs(KugouSession session, MusicPlaylistInfo playlist)
        {
            var songs = new List<MusicSongInfo>();
            if (playlist == null) return songs;

            for (int page = 0; page < 20; page++)
            {
                var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    { "area_code", "1" },
                    { "begin_idx", (page * 300).ToString() },
                    { "plat", "1" },
                    { "type", "1" },
                    { "mode", "1" },
                    { "personal_switch", "1" },
                    { "extend_fields", "abtags,hot_cmt,popularization" },
                    { "pagesize", "300" },
                    { "global_collection_id", playlist.Id }
                };
                string raw = Get(Gateway + "/pubsongs/v2/get_other_list_file_nofilt", query, session);
                if (string.IsNullOrEmpty(raw))
                {
                    LastError = "songs raw empty: " + LastError;
                    break;
                }

                int before = songs.Count;
                try
                {
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                    var data = Dig(root, "data");
                    // 酷狗这个接口把歌放在 data.songs（不是 data.info）
                    var info = AsList(Dig(data, "songs"));
                    if (info == null) info = AsList(Dig(data, "info"));
                    if (info == null)
                    {
                        LastError = "songs array null: " + (raw.Length > 120 ? raw.Substring(0, 120) : raw);
                        break;
                    }
                    foreach (object item in info)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        string hash = Text(row, "hash");
                        if (string.IsNullOrEmpty(hash)) continue;

                        string display = Text(row, "name");
                        if (string.IsNullOrEmpty(display)) display = Text(row, "filename");
                        string artists = SingerNames(row);
                        string title = TitleFromFilename(display);
                        if (string.IsNullOrEmpty(artists)) artists = ArtistFromFilename(display);

                        songs.Add(new MusicSongInfo
                        {
                            Mid = hash,
                            Title = title,
                            Artists = artists,
                            Duration = FormatDuration(Int(row, "timelen") / 1000),
                            AlbumMid = FirstNonEmpty(CoverUrl(Text(row, "cover")), Text(row, "album_id"))
                        });
                    }
                }
                catch (Exception exception)
                {
                    LastError = "songs parse: " + exception.Message;
                    break;
                }
                if (songs.Count == before) break;
            }
            return songs;
        }

        static string TitleFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return "";
            int dash = filename.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? filename.Substring(dash + 3) : filename;
        }

        static string ArtistFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return "";
            int dash = filename.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? filename.Substring(0, dash) : "";
        }

        static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "";
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        // ---------------------------------------------------------------- HTTP + 签名

        // 自检入口：和 node 端算出的参考值比对签名算法
        internal static string SignSelfTest()
        {
            var q = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "plat", "1" }, { "userid", "1778956035" }, { "token", "TOKEN" },
                { "dfid", "DFID" }, { "mid", "MID" }, { "uuid", "-" },
                { "appid", "1005" }, { "clientver", "20489" }, { "clienttime", "1762000000" }
            };
            string body = "{\"userid\":\"1778956035\",\"token\":\"TOKEN\",\"total_ver\":979,\"type\":2,\"page\":1,\"pagesize\":50}";
            return Sign(q, body);
        }

        static string Sign(SortedDictionary<string, string> query, string body)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in query)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value ?? "");
            }
            string raw = Salt + builder + (body ?? "") + Salt;
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var text = new StringBuilder();
                foreach (byte b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        static SortedDictionary<string, string> WithDefaults(KugouSession session, long clientTime)
        {
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "dfid", string.IsNullOrEmpty(session.Dfid) ? "-" : session.Dfid },
                { "mid", DeviceMid },
                { "uuid", "-" },
                { "appid", AppId.ToString() },
                { "clientver", ClientVer.ToString() },
                { "clienttime", clientTime.ToString() }
            };
            if (!string.IsNullOrEmpty(session.Token)) query["token"] = session.Token;
            if (!string.IsNullOrEmpty(session.UserId)) query["userid"] = session.UserId;
            return query;
        }

        static string BuildUrl(string baseUrl, SortedDictionary<string, string> query)
        {
            var builder = new StringBuilder(baseUrl);
            builder.Append(baseUrl.IndexOf('?') >= 0 ? "&" : "?");
            bool first = true;
            foreach (KeyValuePair<string, string> pair in query)
            {
                if (!first) builder.Append('&');
                first = false;
                builder.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? ""));
            }
            return builder.ToString();
        }

        static string Get(string baseUrl, SortedDictionary<string, string> query, KugouSession session)
        {
            return Get(baseUrl, query, session, null);
        }

        static string Get(string baseUrl, SortedDictionary<string, string> query, KugouSession session, string router)
        {
            long clientTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var all = WithDefaults(session, clientTime);
            foreach (KeyValuePair<string, string> pair in query) all[pair.Key] = pair.Value;
            string signature = Sign(all, "");
            var signed = new SortedDictionary<string, string>(all, StringComparer.Ordinal);
            signed["signature"] = signature;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "KuGou2012-9020-Web";
                    client.Headers[HttpRequestHeader.Cookie] = CookieHeader(session);
                    if (!string.IsNullOrEmpty(router)) client.Headers["x-router"] = router;
                    ApplyDeviceHeaders(client, session, clientTime);
                    // 按 UTF-8 字节读：WebClient 默认按系统 ANSI 解码，中文会乱码并破坏 JSON
                    byte[] bytes = client.DownloadData(BuildUrl(baseUrl, signed));
                    string body = Encoding.UTF8.GetString(bytes);
                    return body;
                }
            }
                catch (Exception exception) { LastError = "get: " + exception.Message; return null; }
        }

        static string Post(string baseUrl, SortedDictionary<string, string> query, string body, string router, KugouSession session)
        {
            long clientTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var all = WithDefaults(session, clientTime);
            foreach (KeyValuePair<string, string> pair in query) all[pair.Key] = pair.Value;
            string signature = Sign(all, body);
            var signed = new SortedDictionary<string, string>(all, StringComparer.Ordinal);
            signed["signature"] = signature;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "KuGou2012-9020-Web";
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    client.Headers["x-router"] = router;
                    client.Headers[HttpRequestHeader.Cookie] = CookieHeader(session);
                    ApplyDeviceHeaders(client, session, clientTime);
                    byte[] responseBytes = client.UploadData(BuildUrl(baseUrl, signed), "POST", Encoding.UTF8.GetBytes(body));
                    string response = Encoding.UTF8.GetString(responseBytes);
                    LastBody = response;
                    return response;
                }
            }
            catch (Exception exception) { LastError = exception.Message; return null; }
        }

        static void ApplyDeviceHeaders(WebClient client, KugouSession session, long clientTime)
        {
            client.Headers["dfid"] = string.IsNullOrEmpty(session.Dfid) ? "-" : session.Dfid;
            client.Headers["clienttime"] = clientTime.ToString();
            client.Headers["mid"] = string.IsNullOrEmpty(session.Mid) ? "-" : session.Mid;
            client.Headers["kg-rc"] = "1";
            client.Headers["kg-thash"] = "5d816a0";
            client.Headers["kg-rec"] = "1";
            client.Headers["kg-rf"] = "B9EDA08A64250DEFFBCADDEE00F8F25F";
        }

        static string CookieHeader(KugouSession session)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(session.UserId)) builder.Append("userid=").Append(session.UserId).Append("; ");
            if (!string.IsNullOrEmpty(session.Token)) builder.Append("token=").Append(session.Token);
            return builder.ToString();
        }

        // ---------------------------------------------------------------- 播放地址

        // 取播放地址（v5/url）：返回 http mp3 直链；空 = 没版权/VIP
        internal static string GetSongUrl(KugouSession session, string hash)
        {
            if (session == null || string.IsNullOrEmpty(hash)) return "";
            string low = hash.ToLowerInvariant();
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "album_id", "0" },
                { "area_code", "1" },
                { "hash", low },
                { "ssa_flag", "is_fromtrack" },
                { "version", "11430" },
                { "page_id", "967177915" },
                { "quality", "128" },
                { "album_audio_id", "0" },
                { "behavior", "play" },
                { "pid", "411" },
                { "cmd", "26" },
                { "pidversion", "3001" },
                { "IsFreePart", "0" },
                { "ppage_id", "356753938,823673182,967485191" },
                { "cdnBackup", "1" },
                { "module", "" },
                { "key", Md5Hex(low + "185672dd44712f60bb1736df5a377e82" + AppId + DeviceMid + session.UserId) }
            };
            string raw = Get(Gateway + "/v5/url", query, session, "trackercdn.kugou.com");
            if (string.IsNullOrEmpty(raw))
            {
                LastError = "song url empty: " + LastError;
                return "";
            }
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                string url = FirstUrl(AsList(Dig(root, "url")));
                if (string.IsNullOrEmpty(url)) url = FirstUrl(AsList(Dig(root, "backupUrl")));
                if (string.IsNullOrEmpty(url)) LastError = "song url null: " + (raw.Length > 160 ? raw.Substring(0, 160) : raw);
                return url;
            }
            catch (Exception exception) { LastError = "song url parse: " + exception.Message; return ""; }
        }

        static string FirstUrl(System.Collections.IEnumerable list)
        {
            if (list == null) return "";
            foreach (object item in list)
            {
                // 有的版本 url 是字符串数组，有的是 {url:...} 对象
                var text = item as string;
                if (!string.IsNullOrEmpty(text)) return text;
                var map = item as Dictionary<string, object>;
                if (map != null)
                {
                    string inner = Text(map, "url");
                    if (!string.IsNullOrEmpty(inner)) return inner;
                }
            }
            return "";
        }

        // ---------------------------------------------------------------- 歌词

        // 歌词：lyrics.kugou.com 搜索 + 下载。返回标准 LRC 文本，空 = 没有
        internal static string GetLyrics(KugouSession session, string hash, int durationSeconds)
        {
            if (string.IsNullOrEmpty(hash)) return "";
            var search = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "album_audio_id", "0" },
                { "appid", AppId.ToString() },
                { "clientver", ClientVer.ToString() },
                { "duration", (durationSeconds > 0 ? durationSeconds : 0).ToString() },
                { "hash", hash.ToLowerInvariant() },
                { "keyword", "" },
                { "lrctxt", "1" },
                { "man", "no" }
            };
            // 这两个歌词接口「只签这些参数」，不带 dfid/mid 等默认项
            search["signature"] = Sign(search, "");
            string searchRaw = HttpGet("https://lyrics.kugou.com/v1/search", search);
            if (string.IsNullOrEmpty(searchRaw))
            {
                LastError = "lyric search empty: " + LastError;
                return "";
            }
            string id = "";
            string accessKey = "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(searchRaw);
                var candidates = AsList(Dig(root, "candidates"));
                if (candidates != null)
                {
                    foreach (object item in candidates)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        id = Text(row, "id");
                        accessKey = Text(row, "accesskey");
                        break;
                    }
                }
            }
            catch (Exception exception) { LastError = "lyric search parse: " + exception.Message; return ""; }
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accessKey)) return "";

            var download = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "ver", "1" },
                { "client", "android" },
                { "id", id },
                { "accesskey", accessKey },
                { "fmt", "lrc" },
                { "charset", "utf8" }
            };
            download["signature"] = Sign(download, "");
            string lyricRaw = HttpGet("https://lyrics.kugou.com/download", download);
            if (string.IsNullOrEmpty(lyricRaw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(lyricRaw);
                string content = Text(root, "content");
                if (string.IsNullOrEmpty(content)) return "";
                // content 是 base64 的 LRC 文本（fmt=lrc 时 contenttype 为 1）
                return Encoding.UTF8.GetString(Convert.FromBase64String(content));
            }
            catch (Exception exception) { LastError = "lyric parse: " + exception.Message; return ""; }
        }

        static string HttpGet(string url, SortedDictionary<string, string> query)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi";
                    byte[] bytes = client.DownloadData(BuildUrl(url, query));
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch (Exception exception) { LastError = "http: " + exception.Message; return null; }
        }

        // ---------------------------------------------------------------- 解析小工具

        // singerinfo: [{name:quot;歌手quot;}]
        static string SingerNames(Dictionary<string, object> row)
        {
            var list = AsList(Dig(row, "singerinfo"));
            if (list == null) return Text(row, "singername");
            var names = new List<string>();
            foreach (object item in list)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                string name = Text(entry, "name");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return string.Join(" / ", names.ToArray());
        }

        // 酷狗封面是 http://imge.kugou.com/stdmusic/{size}/... 这种，把 {size} 换成 480
        static string CoverUrl(string cover)
        {
            if (string.IsNullOrEmpty(cover)) return "";
            return cover.Replace("{size}", "480");
        }

        static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? (b ?? "") : a;
        }

        static string Quote(string text)
        {
            if (text == null) return "\"\"";
            return new JavaScriptSerializer().Serialize(text);
        }

        // JavaScriptSerializer 把数组解析成 object[]，不是 ArrayList
        static System.Collections.IEnumerable AsList(object node)
        {
            if (node is System.Collections.ArrayList) return (System.Collections.ArrayList)node;
            if (node is object[]) return (object[])node;
            return null;
        }

        static object Dig(object node, string key)
        {
            var map = node as Dictionary<string, object>;
            if (map == null) return null;
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        static string Text(object node, string key)
        {
            object value = Dig(node, key);
            return value == null ? "" : Convert.ToString(value);
        }

        static int Int(object node, string key)
        {
            int parsed;
            int.TryParse(Text(node, key), out parsed);
            return parsed;
        }
    }
}
