using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace IslandShot
{
    // 监听 DSH（DeepSeek Harness）的工作状态。
    // 实测：DSH 在一轮结束时不会写"结束标记"，会话文件只是停止更新。
    // 所以用"活动 → 静默"来判断：会话文件持续变化时算干活中，稳定 10 秒没变化就算这一轮结束。
    internal sealed class DshWatcher : IDisposable
    {
        const int IdleMs = 3000;       // 静默多久算结束（原来 10 秒太慢）
        const int PollMs = 2000;

        readonly Action onFinished;
        Timer timer;
        bool active;
        DateTime lastWriteUtc;
        bool primed;
        DateTime lastFireUtc = DateTime.MinValue;

        public DshWatcher(Action finished)
        {
            onFinished = finished;
        }

        public void Start()
        {
            if (timer != null) return;
            timer = new Timer(Poll, null, PollMs, PollMs);
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

                DateTime newest = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(folder, "session-*"))
                {
                    DateTime write = File.GetLastWriteTimeUtc(file);
                    if (write > newest) newest = write;
                }
                if (newest == DateTime.MinValue) return;

                if (!primed)
                {
                    primed = true;
                    lastWriteUtc = newest;
                    return;
                }

                if (newest > lastWriteUtc)
                {
                    // 有新活动
                    lastWriteUtc = newest;
                    active = true;
                    return;
                }

                if (!active) return;

                double silentMs = (DateTime.UtcNow - lastWriteUtc).TotalMilliseconds;
                if (silentMs < IdleMs) return;

                active = false;
                if ((DateTime.UtcNow - lastFireUtc).TotalMilliseconds < 5000) return;
                lastFireUtc = DateTime.UtcNow;
                DebugLog("dsh idle " + (int)(silentMs / 1000) + "s -> finished");
                Action handler = onFinished;
                if (handler != null) handler();
            }
            catch (Exception) { }
        }

        static void DebugLog(string message)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "messenger.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
