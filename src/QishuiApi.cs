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
using System.Web.Script.Serialization;

namespace IslandShot
{
    /// <summary>
    /// 汽水音乐（抖音系）：车机版 LunaOpenSDK 协议。扫码登录 + EC 密钥 + 每请求 guard 签名。
    /// </summary>
    internal static class QishuiApi
    {
        const string ClientKey = "aw4bbxfujo741d09";
        const string Prefix = "/api/luna/v1/platform";
        const string UserAgent = "LunaOpenSDK/1.0.10";
        const string ClientToken = "clt.luna.is.number.one";
        const string Aide = "1128";

        static readonly string[] Scopes =
        {
            "user_info", "luna.openapi.platform.interaction", "luna.openapi.platform.me_tab",
            "luna.commerce.openapi_platform.vip_info"
        };

        internal static string LastError = "";
        internal static string LastBody = "";

        // ---------------------------------------------------------------- 会话

        internal sealed class QishuiSession
        {
            internal string UserId = "";
            internal string OpenId = "";
            internal string Ticket = "";
            internal string GuardKey = "";      // PKCS#8 私钥（base64）
            internal string GuardCert = "";
            internal string GuardTsSign = "";
            internal string AccessCode = "";
            internal string DeviceId = "";
            internal long ExpiresAt;
        }

        static QishuiSession session;

        internal static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "qishui.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        static string SessionPath { get { return Path.Combine(AiConfigStore.DataDir, "music", "qishui-session.txt"); } }
        static string DevicePath { get { return Path.Combine(AiConfigStore.DataDir, "music", "qishui-device.txt"); } }

