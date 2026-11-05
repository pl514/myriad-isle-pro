using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // AI 面板的数据层：消息 + 会话。
    // 这一阶段全部放在内存里，退出程序即清空（落盘是下一阶段的活）。
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

        // 渲染出来的气泡和气泡里的文字容器：流式时只更新这个容器，不重建整个会话。
        public Border Bubble;
        public StackPanel Body;
    }

    internal sealed class ChatSession
    {
        public string Id;
        public string Title = "新对话";
        public string Provider = "deepseek";
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
    }
}
