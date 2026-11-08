using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    internal sealed class MusicPlaylistInfo
    {
        public string Id;
        public string DirId;   // QQ 的 dirId（201=我喜欢，其它=自建歌单），歌单详情要它
        public string Name;
        public int Count;
        public string Url;
    }

    internal sealed class MusicSongInfo
    {
        public string Mid;          // 空 = 无版权/下架，播不了
        public string Title;
        public string Artists;
        public string Duration;
        public string AlbumMid;     // 专辑封面：https://y.gtimg.cn/music/photo_new/T002R300x300M000{albumMid}.jpg
        public bool Playable { get { return !string.IsNullOrEmpty(Mid); } }
    }

    // 一行歌词 + 它的时间点（点击这行可以从这里开始播）
    internal sealed class MusicLyricLine
    {
        public TimeSpan Time;
        public string Text;
    }

    // ---------------------------------------------------------------------
    // 歌单导入：在用户已登录的 WebView2 profile 里开一个看不见的窗口，
    // 用页面自己的身份调 QQ 音乐的接口，一次拿到全部歌单 / 歌曲。
    //
    // 为什么不爬页面：页面是虚拟列表（一屏十来首），要翻页、要等渲染，慢且脆。
    // 接口返回的是完整 JSON，一次请求全部到手——这也解释了 Folia 为什么"登录后
    // 立刻就有全部歌单"：它走的就是这条路。
    //
    // 为什么要在网页里 fetch：接口认的是 y.qq.com 的 cookie（qm_keyst），
    // 在页面上下文里发请求，cookie、CORS、g_tk 都天然正确，不用我们手工拼。
    //
    // 独立实现：请求格式是按公开资料和抓包观察写出来的，没有使用任何第三方项目代码。
    // ---------------------------------------------------------------------
    internal static class MusicImport
    {
        internal static void LoadPlaylists(MusicPlatform platform, Action<List<MusicPlaylistInfo>> done, Action<string> failed)
        {
            if (platform == null)
            {
                if (failed != null) failed("没有选择平台");
                return;
            }
            if (platform.Id == "kugou")
            {
                LoadKugouPlaylists(platform, done, failed);
                return;
            }
            if (platform.Id == "netease")
            {
                LoadNeteasePlaylists(done, failed);
                return;
            }
            if (platform.Id == "qishui")
            {
                LoadQishuiPlaylists(done, failed);
                return;
            }
            if (platform.Id != "qq")
            {
                if (failed != null) failed("这个平台还没接歌单导入");
                return;
            }
            RunApi(platform, PlaylistScript, delegate(string json)
            {
                List<MusicPlaylistInfo> list = ParsePlaylists(json);
                if (list.Count == 0)
                {
                    if (failed != null) failed("没读到歌单（登录态可能失效）");
                    return;
                }
                if (done != null) done(list);
            }, failed);
        }

        internal static void LoadSongs(MusicPlatform platform, MusicPlaylistInfo playlist, Action<List<MusicSongInfo>> done, Action<string> failed)
        {
            if (platform == null || playlist == null || string.IsNullOrEmpty(playlist.Id))
            {
                if (failed != null) failed("这个歌单打不开");
                return;
            }
            if (platform.Id == "kugou")
            {
                LoadKugouSongs(platform, playlist, done, failed);
                return;
            }
            if (platform.Id == "netease")
            {
                LoadNeteaseSongs(playlist, done, failed);
                return;
            }
            if (platform.Id == "qishui")
            {
                LoadQishuiSongs(playlist, done, failed);
                return;
            }
            if (platform.Id != "qq")
            {
                if (failed != null) failed("这个歌单打不开");
                return;
            }
            string script = SongScript
                .Replace("__DISSTID__", playlist.Id)
                .Replace("__DIRID__", string.IsNullOrEmpty(playlist.DirId) ? "0" : playlist.DirId);
            RunApi(platform, script, delegate(string json)
            {
                List<MusicSongInfo> list = ParseSongs(json);
                if (list.Count == 0)
                {
                    if (failed != null) failed("没读到歌曲（可能这个歌单需要会员）");
                    return;
                }
                if (done != null) done(list);
            }, failed);
        }

        // ------------------------------------------------------------ 跑一次

        // ------------------------------------------------------------ 酷狗

        static void LoadKugouPlaylists(MusicPlatform platform, Action<List<MusicPlaylistInfo>> done, Action<string> failed)
        {
            Log("kugou playlists start");
            KugouApi.GetSession(platform, delegate(KugouApi.KugouSession session)
            {
                Task.Run(delegate
                {
                    List<MusicPlaylistInfo> list = null;
                    string error = null;
                    try { list = KugouApi.GetPlaylists(session); }
                    catch (Exception exception) { error = exception.Message; }
                    Dispatch(delegate
                    {
                        if (error != null)
                        {
                            Log("kugou playlists error: " + error);
                            if (failed != null) failed("酷狗歌单读取失败：" + error);
                            return;
                        }
                        if (list == null || list.Count == 0)
                        {
                            string raw = KugouApi.LastBody ?? "";
                            if (raw.Length > 200) raw = raw.Substring(0, 200);
                            Log("kugou playlists empty: err=" + KugouApi.LastError + " body=" + raw);
                            if (failed != null) failed("没读到酷狗歌单");
                            return;
                        }
                        Log("kugou playlists ok: " + list.Count);
                        if (done != null) done(list);
                    });
                });
            }, failed);
        }

        static void LoadKugouSongs(MusicPlatform platform, MusicPlaylistInfo playlist, Action<List<MusicSongInfo>> done, Action<string> failed)
        {
            Log("kugou songs start: " + playlist.Name);
            KugouApi.GetSession(platform, delegate(KugouApi.KugouSession session)
            {
                Task.Run(delegate
                {
                    List<MusicSongInfo> list = null;
                    string error = null;
                    try { list = KugouApi.GetSongs(session, playlist); }
                    catch (Exception exception) { error = exception.Message; }
                    Dispatch(delegate
                    {
                        if (error != null)
                        {
                            Log("kugou songs error: " + error);
                            if (failed != null) failed("酷狗歌曲读取失败：" + error);
                            return;
                        }
                        if (list == null || list.Count == 0)
                        {
                            string raw = KugouApi.LastBody ?? "";
                            if (raw.Length > 200) raw = raw.Substring(0, 200);
                            Log("kugou songs empty: err=" + KugouApi.LastError + " body=" + raw);
                            if (failed != null) failed("没读到酷狗歌曲");
                            return;
                        }
                        Log("kugou songs ok: " + list.Count);
                        if (done != null) done(list);
                    });
                });
            }, failed);
        }

        static void Dispatch(Action action)
        {
            if (action == null) return;
            var app = Application.Current;
            if (app == null) { action(); return; }
            app.Dispatcher.BeginInvoke(action);
        }

        static void RunApi(MusicPlatform platform, string script, Action<string> ok, Action<string> failed)
        {
            Log("import start [" + platform.Id + "]");
            var window = new Window
            {
                Width = 1100,
                Height = 800,
                WindowStyle = WindowStyle.None,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = Brushes.Black
            };
            var view = new WebView2();
            window.Content = view;

            bool finished = false;
            int attempt = 0;
            DispatcherTimer timer = null;

            Action finish = delegate
            {
                if (finished) return;
                finished = true;
                if (timer != null) { try { timer.Stop(); } catch (Exception) { } }
                try { view.Dispose(); } catch (Exception) { }
                try { window.Close(); } catch (Exception) { }
            };

            // 发起一次请求。页面没就绪（还没到 y.qq.com）时会 Failed to fetch，靠上层重试。
            Action startAttempt = null;
            startAttempt = delegate
            {
                if (finished) return;
                if (timer != null) timer.Stop();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                timer.Tick += delegate
                {
                    if (finished) return;
                    finish();
                    if (failed != null) failed("接口没有响应（网络或登录态问题）");
                };
                timer.Start();
                try { view.CoreWebView2.ExecuteScriptAsync(script); } catch (Exception) { }
            };

            window.Loaded += async delegate
            {
                try
                {
                    string folder = Path.Combine(AiConfigStore.DataDir, "music", "webview2-" + platform.Id);
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, folder);
                    await view.EnsureCoreWebView2Async(environment);
                    if (view.CoreWebView2 == null) throw new InvalidOperationException("WebView2 没有就绪");
                    view.CoreWebView2.Settings.AreDevToolsEnabled = false;

                    view.CoreWebView2.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e)
                    {
                        if (finished) return;
                        string message = e.TryGetWebMessageAsString();
                        Log("import message len=" + (message == null ? 0 : message.Length) + " head=" + Head(message));

                        // 页面还没到 y.qq.com 就发请求会 "Failed to fetch"：重试
                        if (message != null && message.StartsWith("ERR", StringComparison.Ordinal) && attempt < 2)
                        {
                            attempt++;
                            Log("import retry " + attempt);
                            if (timer != null) timer.Stop();
                            StartDelayed(view, startAttempt, 1500 * attempt);
                            return;
                        }

                        finish();
                        if (message != null && message.StartsWith("ERR", StringComparison.Ordinal))
                        {
                            if (failed != null) failed(message);
                        }
                        else
                        {
                            ok(message);
                        }
                    };

                    view.CoreWebView2.Navigate("https://y.qq.com/n/ryqq_v2/");

                    // 等页面真的落在 y.qq.com 且加载完，再发请求（最多等 20 秒）
                    for (int i = 0; i < 40; i++)
                    {
                        await Task.Delay(500);
                        string state = "";
                        try { state = await view.CoreWebView2.ExecuteScriptAsync("document.readyState+'|'+location.host"); } catch (Exception) { }
                        if (state != null && state.IndexOf("y.qq.com", StringComparison.Ordinal) >= 0
                            && state.IndexOf("complete", StringComparison.Ordinal) >= 0)
                        {
                            break;
                        }
                    }
                    Log("import ready [" + platform.Id + "]");
                    startAttempt();
                }
                catch (Exception exception)
                {
                    if (finished) return;
                    finish();
                    if (failed != null) failed(exception.Message);
                }
            };

            window.Show();
        }

        // 延迟一点再重试（给页面加载留时间）
        static void StartDelayed(WebView2 view, Action action, int delayMs)
        {
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            delayTimer.Tick += delegate
            {
                delayTimer.Stop();
                try { action(); } catch (Exception) { }
            };
            delayTimer.Start();
        }

        internal static void LoadLyrics(MusicPlatform platform, string songMid, Action<List<MusicLyricLine>> done, Action<string> failed)
        {
            if (platform == null || string.IsNullOrEmpty(songMid))
            {
                if (failed != null) failed("没有这首歌的歌词");
                return;
            }
            if (platform.Id == "kugou")
            {
                LoadKugouLyrics(platform, songMid, done, failed);
                return;
            }
            if (platform.Id == "netease")
            {
                LoadNeteaseLyrics(songMid, done, failed);
                return;
            }
            if (platform.Id == "qishui")
            {
                LoadQishuiLyrics(songMid, done, failed);
                return;
            }
            if (platform.Id != "qq")
            {
                if (failed != null) failed("这个平台还没接歌词");
                return;
            }
            string script = LyricsScript.Replace("__SONGMID__", songMid);
            RunApi(platform, script, delegate(string json)
            {
                List<MusicLyricLine> lines = ParseLyrics(json);
                if (lines == null || lines.Count == 0)
                {
                    if (failed != null) failed("暂无歌词");
                    return;
                }
                if (done != null) done(lines);
            }, failed);
        }

        // ---------------------------------------------------------------- 汽水

        static void LoadQishuiPlaylists(Action<List<MusicPlaylistInfo>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                List<MusicPlaylistInfo> list = null;
                string error = null;
                try { list = QishuiApi.GetPlaylists(); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("qishui playlists error: " + error); if (failed != null) failed(error); return; }
                    if (list == null || list.Count == 0)
                    {
                        Log("qishui playlists empty: " + QishuiApi.LastError);
                        if (failed != null) failed("没读到汽水歌单（登录态可能失效）");
                        return;
                    }
                    Log("qishui playlists ok: " + list.Count);
                    if (done != null) done(list);
                });
            });
        }

        static void LoadQishuiSongs(MusicPlaylistInfo playlist, Action<List<MusicSongInfo>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                List<MusicSongInfo> list = null;
                string error = null;
                try { list = QishuiApi.GetSongs(playlist); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("qishui songs error: " + error); if (failed != null) failed(error); return; }
                    if (list == null || list.Count == 0)
                    {
                        Log("qishui songs empty: " + QishuiApi.LastError);
                        if (failed != null) failed("没读到汽水歌曲");
                        return;
                    }
                    Log("qishui songs ok: " + list.Count);
                    if (done != null) done(list);
                });
            });
        }

        static void LoadQishuiLyrics(string songMid, Action<List<MusicLyricLine>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                string lrc = null;
                string error = null;
                try { lrc = QishuiApi.GetLyrics(songMid); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("qishui lyric error: " + error); if (failed != null) failed(error); return; }
                    List<MusicLyricLine> lines = ParseLyrics(lrc == null ? "" : lrc);
                    if (lines.Count == 0)
                    {
                        Log("qishui lyric empty: " + QishuiApi.LastError);
                        if (failed != null) failed("暂无歌词");
                        return;
                    }
                    Log("qishui lyric ok: " + lines.Count);
                    if (done != null) done(lines);
                });
            });
        }

        // ---------------------------------------------------------------- 网易云

        static void LoadNeteasePlaylists(Action<List<MusicPlaylistInfo>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                List<MusicPlaylistInfo> list = null;
                string error = null;
                try { list = NeteaseApi.GetPlaylists(); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("netease playlists error: " + error); if (failed != null) failed(error); return; }
                    if (list == null || list.Count == 0)
                    {
                        Log("netease playlists empty: " + NeteaseApi.LastError);
                        if (failed != null) failed("没读到网易云歌单（登录态可能失效）");
                        return;
                    }
                    Log("netease playlists ok: " + list.Count);
                    if (done != null) done(list);
                });
            });
        }

        static void LoadNeteaseSongs(MusicPlaylistInfo playlist, Action<List<MusicSongInfo>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                List<MusicSongInfo> list = null;
                string error = null;
                try { list = NeteaseApi.GetSongs(playlist); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("netease songs error: " + error); if (failed != null) failed(error); return; }
                    if (list == null || list.Count == 0)
                    {
                        Log("netease songs empty: " + NeteaseApi.LastError);
                        if (failed != null) failed("没读到网易云歌曲");
                        return;
                    }
                    Log("netease songs ok: " + list.Count);
                    if (done != null) done(list);
                });
            });
        }

        static void LoadNeteaseLyrics(string songMid, Action<List<MusicLyricLine>> done, Action<string> failed)
        {
            Task.Run(delegate
            {
                string lrc = null;
                string error = null;
                try { lrc = NeteaseApi.GetLyrics(songMid); }
                catch (Exception exception) { error = exception.Message; }
                Dispatch(delegate
                {
                    if (error != null) { Log("netease lyric error: " + error); if (failed != null) failed(error); return; }
                    List<MusicLyricLine> lines = ParseLyrics(lrc == null ? "" : lrc);
                    if (lines.Count == 0)
                    {
                        Log("netease lyric empty: " + NeteaseApi.LastError);
                        if (failed != null) failed("暂无歌词");
                        return;
                    }
                    Log("netease lyric ok: " + lines.Count);
                    if (done != null) done(lines);
                });
            });
        }

        static void LoadKugouLyrics(MusicPlatform platform, string songMid, Action<List<MusicLyricLine>> done, Action<string> failed)
        {
            Log("kugou lyric start");
            KugouApi.GetSession(platform, delegate(KugouApi.KugouSession session)
            {
                Task.Run(delegate
                {
                    string lrc = null;
                    string error = null;
                    try { lrc = KugouApi.GetLyrics(session, songMid, 0); }
                    catch (Exception exception) { error = exception.Message; }
                    Dispatch(delegate
                    {
                        if (error != null) { Log("kugou lyric error: " + error); if (failed != null) failed(error); return; }
                        List<MusicLyricLine> lines = ParseLyrics(lrc == null ? "" : lrc);
                        if (lines.Count == 0)
                        {
                            Log("kugou lyric empty: " + KugouApi.LastError);
                            if (failed != null) failed("暂无歌词");
                            return;
                        }
                        Log("kugou lyric ok: " + lines.Count);
                        if (done != null) done(lines);
                    });
                });
            }, failed);
        }


        // LRC → 带时间点的行。[00:12.34] 这种前缀可能有多个，全部解析
        static List<MusicLyricLine> ParseLyrics(string json)
        {
            var result = new List<MusicLyricLine>();
            if (string.IsNullOrEmpty(json)) return result;

            string raw = "";
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root != null && root.ContainsKey("lyric")) raw = Convert.ToString(root["lyric"]);
            }
            catch (Exception)
            {
                raw = json;
            }
            if (string.IsNullOrEmpty(raw)) return result;

            var tag = new System.Text.RegularExpressions.Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
            foreach (string line in raw.Replace("\r", "").Split('\n'))
            {
                string text = line;
                var matches = tag.Matches(line);
                if (matches.Count == 0) continue;
                text = tag.Replace(line, "").Trim();
                if (text.Length == 0) continue;

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);
                    int millis = 0;
                    if (match.Groups[3].Success)
                    {
                        string ms = match.Groups[3].Value;
                        if (ms.Length == 1) ms = ms + "00";
                        else if (ms.Length == 2) ms = ms + "0";
                        millis = int.Parse(ms.Substring(0, 3));
                    }
                    result.Add(new MusicLyricLine
                    {
                        Time = new TimeSpan(0, 0, minutes, seconds, millis),
                        Text = text
                    });
                }
            }

            result.Sort(delegate(MusicLyricLine a, MusicLyricLine b) { return a.Time.CompareTo(b.Time); });
            return result;
        }

        // ------------------------------------------------------------ 解析

        static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(AiConfigStore.DataDir, "music");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "import.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static string Head(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            return text.Length <= 140 ? text : text.Substring(0, 140) + "…";
        }

        static List<MusicPlaylistInfo> ParsePlaylists(string json)
        {
            var result = new List<MusicPlaylistInfo>();
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                var node = Dig(root, "req_0");
                if (node == null) node = Dig(root, "req");
                var data = Dig(node, "data");
                var list = Dig(data, "v_playlist") as System.Collections.ArrayList;
                if (list == null) return result;
                foreach (object item in list)
                {
                    var entry = item as Dictionary<string, object>;
                    if (entry == null) continue;
                    string name = Text(entry, "dirName");
                    string id = Text(entry, "tid");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
                    var info = new MusicPlaylistInfo
                    {
                        Id = id,
                        DirId = Text(entry, "dirId"),
                        Name = name,
                        Count = Number(entry, "songNum")
                    };
                    result.Add(info);
                }
                // "我喜欢" 放最前面
                result.Sort(delegate(MusicPlaylistInfo a, MusicPlaylistInfo b)
                {
                    bool aLike = a.Name.IndexOf("我喜欢", StringComparison.Ordinal) >= 0;
                    bool bLike = b.Name.IndexOf("我喜欢", StringComparison.Ordinal) >= 0;
                    if (aLike != bLike) return aLike ? -1 : 1;
                    return 0;
                });
            }
            catch (Exception exception)
            {
                Log("parse playlists failed: " + exception.Message);
            }
            Log("parse playlists -> " + result.Count);
            return result;
        }

        static List<MusicSongInfo> ParseSongs(string json)
        {
            var result = new List<MusicSongInfo>();
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                var node = Dig(root, "req_0");
                if (node == null) node = Dig(root, "req");
                var data = Dig(node, "data");
                // 两种返回结构都兼容：data.songlist（CgiGetDiss）或 data.cdlist[0].songlist
                var songlist = Dig(data, "songlist") as System.Collections.ArrayList;
                if (songlist == null)
                {
                    var cdlist = Dig(data, "cdlist") as System.Collections.ArrayList;
                    if (cdlist != null && cdlist.Count > 0)
                    {
                        var cd = cdlist[0] as Dictionary<string, object>;
                        songlist = Dig(cd, "songlist") as System.Collections.ArrayList;
                    }
                }
                if (songlist == null) return result;
                Log("parse songs raw -> " + songlist.Count);

                int skipped = 0;
                foreach (object item in songlist)
                {
                    var song = item as Dictionary<string, object>;
                    if (song == null) { skipped++; continue; }
                    string mid = Text(song, "mid");
                    if (string.IsNullOrEmpty(mid)) mid = Text(song, "songmid");
                    string name = Text(song, "name");
                    if (string.IsNullOrEmpty(name)) name = Text(song, "title");
                    // 下架/无版权的曲子字段会是空的：保留它、标成"不可播放"，
                    // 而不是直接丢掉——否则列表条数会和歌单显示的数量对不上
                    if (string.IsNullOrEmpty(mid) && string.IsNullOrEmpty(name))
                    {
                        skipped++;
                        if (skipped == 1)
                        {
                            try { Log("skip sample: " + new JavaScriptSerializer().Serialize(song).Substring(0, 240)); } catch (Exception) { }
                        }
                        continue;
                    }
                    if (string.IsNullOrEmpty(name)) name = "（无版权 / 不可播放）";

                    string artists = "";
                    var singers = Dig(song, "singer") as System.Collections.ArrayList;
                    if (singers != null)
                    {
                        var names = new List<string>();
                        foreach (object s in singers)
                        {
                            var one = s as Dictionary<string, object>;
                            if (one == null) continue;
                            string singerName = Text(one, "name");
                            if (!string.IsNullOrEmpty(singerName)) names.Add(singerName);
                        }
                        artists = string.Join(" / ", names.ToArray());
                    }

                    string albumMid = "";
                    var album = Dig(song, "album") as Dictionary<string, object>;
                    if (album != null) albumMid = Text(album, "mid");
                    // 有些歌的专辑字段是平铺的 albummid / albumMid，兜底取一次
                    if (string.IsNullOrEmpty(albumMid)) albumMid = Text(song, "albummid");
                    if (string.IsNullOrEmpty(albumMid)) albumMid = Text(song, "albumMid");

                    int seconds = Number(song, "interval");
                    var info = new MusicSongInfo
                    {
                        Mid = mid,
                        Title = name,
                        Artists = artists,
                        AlbumMid = albumMid,
                        Duration = seconds > 0
                            ? (seconds / 60).ToString("0") + ":" + (seconds % 60).ToString("00")
                            : ""
                    };
                    result.Add(info);
                }
                Log("parse songs -> " + result.Count + " (skipped " + skipped + ")");
            }
            catch (Exception) { }
            return result;
        }

        static object Dig(object node, string key)
        {
            var dict = node as Dictionary<string, object>;
            if (dict == null) return null;
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        static string Text(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return "";
            object value = dict[key];
            return value == null ? "" : Convert.ToString(value);
        }

        static int Number(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return 0;
            object value = dict[key];
            if (value == null) return 0;
            int parsed;
            return int.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }

        // ------------------------------------------------------------ 页面脚本

        // 公共头：从 cookie 里取 uin 和 g_tk，接口认的是页面自己的登录态
        const string Common =
            "function ck(n){var m=document.cookie.match(new RegExp('(?:^|; )'+n+'=([^;]*)'));return m?m[1]:'';}" +
            "function hash33(s){var h=5381;for(var i=0;i<s.length;i++){h+=(h<<5)+s.charCodeAt(i);}return h&0x7fffffff;}" +
            "function post(body){return fetch('https://u.y.qq.com/cgi-bin/musicu.fcg?g_tk='+hash33(ck('qm_keyst'))+'&format=json'," +
            "{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),credentials:'include'})" +
            ".then(function(r){return r.text();});}";

        const string PlaylistScript =
            "(async function(){try{" + Common +
            "var uin=(ck('uin')||'').replace(/^o/,'');" +
            "var body={comm:{uin:uin,format:'json',ct:24,cv:0},req_0:{module:'music.musicasset.PlaylistBaseRead'," +
            "method:'GetPlaylistByUin',param:{uin:uin,encrypt_uin:'*'}}};" +
            "window.chrome.webview.postMessage(await post(body));" +
            "}catch(e){window.chrome.webview.postMessage('ERR '+e);}})()";

        // 取播放地址（vkey）：返回可直接播放的 URL，空字符串 = 没版权/VIP
        internal static void ResolvePlayUrl(MusicPlatform platform, string songMid, Action<string> done, Action<string> failed)
        {
            if (platform == null || platform.Id != "qq" || string.IsNullOrEmpty(songMid))
            {
                if (failed != null) failed("没有播放地址");
                return;
            }
            string script = PlayUrlScript.Replace("__SONGMID__", songMid);
            RunApi(platform, script, delegate(string json)
            {
                string url = ParsePlayUrl(json);
                if (string.IsNullOrEmpty(url))
                {
                    if (failed != null) failed("这首没有播放地址（版权或 VIP 限制）");
                    return;
                }
                if (done != null) done(url);
            }, failed);
        }

        static string ParsePlayUrl(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                var node = Dig(root, "req_0");
                if (node == null) node = Dig(root, "req");
                var data = Dig(node, "data");
                var list = Dig(data, "midurlinfo") as System.Collections.ArrayList;
                if (list == null || list.Count == 0) return "";
                var first = list[0] as Dictionary<string, object>;
                string purl = Text(first, "purl");
                // 接口给的常常是相对路径（C400xxx.m4a?vkey=...），要自己补 CDN 域名
                if (!string.IsNullOrEmpty(purl) && purl.IndexOf("://", StringComparison.Ordinal) < 0)
                {
                    purl = "http://ws.stream.qqmusic.qq.com/" + purl.TrimStart('/');
                }
                return purl;
            }
            catch (Exception) { return ""; }
        }

        // 播放地址：musicu.fcg 的 vkey 接口，返回 purl（空 = 没版权）
        const string PlayUrlScript =
            "(async function(){try{" + Common +
            "var uin=(ck('uin')||'').replace(/^o/,'');" +
            "var mid='__SONGMID__';" +
            "var body={comm:{uin:uin,format:'json',ct:24,cv:0},req_0:{module:'music.vkey.GetVkey',method:'UrlGetVkey'," +
            "param:{guid:'10000',songmid:[mid],songtype:[0],uin:uin,loginflag:1,platform:'20'}}};" +
            "window.chrome.webview.postMessage(await post(body));" +
            "}catch(e){window.chrome.webview.postMessage('ERR '+e);}})()";

        // 歌词：c.y.qq.com 的老接口，页面上下文里带 referer 调用
        const string LyricsScript =
            "(async function(){try{" +
            "var mid='__SONGMID__';" +
            "var url='https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid='+encodeURIComponent(mid)+" +
            "'&format=json&nobase64=1&g_tk=5381';" +
            "var r=await fetch(url,{credentials:'include'});var t=await r.text();" +
            "window.chrome.webview.postMessage(t);" +
            "}catch(e){window.chrome.webview.postMessage('ERR '+e);}})()";

        // 歌单详情：CgiGetDiss（uniform_get_Dissinfo 会返回 10006，实测这个才通）
        const string SongScript =
            "(async function(){try{" + Common +
            "var uin=(ck('uin')||'').replace(/^o/,'');" +
            "var dirid=parseInt('__DIRID__',10)||0;" +
            "var p={disstid:'__DISSTID__',song_begin:0,song_num:1000,userinfo:1,tag:1," +
            "inCharset:'utf-8',outCharset:'utf-8'};" +
            "if(dirid)p.dirid=dirid;" +
            "var body={comm:{uin:uin,format:'json',ct:24,cv:0},req_0:{module:'music.srfDissInfo.DissInfo'," +
            "method:'CgiGetDiss',param:p}};" +
            "window.chrome.webview.postMessage(await post(body));" +
            "}catch(e){window.chrome.webview.postMessage('ERR '+e);}})()";
    }
}
