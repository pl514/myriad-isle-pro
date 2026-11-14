using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // AI 面板的数据层：消息 + 会话。
    // 内存里是唯一真身，chats.txt 只是快照（M5 起一批一批追加，见 ChatStore）。
    // ---------------------------------------------------------------------
    internal enum ChatRole
    {
        User,
        Ai
    }

    internal sealed class ChatMessage
    {
        public ChatRole Role;
        public string Text = "";
        public bool Streaming;
        public bool Failed;
        public bool Persisted;   // 已经写进 chats.txt

        // 这条消息带的图片（本地路径）。落盘时编码在文本前面，见 ChatStore。
        public readonly List<string> Images = new List<string>();

        // 渲染出来的气泡和气泡里的文字容器：流式时只更新这个容器，不重建整个会话。
        public Border Bubble;
        public StackPanel Body;
    }

    internal sealed class ChatSession
    {
        public string Id;
        public string Title = "新对话";
        public string Provider = "deepseek";
        // 这个会话锁定的模型（models.txt 里的 id）；空 = 老会话，不锁定
        public string ModelId = "";
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
    }
}
