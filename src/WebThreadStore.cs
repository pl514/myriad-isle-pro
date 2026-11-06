using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 胶囊会话 ↔ 网页版会话 的对应关系（build\data\webthreads.txt）
    //
    //   sessionId|site|url
    //   s20261106...|doubao|https://www.doubao.com/chat/3845...
    //
    // 为什么要这张表：
    //   网页版每开一次"新对话"就在对方账号里多一条记录。胶囊这边是同一个会话，
    //   但网页那边如果每次都新建，用久了会堆一堆碎片对话，还找不回来。
    //   记下 URL 之后，同一个胶囊会话永远回到同一个网页会话里。
    // ---------------------------------------------------------------------
    internal static class WebThreadStore
    {
        static string Path_()
        {
            return System.IO.Path.Combine(AiConfigStore.DataDir, "webthreads.txt");
        }

        static Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path_();
                if (!File.Exists(path)) return map;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    string key = parts[0].Trim() + "|" + parts[1].Trim();
                    string url = parts[2].Trim();
                    if (key.Length > 0 && url.Length > 0) map[key] = url;
                }
            }
            catch (Exception) { }
            return map;
        }

        static void Save(Dictionary<string, string> map)
        {
            try
            {
                string dir = AiConfigStore.DataDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("# sessionId|site|url");
                foreach (KeyValuePair<string, string> pair in map)
                {
                    string[] key = pair.Key.Split('|');
                    if (key.Length < 2) continue;
                    sb.Append(key[0]).Append('|').Append(key[1]).Append('|').Append(pair.Value).AppendLine();
                }
                File.WriteAllText(Path_(), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal static string Get(string sessionId, string site)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(site)) return null;
            Dictionary<string, string> map = Load();
            string url;
            return map.TryGetValue(sessionId + "|" + site, out url) ? url : null;
        }

        internal static void Set(string sessionId, string site, string url)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(site) || string.IsNullOrEmpty(url)) return;
            Dictionary<string, string> map = Load();
            map[sessionId + "|" + site] = url;
            Save(map);
        }

        internal static void Remove(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            Dictionary<string, string> map = Load();
            bool changed = false;
            var keys = new List<string>(map.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].StartsWith(sessionId + "|", StringComparison.OrdinalIgnoreCase))
                {
                    map.Remove(keys[i]);
                    changed = true;
                }
            }
            if (changed) Save(map);
        }

        // 删会话时用：把这条会话的所有绑定取出来并从文件里清掉
        // 返回值是 site → url，调用方拿去归档网页端
        internal static List<KeyValuePair<string, string>> Take(string sessionId)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(sessionId)) return result;
            Dictionary<string, string> map = Load();
            var keys = new List<string>(map.Keys);
            bool changed = false;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!keys[i].StartsWith(sessionId + "|", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = keys[i].Split('|');
                if (parts.Length >= 2) result.Add(new KeyValuePair<string, string>(parts[1], map[keys[i]]));
                map.Remove(keys[i]);
                changed = true;
            }
            if (changed) Save(map);
            return result;
        }
    }
}
