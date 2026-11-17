using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IslandShot
{
    // 「一键放歌」用的歌单选择（data\quick-play.txt）
    internal static class QuickPlayStore
    {
        public static string PlaylistId = "";
        public static string PlaylistName = "";

        static string FilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "quick-play.txt");
        }

        public static void Load()
        {
            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key == "id") PlaylistId = value;
                    else if (key == "name") PlaylistName = value;
                }
            }
            catch (Exception) { }
        }

        public static void Save(string id, string name)
        {
            PlaylistId = id == null ? "" : id;
            PlaylistName = name == null ? "" : name;
            try
            {
                string path = FilePath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path,
                    "id=" + PlaylistId + Environment.NewLine +
                    "name=" + PlaylistName + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public static string DisplayName()
        {
            return string.IsNullOrEmpty(PlaylistName) ? "默认（第一个歌单）" : PlaylistName;
        }
    }
}