        static string DeviceId
        {
            get
            {
                try
                {
                    if (File.Exists(DevicePath))
                    {
                        string existing = File.ReadAllText(DevicePath).Trim();
                        if (existing.Length == 19) return existing;
                    }
                }
                catch (Exception) { }

                // 19 位数字：首位非 0
                var random = new Random();
                var builder = new StringBuilder();
                builder.Append(random.Next(1, 10));
                for (int i = 1; i < 19; i++) builder.Append(random.Next(0, 10));
                string value = builder.ToString();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DevicePath));
                    File.WriteAllText(DevicePath, value, new UTF8Encoding(false));
                }
                catch (Exception) { }
                return value;
            }
        }

        internal static void ResetSession()
        {
            session = null;
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch (Exception) { }
        }

        static QishuiSession LoadSession()
        {
            if (session != null) return session;
            try
            {
                if (!File.Exists(SessionPath)) return null;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(SessionPath))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    values[line.Substring(0, equals)] = line.Substring(equals + 1);
                }
                if (!values.ContainsKey("ticket") || !values.ContainsKey("guardKey")) return null;
                var loaded = new QishuiSession
                {
                    UserId = Get(values, "userId"),
                    OpenId = Get(values, "openId"),
                    Ticket = Get(values, "ticket"),
                    GuardKey = Get(values, "guardKey"),
                    GuardCert = Get(values, "guardCert"),
                    GuardTsSign = Get(values, "guardTsSign"),
                    AccessCode = Get(values, "accessCode"),
                    DeviceId = Get(values, "deviceId"),
                    ExpiresAt = ParseLong(Get(values, "expiresAt"))
                };
                if (string.IsNullOrEmpty(loaded.DeviceId)) loaded.DeviceId = DeviceId;
                session = loaded;
                return session;
            }
            catch (Exception) { return null; }
        }

        static void SaveSession(QishuiSession value)
        {
            session = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath));
                var lines = new List<string>
                {
                    "userId=" + value.UserId,
                    "openId=" + value.OpenId,
                    "ticket=" + value.Ticket,
                    "guardKey=" + value.GuardKey,
                    "guardCert=" + value.GuardCert,
                    "guardTsSign=" + value.GuardTsSign,
                    "accessCode=" + value.AccessCode,
                    "deviceId=" + value.DeviceId,
                    "expiresAt=" + value.ExpiresAt
                };
                File.WriteAllLines(SessionPath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal static bool HasSession()
        {
            QishuiSession value = LoadSession();
            return value != null && !string.IsNullOrEmpty(value.Ticket);
        }

        // ---------------------------------------------------------------- 登录

        /// <summary>取一张登录二维码：返回轮询用的 token 和 PNG 字节。</summary>
        internal static bool BeginLogin(out string token, out byte[] png)
        {
            token = "";
            png = null;
            try
            {
                string url = "https://open.douyin.com/oauth/get_qrcode/?" + QrQuery(false, "", "");
                string raw = HttpGet(url, null);
                if (string.IsNullOrEmpty(raw)) { LastError = "qr: " + LastError; return false; }
                LastBody = raw;
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                token = Text(data, "token");
                string image = Text(data, "qrcode");
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(image))
                {
                    LastError = "qr: no token/image " + Shorten(raw);
                    return false;
                }
                png = Convert.FromBase64String(image);
                return true;
            }
            catch (Exception exception)
            {
                LastError = "qr: " + exception.Message;
                return false;
            }
        }

        internal enum QrState { Waiting, Scanned, Confirmed, Expired, Refused, Failed }

        /// <summary>轮询扫码状态；Confirmed 时给出 auth_code。</summary>
        internal static QrState CheckLogin(string token, out string authCode)
        {
            authCode = "";
            try
            {
                long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                string url = "https://open.douyin.com/oauth/check_qrcode/?" + QrQuery(true, token, ms.ToString());
                string raw = HttpGet(url, null);
                if (string.IsNullOrEmpty(raw)) return QrState.Failed;
                LastBody = raw;
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                string status = Text(data, "status");
                if (status == "new") return QrState.Waiting;
                if (status == "scanned") return QrState.Scanned;
                if (status == "refused") return QrState.Refused;
                if (status == "expired") return QrState.Expired;
                if (status == "confirmed")
                {
                    authCode = FirstNonEmpty(Text(data, "code"), Text(data, "auth_code"));
                    return string.IsNullOrEmpty(authCode) ? QrState.Failed : QrState.Confirmed;
                }
                LastError = "check: unknown status " + status + " " + Shorten(raw);
                return QrState.Failed;
            }
            catch (Exception exception)
            {
                LastError = "check: " + exception.Message;
                return QrState.Failed;
            }
        }

        /// <summary>用 auth_code 换 access_code，再换 ticket（会生成一对新 EC 密钥并做 CSR）。</summary>
        internal static bool FinishLogin(string authCode)
        {
            try
            {
                string device = DeviceId;
                // 1) access_code
                var accessQuery = CommonParams(device);
                accessQuery["auth_code"] = authCode;
                string accessUrl = "https://open.qishui.com" + Prefix + "/access/code/get/?" + BuildQuery(accessQuery);
                string accessRaw = HttpGet(accessUrl, null);
                Log("access code http len=" + (accessRaw == null ? 0 : accessRaw.Length) + " err=" + LastError);
                if (string.IsNullOrEmpty(accessRaw))
                {
                    LastError = "access code: " + LastError;
                    return false;
                }
                LastBody = accessRaw;
                var accessRoot = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(accessRaw);
                var accessData = Dig(accessRoot, "data");
                if (accessData == null) accessData = accessRoot;
                var inner = Dig(accessData, "data");
                if (inner != null) accessData = inner;
                string accessCode = Text(accessData, "access_code");
                Log("access code=" + (string.IsNullOrEmpty(accessCode) ? "<empty>" : accessCode.Substring(0, Math.Min(10, accessCode.Length))) + " head=" + Shorten(accessRaw));
                string openId = FirstNonEmpty(Text(accessData, "open_id"), Text(accessData, "open_uid"));
                long expires = ParseLong(Text(accessData, "expires_time"));
                if (string.IsNullOrEmpty(accessCode))
                {
                    LastError = "access code missing: " + Shorten(accessRaw);
                    return false;
                }

                // 2) 新密钥 + CSR
                string guardKey;
                string csrBase64;
                if (!CreateGuard(out guardKey, out csrBase64))
                {
                    LastError = "guard: " + LastError;
                    return false;
                }

                Log("csr ready len=" + (csrBase64 == null ? 0 : csrBase64.Length));
                // 3) 换 ticket
                string ticketUrl = "https://open-client.douyin.com/passport/open/get_access_ticket/"
                    + "?aid=" + Aide + "&client_key=" + ClientKey + "&device_platform=android";
                var form = new Dictionary<string, string>
                {
                    { "access_code", accessCode },
                    { "open_id", openId ?? "" }
                };
                var headers = new Dictionary<string, string>
                {
                    { "bd-ticket-guard-version", "1" },
                    { "bd-ticket-guard-client-csr", csrBase64 }
                };
                string ticketRaw;
                Dictionary<string, string> responseHeaders;
                string ticketBody = HttpPostForm(ticketUrl, form, headers, out responseHeaders, out ticketRaw);
                if (ticketBody == null)
                {
                    LastError = "ticket: " + LastError;
                    return false;
                }
                string serverData = "";
                if (responseHeaders != null && responseHeaders.ContainsKey("bd-ticket-guard-server-data"))
                {
                    serverData = responseHeaders["bd-ticket-guard-server-data"];
                }
                Log("ticket http len=" + (ticketBody == null ? 0 : ticketBody.Length) + " guardHeader=" + (string.IsNullOrEmpty(serverData) ? "<none>" : "yes") + " err=" + LastError + " head=" + Shorten(ticketBody));
                if (string.IsNullOrEmpty(serverData))
                {
                    LastError = "ticket: no guard header " + Shorten(ticketBody);
                    return false;
                }
                var guardJson = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(serverData)));
                string ticket = Text(guardJson, "ticket");
                string tsSign = Text(guardJson, "ts_sign");
                string clientCert = Text(guardJson, "client_cert");
                if (clientCert != null && clientCert.Contains("-----BEGIN"))
                {
                    clientCert = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientCert));
                }
                if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(clientCert))
                {
                    LastError = "ticket: incomplete " + Shorten(serverData);
                    return false;
                }

                // 4) 账号信息
                var value = new QishuiSession
                {
                    UserId = "",
                    OpenId = openId,
                    Ticket = ticket,
                    GuardKey = guardKey,
                    GuardCert = clientCert,
                    GuardTsSign = tsSign,
                    AccessCode = accessCode,
                    DeviceId = device,
                    ExpiresAt = expires > 1000000000000 ? expires : (expires > 1000000000 ? expires * 1000 : 0)
                };
                SaveSession(value);
                Log("ticket=" + (ticket == null ? 0 : ticket.Length) + " cert=" + (clientCert == null ? 0 : clientCert.Length));
                string userId = FetchUserId();
                Log("me/info userId=" + (userId ?? "<none>") + " err=" + LastError + " body=" + Shorten(LastBody));
                if (string.IsNullOrEmpty(userId))
                {
                    LastError = "me/info: " + LastError;
                    return false;
                }
                value.UserId = userId;
                SaveSession(value);
                return true;
            }
            catch (Exception exception)
            {
                LastError = "finish: " + exception.Message;
                return false;
            }
        }

        static string FetchUserId()
        {
            string raw = Platform("/me/info/", new Dictionary<string, object>());
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                var info = Dig(data, "my_info");
                if (info == null) info = data;
                return Text(info, "id");
            }
            catch (Exception exception) { LastError = "me/info parse: " + exception.Message; return ""; }
        }

        // ---------------------------------------------------------------- 数据

        internal static List<MusicPlaylistInfo> GetPlaylists()
        {
            var list = new List<MusicPlaylistInfo>();
            QishuiSession value = LoadSession();
            if (value == null) { LastError = "no session"; return list; }

            string cursor = "";
            for (int page = 0; page < 20; page++)
            {
                var body = new Dictionary<string, object>
                {
                    { "count", 100 },
                    { "cursor", cursor },
                    { "item_types", new object[] { "playlist" } }
                };
                string raw = Platform("/me/playlist/", body);
                if (string.IsNullOrEmpty(raw)) break;
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var data = Dig(root, "data");
                string errNo = Text(root, "err_no");
                if (!string.IsNullOrEmpty(errNo) && errNo != "0") { LastError = "err_no=" + errNo + " " + Text(root, "err_msg"); break; }
                if (data == null) break;
                var items = AsList(Dig(data, "playlists"));
                if (items == null) break;
                foreach (object item in items)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    var baseInfo = Dig(row, "base_info");
                    if (baseInfo == null) baseInfo = row;
                    string id = Text(baseInfo, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    list.Add(new MusicPlaylistInfo
                    {
                        Id = id,
                        DirId = Text(baseInfo, "type"),
                        Name = Text(baseInfo, "title"),
                        Count = 0,
                        Url = CoverOf(row)
                    });
                }
                string next = Text(data, "next_cursor");
                if (string.IsNullOrEmpty(next) || next == cursor) break;
                cursor = next;
            }
            return list;
        }

        internal static List<MusicSongInfo> GetSongs(MusicPlaylistInfo playlist)
        {
            var songs = new List<MusicSongInfo>();
            if (playlist == null || string.IsNullOrEmpty(playlist.Id)) return songs;

            string cursor = "";
            for (int page = 0; page < 50; page++)
            {
                var body = new Dictionary<string, object>
                {
                    { "playlist_id", playlist.Id },
                    { "owner_id", "" },
                    { "count", 100 },
                    { "cursor", cursor },
                    { "type", null }
                };
                // 歌单详情是公共接口：用 client token，不带 guard 头（带了会被判 access_token 无效）
                string raw = PlatformPublic("/playlist/detail/", body);
                if (string.IsNullOrEmpty(raw))
                {
                    LastError = "songs: " + LastError;
                    break;
                }
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                string errNo = Text(root, "err_no");
                if (!string.IsNullOrEmpty(errNo) && errNo != "0")
                {
                    LastError = "songs err_no=" + errNo + " " + Text(root, "err_msg");
                    break;
                }
                var data = Dig(root, "data");
                if (data == null) break;
                var entities = AsList(Dig(data, "media_entities"));
                if (entities == null) break;
                foreach (object item in entities)
                {
                    var entity = Dig(item, "track_entity");
                    if (entity == null) continue;
                    var added = ToSong(entity);
                    if (added != null) songs.Add(added);
                }
                string next = Text(data, "next_cursor");
                bool more = Text(data, "has_more") == "True" || Text(data, "has_more") == "true";
                if (string.IsNullOrEmpty(next) || next == cursor || !more) break;
                cursor = next;
            }
            return songs;
        }

        static MusicSongInfo ToSong(object entity)
        {
            var row = entity as Dictionary<string, object>;
            if (row == null) return null;
            var baseInfo = Dig(row, "base_info");
            if (baseInfo == null) return null;
            string id = Text(baseInfo, "id");
            if (string.IsNullOrEmpty(id)) return null;
            var related = Dig(row, "related_info");
            var display = Dig(row, "display_info");
            return new MusicSongInfo
            {
                Mid = id,
                Title = Text(baseInfo, "name"),
                Artists = ArtistNames(related),
                Duration = FormatDuration(ParseLong(Text(baseInfo, "duration_ms")) / 1000),
                AlbumMid = FirstNonEmpty(CoverOf(display), CoverOf(row))
            };
        }

        internal static string GetSongUrl(string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return "";
            // /media/mget/ 用公共 token 即可（和 Folia 一致）
            var body = new Dictionary<string, object>
            {
                { "media_list", new object[] { new Dictionary<string, object> { { "id", trackId }, { "type", "track" } } } },
                { "card_context", new Dictionary<string, object>() }
            };
            string raw = PlatformPublic("/media/mget/", body);
            if (string.IsNullOrEmpty(raw))
            {
                LastError = "audio: " + LastError;
                return "";
            }
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                string errNo = Text(root, "err_no");
                if (!string.IsNullOrEmpty(errNo) && errNo != "0")
                {
                    LastError = "audio err_no=" + errNo + " " + Text(root, "err_msg");
                    return "";
                }
                var mediaList = AsList(Dig(Dig(root, "data"), "media_list"));
                if (mediaList == null) { LastError = "audio: no media_list"; return ""; }
                foreach (object item in mediaList)
                {
                    var entity = Dig(item, "track_entity");
                    if (entity == null) continue;
                    string url = UrlFromPlayerInfo(Dig(entity, "player_info"));
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            }
            catch (Exception exception) { LastError = "audio parse: " + exception.Message; }
            return "";
        }

        static string UrlFromPlayerInfo(object playerInfo)
        {
            if (playerInfo == null) return "";
            foreach (string variant in new[] { "full", "preview" })
            {
                var block = Dig(playerInfo, variant);
                if (block == null) continue;
                var modelInfo = Dig(block, "video_model_info");
                if (modelInfo == null) continue;

                // 1) video_model 里的通用位率列表（未加密的才有可播地址）
                object model = Dig(modelInfo, "video_model");
                var modelText = model as string;
                if (!string.IsNullOrEmpty(modelText))
                {
                    try { model = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(modelText); }
                    catch (Exception) { model = null; }
                }
                var videoList = AsList(Dig(model, "video_list"));
                if (videoList != null)
                {
                    foreach (object item in videoList)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        if (IsEncrypted(row)) continue;
                        string mainUrl = Text(row, "main_url");
                        if (!string.IsNullOrEmpty(mainUrl)) return mainUrl;
                    }
                }

                // 2) 全被加密时走 url_player_info（VOD 播放信息，返回明文地址）
                string playInfoUrl = Text(modelInfo, "url_player_info");
                if (!string.IsNullOrEmpty(playInfoUrl))
                {
                    string playRaw = HttpGet(playInfoUrl, null);
                    if (!string.IsNullOrEmpty(playRaw))
                    {
                        try
                        {
                            var playRoot = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(playRaw);
                            var list = PlayInfoList(playRoot);
                            if (list != null)
                            {
                                foreach (object item in list)
                                {
                                    var row = item as Dictionary<string, object>;
                                    if (row == null) continue;
                                    if (IsEncrypted(row)) continue;
                                    string url = Text(row, "MainPlayUrl");
                                    if (!string.IsNullOrEmpty(url)) return url;
                                }
                            }
                        }
                        catch (Exception exception) { LastError = "playinfo parse: " + exception.Message; }
                    }
                }
            }
            return "";
        }

        static bool IsEncrypted(Dictionary<string, object> row)
        {
            var info = Dig(row, "encrypt_info");
            if (info != null)
            {
                string encrypt = Text(info, "encrypt");
                if (encrypt == "True" || encrypt == "true") return true;
                string spade = Text(info, "spade_a");
                if (spade == "True" || spade == "true") return true;
            }
            string playAuth = Text(row, "PlayAuth");
            if (playAuth == "True" || playAuth == "true") return true;
            string enc = Text(row, "Encrypt");
            return enc == "1" || enc == "True";
        }

        internal static string GetLyrics(string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return "";
            var body = new Dictionary<string, object>
            {
                { "media_list", new object[] { new Dictionary<string, object> { { "id", trackId }, { "type", "track" } } } },
                { "card_context", new Dictionary<string, object>() }
            };
            string raw = PlatformPublic("/media/mget/", body);
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var mediaList = AsList(Dig(Dig(root, "data"), "media_list"));
                if (mediaList == null) return "";
                foreach (object item in mediaList)
                {
                    var entity = Dig(item, "track_entity");
                    if (entity == null) continue;
                    var lyricInfo = Dig(Dig(entity, "display_info"), "lyric_info");
                    if (lyricInfo == null) continue;
                    // 翻译表里是标准 LRC；lyric_entity 是逐字富文本
                    var translations = Dig(lyricInfo, "lang_translations");
                    string lrc = TranslationLyric(translations);
                    if (!string.IsNullOrEmpty(lrc)) return lrc;
                    var lyricEntity = Dig(lyricInfo, "lyric_entity");
                    string rich = Text(lyricEntity, "content");
                    if (!string.IsNullOrEmpty(rich)) return ConvertRichLyric(rich);
                }
            }
            catch (Exception exception) { LastError = "lyric parse: " + exception.Message; }
            return "";
        }

        static string TranslationLyric(object translations)
        {
            var map = translations as Dictionary<string, object>;
            if (map == null) return "";
            foreach (KeyValuePair<string, object> pair in map)
            {
                string content = Text(pair.Value, "content");
                if (!string.IsNullOrEmpty(content) && content.IndexOf('[') >= 0) return content;
            }
            return "";
        }

        // [14750,1010]<0,170,0>我... → [00:14.75]我...
        static string ConvertRichLyric(string rich)
        {
            var builder = new StringBuilder();
            foreach (string line in rich.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                int comma = trimmed.IndexOf(',');
                if (trimmed[0] != '[' || comma < 0) continue;
                string startText = trimmed.Substring(1, comma - 1);
                long ms;
                if (!long.TryParse(startText, out ms)) continue;
                int close = trimmed.IndexOf(']');
                if (close < 0) continue;
                string rest = trimmed.Substring(close + 1);
                var textBuilder = new StringBuilder();
                int index = 0;
                while (index < rest.Length)
                {
                    int lt = rest.IndexOf('<', index);
                    if (lt < 0) break;
                    int gt = rest.IndexOf('>', lt);
                    if (gt < 0) break;
                    index = gt + 1;
                    if (index < rest.Length && rest[index] != '<')
                    {
                        int next = rest.IndexOf('<', index);
                        if (next < 0) next = rest.Length;
                        textBuilder.Append(rest.Substring(index, next - index));
                        index = next;
                    }
                }
                if (textBuilder.Length == 0) continue;
                long totalSeconds = ms / 1000;
                builder.Append('[');
                builder.Append((totalSeconds / 60).ToString("00"));
                builder.Append(':');
                builder.Append((totalSeconds % 60).ToString("00"));
                builder.Append('.');
                builder.Append(((ms % 1000) / 10).ToString("00"));
                builder.Append(']');
                builder.Append(textBuilder.ToString());
                builder.Append('\n');
            }
            return builder.ToString();
        }

        // ---------------------------------------------------------------- HTTP

        static string HttpGet(string url, Dictionary<string, string> headers)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = UserAgent;
                request.Timeout = 25000;
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> pair in headers) request.Headers[pair.Key] = pair.Value;
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception exception)
            {
                LastError = "http: " + exception.Message;
                return null;
            }
        }

        static string HttpPostJson(string url, string json, Dictionary<string, string> headers, out Dictionary<string, string> responseHeaders, out string body)
        {
            responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            body = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.UserAgent = UserAgent;
                request.ContentType = "application/json";
                request.Timeout = 25000;
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> pair in headers) request.Headers[pair.Key] = pair.Value;
                }
                byte[] payload = Encoding.UTF8.GetBytes(json);
                request.ContentLength = payload.Length;
                using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    foreach (string name in response.Headers.AllKeys)
                    {
                        if (name == null) continue;
                        responseHeaders[name] = response.Headers[name];
                    }
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                        return body;
                    }
                }
            }
            catch (Exception exception)
            {
                LastError = "http post: " + exception.Message;
                return null;
            }
        }

        static string HttpPostForm(string url, Dictionary<string, string> form, Dictionary<string, string> headers,
            out Dictionary<string, string> responseHeaders, out string body)
        {
            responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            body = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.UserAgent = UserAgent;
                request.ContentType = "application/x-www-form-urlencoded";
                request.Timeout = 25000;
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> pair in headers) request.Headers[pair.Key] = pair.Value;
                }
                var builder = new StringBuilder();
                foreach (KeyValuePair<string, string> pair in form)
                {
                    if (builder.Length > 0) builder.Append('&');
                    builder.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? ""));
                }
                byte[] payload = Encoding.UTF8.GetBytes(builder.ToString());
                request.ContentLength = payload.Length;
                using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    foreach (string name in response.Headers.AllKeys)
                    {
                        if (name == null) continue;
                        responseHeaders[name] = response.Headers[name];
                    }
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                        return body;
                    }
                }
            }
            catch (Exception exception)
            {
                LastError = "http form: " + exception.Message;
                return null;
            }
        }

        // ---------------------------------------------------------------- 平台请求

        static string Platform(string path, Dictionary<string, object> body)
        {
            QishuiSession value = LoadSession();
            if (value == null) { LastError = "no session"; return null; }
            var payload = new Dictionary<string, object>(body);
            var common = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(value.UserId)) common["user_id"] = value.UserId;
            payload["common_params"] = common;

            string url = "https://open.douyin.com" + Prefix + path + "?" + BuildQuery(CommonParams(value.DeviceId));
            string json = new JavaScriptSerializer().Serialize(payload);
            string body_;
            Dictionary<string, string> headers;
            string raw = HttpPostJson(url, json, GuardHeaders(value, path), out headers, out body_);
            LastBody = body_ ?? "";
            return raw;
        }

        static string PlatformPublic(string path, Dictionary<string, object> body)
        {
            QishuiSession value = LoadSession();
            string device = value != null ? value.DeviceId : DeviceId;
            var payload = new Dictionary<string, object>(body);
            var common = new Dictionary<string, object>();
            if (value != null && !string.IsNullOrEmpty(value.UserId)) common["user_id"] = value.UserId;
            payload["common_params"] = common;

            string url = "https://open.douyin.com" + Prefix + path + "?" + BuildQuery(CommonParams(device));
            var headers = new Dictionary<string, string> { { "access-token", ClientToken } };
            string body_;
            Dictionary<string, string> responseHeaders;
            string raw = HttpPostJson(url, new JavaScriptSerializer().Serialize(payload), headers, out responseHeaders, out body_);
            LastBody = body_ ?? "";
            return raw;
        }

        static Dictionary<string, string> GuardHeaders(QishuiSession value, string path)
        {
            var headers = new Dictionary<string, string>
            {
                { "access-token", value.Ticket },
                { "bd-ticket-guard-version", "1" },
                { "bd-ticket-guard-client-cert", value.GuardCert }
            };
            try
            {
                long timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                string content = "ticket=" + value.Ticket + "&path=" + Prefix + path + "&timestamp=" + timestamp;
                string signature = SignWithGuard(value.GuardKey, content);
                var data = new Dictionary<string, object>
                {
                    { "ts_sign", value.GuardTsSign },
                    { "req_content", "ticket,path,timestamp" },
                    { "req_sign", signature },
                    { "timestamp", timestamp }
                };
                headers["bd-ticket-guard-client-data"] = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data)));
            }
            catch (Exception exception) { LastError = "guard sign: " + exception.Message; }
            return headers;
        }

        // ---------------------------------------------------------------- 密码学

        // 生成 P-256 密钥对和 PKCS#10 CSR（base64 后的 PEM，和 Folia 一致）
        static bool CreateGuard(out string guardKey, out string csrBase64)
        {
            guardKey = "";
            csrBase64 = "";
            try
            {
                using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    var cng = ecdsa as ECDsaCng;
                    if (cng == null) { LastError = "no ECDsaCng"; return false; }

                    byte[] pkcs8 = cng.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
                    guardKey = Convert.ToBase64String(pkcs8);

                    byte[] publicBlob = cng.Key.Export(CngKeyBlobFormat.EccPublicBlob);
                    // 两种布局：
                    //   旧 BCRYPT_ECCKEY_BLOB：BLOBHEADER(8) + cbKey(4) + X + Y
                    //   CNG ECS1："ECS1" + cbKey(4) + X + Y
                    int offset;
                    int keyLength;
                    if (publicBlob.Length >= 8 && publicBlob[0] == (byte)'E' && publicBlob[1] == (byte)'C'
                        && publicBlob[2] == (byte)'S' && publicBlob[3] == (byte)'1')
                    {
                        keyLength = BitConverter.ToInt32(publicBlob, 4);
                        offset = 8;
                    }
                    else
                    {
                        keyLength = BitConverter.ToInt32(publicBlob, 8);
                        offset = 12;
                    }
                    byte[] x = new byte[keyLength];
                    byte[] y = new byte[keyLength];
                    Array.Copy(publicBlob, offset, x, 0, keyLength);
                    Array.Copy(publicBlob, offset + keyLength, y, 0, keyLength);

                    byte[] point = new byte[1 + x.Length + y.Length];
                    point[0] = 0x04;
                    Array.Copy(x, 0, point, 1, x.Length);
                    Array.Copy(y, 0, point, 1 + x.Length, y.Length);

                    // SPKI: SEQ{ SEQ{ OID ecPublicKey, OID prime256v1 }, BITSTRING(point) }
                    byte[] algorithm = DerSequence(Concat(DerOid(new byte[] { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01 }),
                                                          DerOid(new byte[] { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07 })));
                    byte[] spki = DerSequence(Concat(algorithm, DerBitString(point)));

                    // subject: CN=bd-ticket-guard, OU="", O=""
                    byte[] cn = DerSet(DerSequence(Concat(DerOid(new byte[] { 0x55, 0x04, 0x03 }), DerUtf8("bd-ticket-guard"))));
                    byte[] ou = DerSet(DerSequence(Concat(DerOid(new byte[] { 0x55, 0x04, 0x0B }), DerUtf8(""))));
                    byte[] org = DerSet(DerSequence(Concat(DerOid(new byte[] { 0x55, 0x04, 0x0A }), DerUtf8(""))));
                    byte[] subject = DerSequence(Concat(Concat(cn, ou), org));

                    byte[] info = DerSequence(Concat(Concat(Concat(DerInteger(0), subject), spki), DerContext(0, new byte[0])));
                    byte[] signature = SignDer(ecdsa, info);

                    byte[] algorithm2 = DerSequence(DerOid(new byte[] { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04, 0x03, 0x02 }));
                    byte[] request = DerSequence(Concat(Concat(info, algorithm2), DerBitString(signature)));

                    string base64 = Convert.ToBase64String(request);
                    var pem = new StringBuilder();
                    pem.Append("-----BEGIN CERTIFICATE REQUEST-----\n");
                    for (int i = 0; i < base64.Length; i += 64)
                    {
                        pem.Append(base64.Substring(i, Math.Min(64, base64.Length - i)));
                        pem.Append('\n');
                    }
                    pem.Append("-----END CERTIFICATE REQUEST-----\n");
                    csrBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pem.ToString()));
                    return true;
                }
            }
            catch (Exception exception)
            {
                LastError = "csr: " + exception.Message;
                return false;
            }
        }

        static string SignWithGuard(string guardKeyBase64, string content)
        {
            byte[] pkcs8 = Convert.FromBase64String(guardKeyBase64);
            using (var cng = CngKey.Import(pkcs8, CngKeyBlobFormat.Pkcs8PrivateBlob))
            using (var ecdsa = new ECDsaCng(cng))
            {
                return Convert.ToBase64String(SignDer(ecdsa, Encoding.UTF8.GetBytes(content)));
            }
        }

        // .NET 的 ECDSA 签名是 P1363(r||s)，CSR/guard 都要 DER 的 SEQUENCE{INTEGER r, INTEGER s}
        static byte[] SignDer(ECDsa ecdsa, byte[] data)
        {
            byte[] raw = ecdsa.SignData(data, HashAlgorithmName.SHA256);
            if (raw.Length > 0 && raw[0] == 0x30)
            {
                // 已经是 DER
                return raw;
            }
            int half = raw.Length / 2;
            byte[] r = new byte[half];
            byte[] s = new byte[half];
            Array.Copy(raw, 0, r, 0, half);
            Array.Copy(raw, half, s, 0, half);
            return DerSequence(Concat(DerInteger(r), DerInteger(s)));
        }

        static byte[] DerInteger(int value)
        {
            return DerInteger(BigEndian(value));
        }

        static byte[] BigEndian(int value)
        {
            if (value == 0) return new byte[] { 0 };
            var buffer = new List<byte>();
            int current = value;
            while (current > 0)
            {
                buffer.Insert(0, (byte)(current & 0xFF));
                current >>= 8;
            }
            return buffer.ToArray();
        }

        static byte[] DerInteger(byte[] bigEndian)
        {
            int start = 0;
            while (start < bigEndian.Length - 1 && bigEndian[start] == 0) start++;
            var value = new byte[bigEndian.Length - start];
            Array.Copy(bigEndian, start, value, 0, value.Length);
            bool needsZero = (value[0] & 0x80) != 0;
            byte[] content = needsZero ? Concat(new byte[] { 0 }, value) : value;
            return DerTag(0x02, content);
        }

        static byte[] DerOid(byte[] content) { return DerTag(0x06, content); }
        static byte[] DerUtf8(string text) { return DerTag(0x0C, Encoding.UTF8.GetBytes(text)); }
        static byte[] DerSequence(byte[] content) { return DerTag(0x30, content); }
        static byte[] DerSet(byte[] content) { return DerTag(0x31, content); }
        static byte[] DerContext(int tag, byte[] content) { return DerTag((byte)(0xA0 + tag), content); }

        static byte[] DerBitString(byte[] content)
        {
            return DerTag(0x03, Concat(new byte[] { 0 }, content));
        }

        static byte[] DerTag(byte tag, byte[] content)
        {
            var header = new List<byte>();
            header.Add(tag);
            int length = content.Length;
            if (length < 0x80) header.Add((byte)length);
            else if (length < 0x100) { header.Add(0x81); header.Add((byte)length); }
            else { header.Add(0x82); header.Add((byte)(length >> 8)); header.Add((byte)(length & 0xFF)); }
            return Concat(header.ToArray(), content);
        }

        static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Array.Copy(first, 0, result, 0, first.Length);
            Array.Copy(second, 0, result, first.Length, second.Length);
            return result;
        }

        // ---------------------------------------------------------------- 公共参数

        static Dictionary<string, string> CommonParams(string device)
        {
            long timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var random = new Random();
            return new Dictionary<string, string>
            {
                { "aid", "765213" },
                { "app_id", "765213" },
                { "app_name", "byte_autoservice_lite" },
                { "version_code", "100010" },
                { "version_name", "1.0.10" },
                { "channel", "bydSong" },
                { "sdk_aid", "753999" },
                { "caller_name", "luna_open_sdk" },
                { "sdk_version_code", "100000100" },
                { "sdk_version_name", "0.0.1" },
                { "sdk_channel", "Test" },
                { "vehicle_type", "BYD" },
                { "device_brand", "BYD" },
                { "device_type", "BYD" },
                { "device_platform", "android" },
                { "resolution", "1920x1080" },
                { "os_version", "13" },
                { "ac", "wifi" },
                { "device_id", device },
                { "did", device },
                { "iid", device },
                { "vehicle_id", "qishui-" + device },
                { "_timestamp", timestamp.ToString() },
                { "_nonce", random.Next(0, 65535).ToString() }
            };
        }

        static string QrQuery(bool withExtra, string token, string timestamp)
        {
            var values = new Dictionary<string, string>
            {
                { "aid", Aide },
                { "client_key", ClientKey },
                { "scope", string.Join(",", Scopes) },
                { "state", "" },
                { "next", "" },
                { "jump_type", "native" },
                { "optional_scope_check", "" },
                { "optional_scope_uncheck", "" },
                { "customize_params", "{\"source\":\"pc_auth\",\"not_skip_confirm\":\"true\"}" },
                { "source_from", "native" },
                { "device_platform", "android" },
                { "app_identity", Md5Hex("com.luna.music.car") },
                { "signature", "cf9024ea72d9c71813238b13ea107a3d" }
            };
            if (withExtra)
            {
                values["token"] = token;
                values["timestamp"] = timestamp;
            }
            return BuildQuery(values);
        }

        static string BuildQuery(Dictionary<string, string> values)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (builder.Length > 0) builder.Append('&');
                builder.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? ""));
            }
            return builder.ToString();
        }

        // ---------------------------------------------------------------- 小工具

        static string Md5Hex(string text)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder();
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        static string CoverOf(object node)
        {
            var map = node as Dictionary<string, object>;
            if (map == null) return "";
            foreach (string field in new[] { "display_info", "cover_url", "url_cover", "medium_avatar_url", "avatar_url" })
            {
                var cover = Dig(node, field);
                if (cover == null) continue;
                if (field == "display_info")
                {
                    cover = Dig(cover, "cover_url");
                    if (cover == null) continue;
                }
                string url = CoverUrl(cover);
                if (!string.IsNullOrEmpty(url)) return url;
            }
            string plain = Text(node, "cover");
            return plain;
        }

        // 汽水的封面是 urls[0] + uri 拼出来的
        static string CoverUrl(object cover)
        {
            if (cover == null) return "";
            string plain = cover as string;
            if (!string.IsNullOrEmpty(plain)) return plain;
            string uri = Text(cover, "uri");
            var urls = AsList(Dig(cover, "urls"));
            if (urls != null)
            {
                foreach (object item in urls)
                {
                    string baseUrl = item as string;
                    if (string.IsNullOrEmpty(baseUrl)) continue;
                    if (string.IsNullOrEmpty(uri)) return baseUrl;
                    if (!baseUrl.EndsWith("/")) baseUrl += "/";
                    // 裸 uri 会 400，必须带尺寸模板
                    return baseUrl + uri.TrimStart('/') + "~c5_300x300.jpeg";
                }
            }
            return uri;
        }


        static string FirstUrl(object cover)
        {
            if (cover == null) return "";
            string plain = cover as string;
            if (!string.IsNullOrEmpty(plain)) return plain;
            var urls = AsList(Dig(cover, "urls"));
            if (urls == null) return "";
            var prefix = Text(cover, "urls") ;
            foreach (object item in urls)
            {
                string url = item as string;
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return "";
        }

        static string ArtistNames(object related)
        {
            var links = AsList(Dig(related, "artist_links"));
            if (links == null) return "";
            var names = new List<string>();
            foreach (object item in links)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                string name = Text(row, "name");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return string.Join(" / ", names.ToArray());
        }

        static string FormatDuration(long seconds)
        {
            if (seconds <= 0) return "";
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        static long ParseLong(string text)
        {
            long value;
            long.TryParse(text, out value);
            return value;
        }

        static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? (b ?? "") : a;
        }

        static string Get(Dictionary<string, string> map, string key)
        {
            string value;
            return map.TryGetValue(key, out value) ? value : "";
        }

        static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > 160 ? text.Substring(0, 160) : text;
        }

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
            if (node is string) return node as string;
            object value = Dig(node, key);
            if (value == null) return "";
            return Convert.ToString(value);
        }

        static System.Collections.IEnumerable PlayInfoList(object root)
        {
            var result = Dig(root, "Result");
            var data = Dig(result, "Data");
            var list = AsList(Dig(data, "PlayInfoList"));
            if (list != null) return list;
            data = Dig(root, "data");
            list = AsList(Dig(data, "PlayInfoList"));
            if (list != null) return list;
            return AsList(Dig(root, "PlayInfoList"));
        }

        static int Count(System.Collections.IEnumerable list)
        {
            int n = 0;
            foreach (object item in list) n++;
            return n;
        }
    }
}
