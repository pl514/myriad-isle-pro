using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // M5：会话落盘（build\data\chats.txt）
    //
    // 行格式（一行一条消息，文本用 base64 以免被换行/竖线破坏）：
    //   sessionId|title|role|base64(utf8 text)|timestamp
    //
    // 写入时机：一条回答**结束之后**整条追加（不在流式过程中反复重写整个文件）。
    // 删除会话 / 超上限时才整体重写。
    // ---------------------------------------------------------------------
    internal static class ChatStore
    {
        internal const int MaxSessions = 50;
        internal const int MaxMessagesPerSession = 200;

        internal static string Path_()
        {
            return System.IO.Path.Combine(AiConfigStore.DataDir, "chats.txt");
        }

        static void EnsureDir()
        {
            try
            {
                if (!Directory.Exists(AiConfigStore.DataDir)) Directory.CreateDirectory(AiConfigStore.DataDir);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------- 读

        internal static List<ChatSession> Load()
        {
            var list = new List<ChatSession>();
            var index = new Dictionary<string, ChatSession>(StringComparer.Ordinal);
            try
            {
                string path = Path_();
                if (!File.Exists(path)) return list;

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 5) continue;

                    string id = parts[0].Trim();
                    if (id.Length == 0) continue;
                    string title = parts[1].Trim();
                    string role = parts[2].Trim();
                    string base64 = parts[3].Trim();
                    string modelField = parts.Length >= 6 ? parts[5].Trim() : "";

                    string text;
                    try { text = Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
                    catch (Exception) { continue; }

                    ChatSession session;
                    if (!index.TryGetValue(id, out session))
                    {
                        session = new ChatSession();
                        session.Id = id;
                        session.Title = title.Length == 0 ? "新对话" : title;
                        if (modelField.StartsWith("model:", StringComparison.Ordinal)) session.ModelId = modelField.Substring(6).Trim();
                        index[id] = session;
                        list.Add(session);
                    }

                    var message = new ChatMessage();
                    message.Role = role == "user" ? ChatRole.User : ChatRole.Ai;
                    SplitPayload(text, message);
                    message.Persisted = true;
                    session.Messages.Add(message);
                }
            }
            catch (Exception) { return new List<ChatSession>(); }

            // 修剪：会话按"文件里出现的顺序"保留最近的 N 个，每个会话保留最后 M 条
            while (list.Count > MaxSessions) list.RemoveAt(0);
            foreach (ChatSession session in list)
            {
                if (session.Messages.Count > MaxMessagesPerSession)
                {
                    session.Messages.RemoveRange(0, session.Messages.Count - MaxMessagesPerSession);
                }
            }
            return list;
        }

        // ------------------------------------------------------------- 写

        internal static void Append(ChatSession session, ChatMessage message)
        {
            if (session == null || message == null) return;
            try
            {
                EnsureDir();
                string line = BuildLine(session, message);
                File.AppendAllText(Path_(), line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        /// <summary>整体重写（删除会话、超上限时用）。</summary>
        internal static void SaveAll(List<ChatSession> sessions)
        {
            try
            {
                EnsureDir();
                var sb = new StringBuilder();
                sb.AppendLine("# sessionId|title|role|base64(text)|timestamp");

                int start = Math.Max(0, sessions.Count - MaxSessions);
                for (int i = start; i < sessions.Count; i++)
                {
                    ChatSession session = sessions[i];
                    if (session == null) continue;
                    int from = Math.Max(0, session.Messages.Count - MaxMessagesPerSession);
                    for (int j = from; j < session.Messages.Count; j++)
                    {
                        sb.AppendLine(BuildLine(session, session.Messages[j]));
                    }
                }
                File.WriteAllText(Path_(), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static string BuildLine(ChatSession session, ChatMessage message)
        {
            string text = message == null || message.Text == null ? "" : message.Text;
            // 图片用 [img] 前缀写在正文前面（行格式不用改，旧数据天然兼容）
            StringBuilder payload = new StringBuilder();
            if (message != null && message.Images.Count > 0)
            {
                for (int i = 0; i < message.Images.Count; i++)
                {
                    string file = message.Images[i];
                    if (string.IsNullOrEmpty(file)) continue;
                    payload.Append("[img]").Append(file.Replace("\r", "").Replace("\n", "")).Append('\n');
                }
            }
            payload.Append(text);

            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToString()));
            return Safe(session.Id) + "|" + Safe(session.Title) + "|"
                + (message != null && message.Role == ChatRole.User ? "user" : "ai") + "|"
                + base64 + "|" + Stamp() + "|" + (string.IsNullOrEmpty(session.ModelId) ? "" : "model:" + Safe(session.ModelId));
        }

        // 把正文前面的 [img] 行拆出来
        internal static void SplitPayload(string payload, ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(payload)) return;
            int offset = 0;
            while (payload.Length - offset > 5
                && string.CompareOrdinal(payload, offset, "[img]", 0, 5) == 0)
            {
                int eol = payload.IndexOf('\n', offset);
                if (eol < 0) break;
                string file = payload.Substring(offset + 5, eol - offset - 5).Trim();
                if (file.Length > 0) message.Images.Add(file);
                offset = eol + 1;
            }
            message.Text = payload.Substring(offset);
        }

        static string Stamp()
        {
            try { return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); }
            catch (Exception) { return "0"; }
        }

        // 竖线和换行会破坏行格式，标题里一律替换掉
        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("|", "／").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
