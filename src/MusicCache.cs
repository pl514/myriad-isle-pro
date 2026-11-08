using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IslandShot
{
    /// <summary>
    /// 歌单 / 歌曲的本地缓存。
    /// 点歌单以前每次都要走一遍 WebView2 接口请求（几秒），现在先读这里秒开，再后台刷新。
    /// 文件放在 build\data\music\cache\，一行一条，用 | 分隔。
    /// </summary>
    internal static class MusicCache
    {
        static string Root
        {
            get { return Path.Combine(AiConfigStore.DataDir, "music", "cache"); }
        }

        static void EnsureRoot()
        {
            string root = Root;
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        }

        static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("|", "／").Replace("\r", " ").Replace("\n", " ");
        }

        static string SongsPath(string platform, string id)
        {
            return Path.Combine(Root, "songs-" + platform + "-" + id + ".txt");
        }

        static string PlaylistsPath(string platform)
        {
            return Path.Combine(Root, "playlists-" + platform + ".txt");
        }

        public static void SaveSongs(string platform, string id, List<MusicSongInfo> songs)
        {
            if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(id) || songs == null) return;
            try
            {
                EnsureRoot();
                var builder = new StringBuilder();
                foreach (MusicSongInfo song in songs)
                {
                    if (song == null) continue;
                    builder.Append(Safe(song.Mid)).Append('|')
                        .Append(Safe(song.Title)).Append('|')
                        .Append(Safe(song.Artists)).Append('|')
                        .Append(Safe(song.Duration)).Append('|')
                        .Append(Safe(song.AlbumMid)).Append('\n');
                }
                File.WriteAllText(SongsPath(platform, id), builder.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public static List<MusicSongInfo> LoadSongs(string platform, string id)
        {
            if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(id)) return null;
            try
            {
                string path = SongsPath(platform, id);
                if (!File.Exists(path)) return null;
                var list = new List<MusicSongInfo>();
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 5) continue;
                    list.Add(new MusicSongInfo
                    {
                        Mid = parts[0],
                        Title = parts[1],
                        Artists = parts[2],
                        Duration = parts[3],
                        AlbumMid = parts[4]
                    });
                }
                return list.Count > 0 ? list : null;
            }
            catch (Exception) { return null; }
        }

        public static void SavePlaylists(string platform, List<MusicPlaylistInfo> list)
        {
            if (string.IsNullOrEmpty(platform) || list == null) return;
            try
            {
                EnsureRoot();
                var builder = new StringBuilder();
                foreach (MusicPlaylistInfo info in list)
                {
                    if (info == null) continue;
                    builder.Append(Safe(info.Id)).Append('|')
                        .Append(Safe(info.DirId)).Append('|')
                        .Append(Safe(info.Name)).Append('|')
                        .Append(info.Count).Append('|')
                        .Append(Safe(info.Url)).Append('\n');
                }
                File.WriteAllText(PlaylistsPath(platform), builder.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public static List<MusicPlaylistInfo> LoadPlaylists(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return null;
            try
            {
                string path = PlaylistsPath(platform);
                if (!File.Exists(path)) return null;
                var list = new List<MusicPlaylistInfo>();
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 5) continue;
                    int count = 0;
                    int.TryParse(parts[3], out count);
                    list.Add(new MusicPlaylistInfo
                    {
                        Id = parts[0],
                        DirId = parts[1],
                        Name = parts[2],
                        Count = count,
                        Url = parts[4]
                    });
                }
                return list.Count > 0 ? list : null;
            }
            catch (Exception) { return null; }
        }
    }
}
