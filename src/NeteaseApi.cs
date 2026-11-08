// ---------------------------------------------------------------------------
// 协议实现来源与免责（详见仓库根目录 THIRD-PARTY-NOTICES.txt 第五、六节）：
//   本文件是对第三方开源项目公开「协议事实」的独立 C# 重写，
//   不包含也不分发任何音乐内容、音源文件或解密密钥；
//   不做付费/加密绕过（无明文音源时直接跳过）；
//   与各音乐平台无关联，商标归各自权利人；使用时请遵守平台服务条款。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace IslandShot
{
    /// <summary>
    /// 网易云音乐：歌单 / 歌曲 / 播放地址 / 歌词。
    /// 走 music.163.com 的 web 接口（非 weapi，不需要加密），带扫码登录拿到的 cookie。
    /// </summary>
    internal static class NeteaseApi
    {
        const string Base = "https://music.163.com";
        const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

        internal static string LastError = "";

        static string CookieHeader()
        {
            Dictionary<string, string> cookies = MusicCookieStore.Load("netease");
            if (cookies == null || cookies.Count == 0) return "";
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in cookies)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                if (builder.Length > 0) builder.Append("; ");
                builder.Append(pair.Key).Append('=').Append(pair.Value);
            }
            return builder.ToString();
        }

        static string Get(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    client.Headers[HttpRequestHeader.Referer] = "https://music.163.com/";
                    string cookie = CookieHeader();
                    if (!string.IsNullOrEmpty(cookie)) client.Headers[HttpRequestHeader.Cookie] = cookie;
                    byte[] bytes = client.DownloadData(url);
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch (Exception exception) { LastError = "get: " + exception.Message; return null; }
        }

        static string Uid()
        {
            string raw = Get(Base + "/api/nuser/account/get");
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var profile = Dig(root, "profile");
                string uid = Text(profile, "userId");
                if (string.IsNullOrEmpty(uid)) LastError = "account: no profile (cookie 可能失效)";
                return uid;
            }
            catch (Exception exception) { LastError = "account parse: " + exception.Message; return ""; }
        }

        internal static List<MusicPlaylistInfo> GetPlaylists()
        {
            var list = new List<MusicPlaylistInfo>();
            string uid = Uid();
            if (string.IsNullOrEmpty(uid)) return list;

            string raw = Get(Base + "/api/user/playlist?uid=" + Uri.EscapeDataString(uid) + "&limit=1000&offset=0");
            if (string.IsNullOrEmpty(raw)) return list;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var arr = AsList(Dig(root, "playlist"));
                if (arr == null) { LastError = "playlist: no array"; return list; }
                foreach (object item in arr)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    string id = Text(row, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    list.Add(new MusicPlaylistInfo
                    {
                        Id = id,
                        DirId = "",
                        Name = Text(row, "name"),
                        Count = Int(row, "trackCount"),
                        Url = Text(row, "coverImgUrl")
                    });
                }
            }
            catch (Exception exception) { LastError = "playlist parse: " + exception.Message; }
            return list;
        }

        internal static List<MusicSongInfo> GetSongs(MusicPlaylistInfo playlist)
        {
            var songs = new List<MusicSongInfo>();
            if (playlist == null || string.IsNullOrEmpty(playlist.Id)) return songs;

            // 1) 详情里只给前 20 首，但 trackIds 是全的
            string raw = Get(Base + "/api/v6/playlist/detail?id=" + Uri.EscapeDataString(playlist.Id) + "&n=1000&s=0");
            if (string.IsNullOrEmpty(raw))
            {
                LastError = "songs detail empty: " + LastError;
                return songs;
            }

            var ids = new List<string>();
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var detail = Dig(root, "playlist");
                var trackIds = AsList(Dig(detail, "trackIds"));
                if (trackIds != null)
                {
                    foreach (object item in trackIds)
                    {
                        var row = item as Dictionary<string, object>;
                        string id = row == null ? Convert.ToString(item) : Text(row, "id");
                        if (!string.IsNullOrEmpty(id)) ids.Add(id);
                    }
                }
                if (ids.Count == 0)
                {
                    // 没有 trackIds 时退回 tracks
                    var tracks = AsList(Dig(detail, "tracks"));
                    if (tracks != null)
                    {
                        foreach (object item in tracks)
                        {
                            var row = item as Dictionary<string, object>;
                            string id = Text(row, "id");
                            if (!string.IsNullOrEmpty(id)) ids.Add(id);
                        }
                    }
                }
                if (ids.Count == 0)
                {
                    LastError = "songs: no trackIds (playlist 可能为空)";
                    return songs;
                }
            }
            catch (Exception exception) { LastError = "songs detail parse: " + exception.Message; return songs; }

            // 2) 批量取歌曲信息，每批 200 首
            for (int offset = 0; offset < ids.Count; offset += 200)
            {
                var batch = ids.GetRange(offset, Math.Min(200, ids.Count - offset));
                string body = "ids=" + Uri.EscapeDataString("[" + string.Join(",", batch.ToArray()) + "]");
                string detailRaw = Get(Base + "/api/song/detail?" + body);
                if (string.IsNullOrEmpty(detailRaw)) continue;
                try
                {
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(detailRaw);
                    var arr = AsList(Dig(root, "songs"));
                    if (arr == null) continue;
                    foreach (object item in arr)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        string id = Text(row, "id");
                        if (string.IsNullOrEmpty(id)) continue;
                        var album = Dig(row, "al");
                        if (album == null) album = Dig(row, "album");
                        songs.Add(new MusicSongInfo
                        {
                            Mid = id,
                            Title = Text(row, "name"),
                            Artists = ArtistNames(row),
                            Duration = FormatDuration((Int(row, "dt") > 0 ? Int(row, "dt") : Int(row, "duration")) / 1000),
                            AlbumMid = FirstNonEmpty(Text(album, "picUrl"), Text(album, "blurPicUrl"))
                        });
                    }
                }
                catch (Exception exception) { LastError = "song/detail parse: " + exception.Message; }
            }
            return songs;
        }

        internal static string GetSongUrl(string songId)
        {
            if (string.IsNullOrEmpty(songId)) return "";
            string raw = Get(Base + "/api/song/enhance/player/url?ids=[" + songId + "]&br=320000");
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var arr = AsList(Dig(root, "data"));
                if (arr == null) return "";
                foreach (object item in arr)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    string url = Text(row, "url");
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            }
            catch (Exception exception) { LastError = "url parse: " + exception.Message; }
            return "";
        }

        internal static string GetLyrics(string songId)
        {
            if (string.IsNullOrEmpty(songId)) return "";
            string raw = Get(Base + "/api/song/lyric?id=" + Uri.EscapeDataString(songId) + "&lv=-1&kv=-1&tv=-1");
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                var lrc = Dig(root, "lrc");
                string lyric = Text(lrc, "lyric");
                if (!string.IsNullOrEmpty(lyric)) return lyric;
                var tlyric = Dig(root, "tlyric");
                return Text(tlyric, "lyric");
            }
            catch (Exception exception) { LastError = "lyric parse: " + exception.Message; return ""; }
        }

        // ---------------------------------------------------------------- 小工具

        static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? (b ?? "") : a;
        }

        static string ArtistNames(Dictionary<string, object> row)
        {
            var arr = AsList(Dig(row, "ar"));
            if (arr == null) arr = AsList(Dig(row, "artists"));
            if (arr == null) return "";
            var names = new List<string>();
            foreach (object item in arr)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                string name = Text(entry, "name");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return string.Join(" / ", names.ToArray());
        }

        static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "";
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
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
