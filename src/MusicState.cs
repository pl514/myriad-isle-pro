using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace IslandShot
{
    // 音乐播放状态的持久化：关掉软件再打开，还能看到上次听到哪首、听到哪。
    internal sealed class MusicState
    {
        public string PlatformId = "";
        public string Mid = "";
        public string Title = "";
        public string Artists = "";
        public string AlbumMid = "";
        public double PositionSeconds;
        public bool WasPlaying;
        public string PlayMode = "shuffle";
        public string ShuffleKeys = "";      // 本轮随机已播的歌（| 分隔），保证重开还能接着一轮

        static string FilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "music-state.txt");
        }

        public static MusicState Load()
        {
            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return null;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                var state = new MusicState();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = Unescape(line.Substring(eq + 1));
                    if (key == "platform") state.PlatformId = value;
                    else if (key == "mid") state.Mid = value;
                    else if (key == "title") state.Title = value;
                    else if (key == "artists") state.Artists = value;
                    else if (key == "albumMid") state.AlbumMid = value;
                    else if (key == "position") double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out state.PositionSeconds);
                    else if (key == "playing") state.WasPlaying = value == "1";
                    else if (key == "mode") state.PlayMode = value;
                    else if (key == "shuffle") state.ShuffleKeys = value;
                }
                return state;
            }
            catch (Exception) { return null; }
        }

        public void Save()
        {
            try
            {
                string path = FilePath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("platform=" + Escape(PlatformId));
                sb.AppendLine("mid=" + Escape(Mid));
                sb.AppendLine("title=" + Escape(Title));
                sb.AppendLine("artists=" + Escape(Artists));
                sb.AppendLine("albumMid=" + Escape(AlbumMid));
                sb.AppendLine("position=" + PositionSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("playing=" + (WasPlaying ? "1" : "0"));
                sb.AppendLine("mode=" + Escape(PlayMode));
                sb.AppendLine("shuffle=" + Escape(ShuffleKeys));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\r", " ").Replace("\n", " ").Replace("|", "\\p");
        }

        static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    i++;
                    char n = value[i];
                    if (n == 'p') sb.Append('|');
                    else if (n == '\\') sb.Append('\\');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}