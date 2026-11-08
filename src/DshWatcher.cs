using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace IslandShot
{
    // 监听 DSH（DeepSeek Harness）的工作状态：一轮任务从"进行中"变成"结束"时回调一次。
    // DSH 会把会话状态写到 ~/.dsh/storages/session_projcache/sessions/session-*.json，
    // 其中 approvalState.turnOpen = true 表示该轮还在跑。
    internal sealed class DshWatcher : IDisposable
    {
        readonly Action onFinished;
        Timer timer;
        string lastFile;
        bool lastOpen;
        bool primed;

        public DshWatcher(Action finished)
        {
            onFinished = finished;
        }

        public void Start()
        {
            if (timer != null) return;
            timer = new Timer(Poll, null, 2500, 2500);
        }

        public void Dispose()
        {
            if (timer != null) { timer.Dispose(); timer = null; }
        }

        static string SessionFolder
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, ".dsh", "storages", "session_projcache", "sessions");
            }
        }

        void Poll(object state)
        {
            try
            {
                string folder = SessionFolder;
                if (!Directory.Exists(folder)) return;

                FileInfo newest = null;
                foreach (string file in Directory.GetFiles(folder, "session-*.json"))
                {
                    var info = new FileInfo(file);
                    if (newest == null || info.LastWriteTimeUtc > newest.LastWriteTimeUtc) newest = info;
                }
                if (newest == null) return;

                bool open = ReadTurnOpen(newest.FullName);
                string name = newest.FullName;

                if (!primed)
                {
                    primed = true;
                    lastFile = name;
                    lastOpen = open;
                    return;
                }

                // 同一个会话里：进行中 → 结束
                bool finished = name == lastFile && lastOpen && !open;
                // 换了一个会话文件：上一个会话已经结束
                if (name != lastFile && lastOpen) finished = true;

                lastFile = name;
                lastOpen = open;

                if (finished && onFinished != null) onFinished();
            }
            catch (Exception) { }
        }

        static bool ReadTurnOpen(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    // 只读前面一段就够了，避免整文件读入
                    var buffer = new char[64 * 1024];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    string text = new string(buffer, 0, Math.Max(0, read));
                    Match match = Regex.Match(text, "\"turnOpen\"\\s*:\\s*(true|false)");
                    if (!match.Success) return false;
                    return match.Groups[1].Value == "true";
                }
            }
            catch (Exception) { return false; }
        }
    }
}
