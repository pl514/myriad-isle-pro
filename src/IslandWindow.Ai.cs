using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // AI 面板（第一阶段：只做前端 + 假数据）
    //
    // 结构：顶栏（AI 助手 + 两个网页版球 + 关闭）× 左栏（新对话 + 历史）× 聊天区，底栏是输入框 + 模型 chip + 发送。
    // 全部是原生 WPF 控件——分层窗口里塞不进 WebView2（见交接包 §0）。
    //
    // 状态：expanded 仍表示"窗口展开态"，aiVisible 表示"展开的是 AI 面板"。
    // 收起一律走 SetExpanded(false)，窗口高度在收起动画结束后还原（IslandWindow.Motion.cs）。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        const double AiPanelWidth = 860;
        const double AiPanelHeight = 420;
        const double AiPanelTop = 8;
        const double AiSidebarWidth = 160;
        const double AiBubbleMaxWidth = 560;

        static readonly FontFamily AiFont = new FontFamily("Microsoft YaHei UI");
        static readonly Color AiTextColor = Color.FromRgb(230, 233, 240);
        static readonly Color AiMutedColor = Color.FromRgb(167, 160, 196);
        static readonly Color AiAccentColor = Color.FromRgb(124, 108, 242);
        static readonly Color AiBubbleColor = Color.FromRgb(38, 44, 54);
        static readonly Color AiUserBubbleColor = Color.FromRgb(108, 92, 226);

        Border aiPanel;
        Border aiChipButton;
        Border aiChipPopup;
        Border aiModelOptions;
        Border aiAddSheet;
        Border aiRulesSheet;
        StackPanel aiAttachmentStrip;
        bool aiPickMode;
        Border aiJumpLatest;
        Border aiPickBar;
        TextBlock aiPickCountText;
        Button aiPickSendButton;
        readonly List<string> aiPickOrder = new List<string>();
        Dictionary<string, Border> cardBadges;
        // 从图片面板「发送到 AI」带过来的图片（还没真正发给模型，先占位）
        readonly List<string> aiAttachments = new List<string>();
        Grid aiShell;
        Border aiSessionMenu;
        ChatSession aiSessionMenuTarget;
        Button aiSendButton;
        TextBox aiInput;
        TextBox aiAddNameBox;
        TextBox aiAddEndpointBox;
        TextBox aiAddModelBox;
        TextBlock aiAddSheetTitle;
        TextBlock aiAddKeyHint;
        Button aiDeleteModelButton;
        string aiEditingModelId;
        TextBox aiAddKeyBox;
        TextBlock aiModelChipText;
        TextBlock aiModelValueText;
        TextBlock aiReasoningValueText;
        Canvas aiReasoningSlider;
        Border aiReasoningKnob;
        TranslateTransform aiReasoningKnobMove;
        Border aiReasoningFill;
        ScaleTransform aiReasoningFillScale;
        bool aiReasoningDragging;
        TextBlock aiEmptyHint;
        StackPanel aiEmptyState;
        ScrollViewer aiChatScroll;
        StackPanel aiMessageList;
        StackPanel aiSessionList;
        Border[] aiOrbs;

        bool aiVisible;
        bool aiLastSurface;     // 上次收起时是 AI 面板 → 下次展开直接回到 AI
        bool aiStreaming;
        bool aiChipOpen;
        bool aiImeComposing;

        CancellationTokenSource aiCancellation;
        readonly List<ChatSession> aiSessions = new List<ChatSession>();
        readonly string[] aiOrbIds = new string[] { "deepseek", "doubao" };
        ChatSession aiCurrent;

        // 两条独立的选择：
        //   aiProvider  = 用哪个「网页版」入口（两个球，阶段三才真正接）
        //   aiModelId   = 用哪个 API 模型（models.txt 里的一条，阶段二真正发请求用它）
        List<AiModelConfig> aiModels = new List<AiModelConfig>();
        string aiModelId = "";
        string aiProvider = "deepseek";
        string aiModelLabel = "未配置模型";
        int aiReasoningIndex = 2;   // 0..4 → 低 / 中 / 高 / 极高 / 最高
        string aiLastUserText = "";

        // ================================================================ 构建

        Border BuildAiPanel()
        {
            var shell = new Grid();

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---------------------------------------------------------- 顶栏
            var header = new Grid { Margin = new Thickness(18, 0, 14, 0) };
            var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerLeft.Children.Add(new TextBlock
            {
                Text = "AI 助手",
                FontFamily = AiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AiTextColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerLeft.Children.Add(new Border
            {
                Width = 1,
                Height = 16,
                Margin = new Thickness(12, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerLeft.Children.Add(BuildAiOrbRow());

            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            // ← 返回图片面板（不收起窗口）；× 才是收起回胶囊
            var back = AiGhostButton("←");
            back.FontSize = 15;
            back.Margin = new Thickness(0, 0, 4, 0);
            back.ToolTip = "返回图片面板";
            back.Click += delegate { BackToImagePanel(); };
            headerRight.Children.Add(back);
            var close = AiGhostButton("×");
            close.FontSize = 15;
            close.ToolTip = "收起";
            close.Click += delegate { CloseAiPanel(); };
            headerRight.Children.Add(close);

            header.Children.Add(headerLeft);
            header.Children.Add(headerRight);
            var headerWrap = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = header
            };
            Grid.SetRow(headerWrap, 0);
            layout.Children.Add(headerWrap);

            // ---------------------------------------------------------- 主体
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AiSidebarWidth) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var sideGrid = new Grid();
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var newChat = ToolButton("＋ 新对话", 8);
            newChat.HorizontalAlignment = HorizontalAlignment.Stretch;
            newChat.Margin = new Thickness(0, 0, 0, 8);
            newChat.Click += delegate { NewAiChat(); };
            Grid.SetRow(newChat, 0);
            sideGrid.Children.Add(newChat);

            aiSessionList = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            var sessionScroll = new ScrollViewer
            {
                Content = aiSessionList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent
            };
            Grid.SetRow(sessionScroll, 1);
            sideGrid.Children.Add(sessionScroll);
            side.Child = sideGrid;
            Grid.SetColumn(side, 0);
            body.Children.Add(side);

            aiMessageList = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            aiChatScroll = new ScrollViewer
            {
                Content = aiMessageList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(18, 10, 18, 6),
                Background = Brushes.Transparent
            };
            aiEmptyHint = new TextBlock
            {
                Text = "问点什么吧",
                FontFamily = AiFont,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 167, 160, 196)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            var emptyIcon = new TextBlock
            {
                Text = "✧",
                FontFamily = AiFont,
                FontSize = 26,
                Foreground = new SolidColorBrush(Color.FromArgb(90, 124, 108, 242)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var emptySub = new TextBlock
            {
                Text = "Enter 发送 · Shift+Enter 换行",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(90, 167, 160, 196)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            aiEmptyState = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            aiEmptyState.Children.Add(emptyIcon);
            aiEmptyState.Children.Add(aiEmptyHint);
            aiEmptyState.Children.Add(emptySub);

            var chatHost = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            chatHost.Children.Add(aiChatScroll);
            chatHost.Children.Add(aiEmptyState);
            chatHost.Children.Add(BuildAiJumpLatest());
            aiChatScroll.ScrollChanged += delegate { UpdateAiJumpLatest(); };
            Grid.SetColumn(chatHost, 1);
            body.Children.Add(chatHost);

            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            // ---------------------------------------------------------- 底栏
            var footer = new Grid { MinHeight = 36, Margin = new Thickness(AiSidebarWidth + 4, 0, 14, 10) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var inputHost = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(6, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var inputGrid = new Grid();
            aiInput = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 28,
                MaxHeight = 92,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = AiFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(AiTextColor),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CaretBrush = new SolidColorBrush(AiAccentColor)
            };
            var placeholder = new TextBlock
            {
                Text = "输入你的问题…",
                FontFamily = AiFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(110, 167, 160, 196)),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            aiInput.TextChanged += delegate
            {
                placeholder.Visibility = aiInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            // 输入法开着的时候，所有按键都以 Key.ImeProcessed 到达（h / i / Enter 都一样），
            // 所以不能"看到 ImeProcessed 就返回"，否则回车永远发不出去。
            // 正确做法：取真实按键（ImeProcessedKey），再用组词状态区分"选词回车"和"发送回车"。
            TextCompositionManager.AddPreviewTextInputStartHandler(aiInput, delegate(object sender, TextCompositionEventArgs e)
            {
                aiImeComposing = true;
            });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(aiInput, delegate(object sender, TextCompositionEventArgs e)
            {
                if (e.TextComposition == null || e.TextComposition.CompositionText.Length == 0) aiImeComposing = false;
            });
            TextCompositionManager.AddPreviewTextInputHandler(aiInput, delegate(object sender, TextCompositionEventArgs e)
            {
                aiImeComposing = false;
            });

            aiInput.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
                if (key != Key.Enter || !e.IsDown) return;
                if (e.Key == Key.ImeProcessed && aiImeComposing) return;   // 这一下回车是输入法选词，放行
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return; // Shift+Enter 换行
                e.Handled = true;
                SubmitAiInput();
            };
            inputGrid.Children.Add(aiInput);
            inputGrid.Children.Add(placeholder);
            inputHost.Child = inputGrid;

            // 聚焦态：边框亮一点，像一个"正在输入"的框
            aiInput.GotFocus += delegate
            {
                inputHost.BorderBrush = new SolidColorBrush(Color.FromArgb(130, 124, 108, 242));
                inputHost.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            };
            aiInput.LostFocus += delegate
            {
                inputHost.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
                inputHost.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            };
            Grid.SetColumn(inputHost, 0);
            footer.Children.Add(inputHost);

            aiChipButton = BuildAiChipButton();
            Grid.SetColumn(aiChipButton, 1);
            footer.Children.Add(aiChipButton);

            aiSendButton = BuildAiSendButton();
            Grid.SetColumn(aiSendButton, 2);
            footer.Children.Add(aiSendButton);

            // 输入区上面的"已选图片"条（从图片面板「发送到 AI」带过来的）
            aiAttachmentStrip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(AiSidebarWidth + 10, 0, 14, 4),
                Visibility = Visibility.Collapsed
            };

            var footerStack = new StackPanel();
            footerStack.Children.Add(aiAttachmentStrip);
            footerStack.Children.Add(footer);

            var footerWrap = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 6, 0, 0),
                Child = footerStack
            };
            Grid.SetRow(footerWrap, 2);
            layout.Children.Add(footerWrap);

            // ---------------------------------------------------------- 浮层
            aiShell = shell;
            aiChipPopup = BuildAiChipPopup();
            aiAddSheet = BuildAiAddSheet();
            aiRulesSheet = BuildAiRulesSheet();
            aiSessionMenu = BuildAiSessionMenu();

            shell.Children.Add(layout);
            shell.Children.Add(aiChipPopup);
            shell.Children.Add(aiAddSheet);
            shell.Children.Add(aiRulesSheet);
            shell.Children.Add(aiSessionMenu);

            shell.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (aiSessionMenu != null && aiSessionMenu.Visibility == Visibility.Visible)
                {
                    if (!aiSessionMenu.IsMouseOver)
                    {
                        CloseAiSessionMenu();
                        e.Handled = true;
                        return;
                    }
                }
                if (!aiChipOpen) return;
                if (aiChipButton != null && aiChipButton.IsMouseOver) return;
                if (aiChipPopup != null && aiChipPopup.IsMouseOver) return;
                if (aiAddSheet != null && aiAddSheet.Visibility == Visibility.Visible && aiAddSheet.IsMouseOver) return;
                CloseAiChip();
                e.Handled = true;
            };

            var panelBorder = new Border
            {
                Width = AiPanelWidth,
                Height = AiPanelHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, AiPanelTop, 0, 0),
                CornerRadius = new CornerRadius(30),
                BorderBrush = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(
                    Color.FromArgb(250, 38, 44, 51),
                    Color.FromArgb(248, 27, 33, 41),
                    new Point(0, 0), new Point(0, 1)),
                Child = shell,
                Visibility = Visibility.Collapsed
            };
            // 聊天区、历史列表、输入框的滚动条统一换成 8px 细条，去掉默认的白色滑块和箭头按钮
            panelBorder.Resources[typeof(ScrollBar)] = BuildAiScrollBarStyle();
            return panelBorder;
        }

        // WPF 默认滚动条是浅色宽条 + 上下箭头，在深色面板里非常扎眼。
        // 这里只留一条 6px 的圆角滑块，悬停和拖动时变亮，轨道完全透明。
        static Style BuildAiScrollBarStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='8'/>" +
                "<Setter Property='MinWidth' Value='8'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>" +
                "<Track.Thumb>" +
                "<Thumb Width='6' HorizontalAlignment='Center'>" +
                "<Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border x:Name='bar' CornerRadius='3' Background='#2EFFFFFF'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bar' Property='Background' Value='#66FFFFFF'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='bar' Property='Background' Value='#A0FFFFFF'/></Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate></Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</Setter.Value></Setter>" +
                "</Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        StackPanel BuildAiOrbRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            aiOrbs = new Border[2];
            aiOrbs[0] = BuildAiOrb(0, "DeepSeek 网页版", Color.FromRgb(77, 107, 254), Color.FromRgb(138, 180, 255), "D");
            aiOrbs[1] = BuildAiOrb(1, "豆包网页版", Color.FromRgb(58, 214, 169), Color.FromRgb(14, 165, 233), "豆");
            row.Children.Add(aiOrbs[0]);
            row.Children.Add(aiOrbs[1]);
            row.Children.Add(BuildAiRulesOrb());
            return row;
        }

        // 小球右边那个「!」：点开网页版使用注意（怎么用才不容易被风控）
        Border BuildAiRulesOrb()
        {
            var orb = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "网页版使用注意",
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255))
            };
            orb.Child = new TextBlock
            {
                Text = "!",
                FontFamily = AiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(196, 186, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            orb.MouseEnter += delegate { orb.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)); };
            orb.MouseLeave += delegate { orb.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); };
            orb.MouseLeftButtonUp += delegate { ShowAiRules(); };
            return orb;
        }

        Border BuildAiOrb(int index, string name, Color from, Color to, string glyph)
        {
            string id = aiOrbIds[index];
            var orb = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Background = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
            };
            orb.Child = new TextBlock
            {
                Text = glyph,
                FontFamily = AiFont,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            orb.MouseLeftButtonUp += delegate { SetAiProvider(id); };
            return orb;
        }

        Border BuildAiChipButton()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            aiModelChipText = new TextBlock
            {
                Text = aiModelLabel,
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(AiTextColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(aiModelChipText);
            row.Children.Add(new TextBlock
            {
                Text = "⌄",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(AiMutedColor),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            var chip = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 8, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row
            };
            chip.MouseLeftButtonUp += delegate { ToggleAiChip(); };
            return chip;
        }

        Button BuildAiSendButton()
        {
            var button = new Button
            {
                Width = 76,
                Height = 32,
                FontSize = 12,
                FontFamily = AiFont,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                Content = "发送",
                Background = new LinearGradientBrush(
                    Color.FromRgb(124, 108, 242), Color.FromRgb(94, 82, 196),
                    new Point(0, 0), new Point(0, 1))
            };
            button.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='16'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='0.86'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>");
            button.Click += delegate
            {
                if (aiStreaming) StopAiStream();
                else SubmitAiInput();
            };
            return button;
        }

        Button AiGhostButton(string glyph)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 22,
                Height = 22,
                FontSize = 13,
                FontFamily = AiFont,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 210, 255)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='8'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='bg' Property='Background' Value='#33FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return button;
        }

        Border BuildAiMenuRow(string label, out TextBlock valueText, Action click)
        {
            var grid = new Grid { Height = 34 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = label,
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(AiMutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            valueText = new TextBlock
            {
                Text = "",
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(AiTextColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(name);
            grid.Children.Add(valueText);

            var row = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Tag = "ai-popup"
            };
            row.MouseLeftButtonUp += delegate { click(); };
            return row;
        }

        Border BuildAiChipPopup()
        {
            var stack = new StackPanel();

            var modelRow = BuildAiMenuRow("模型", out aiModelValueText, delegate { ToggleAiModelOptions(); });
            stack.Children.Add(modelRow);

            aiModelOptions = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(6, 0, 6, 2) };
            aiModelOptions.Child = new StackPanel();
            stack.Children.Add(aiModelOptions);

            stack.Children.Add(BuildAiReasoningBlock());
            stack.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255))
            });
            TextBlock editRowValue;
            stack.Children.Add(BuildAiMenuRow("编辑当前模型", out editRowValue, delegate { OpenAiModelSheet(CurrentAiModel()); }));
            TextBlock addRowValue;
            stack.Children.Add(BuildAiMenuRow("＋ 加入模型", out addRowValue, delegate { OpenAiModelSheet(null); }));

            var popup = new Border
            {
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 98, 52),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(32, 37, 45)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.45 },
                Visibility = Visibility.Collapsed,
                Child = stack
            };
            return popup;
        }

        // ---------------------------------------------------------- 推理等级滑块

        const double AiSliderWidth = 244;
        const double AiSliderPad = 6;
        static readonly string[] AiReasoningLevels = new string[] { "低", "中", "高", "极高", "最高" };

        static string ReasoningName(int index)
        {
            if (index < 0) index = 0;
            if (index > AiReasoningLevels.Length - 1) index = AiReasoningLevels.Length - 1;
            return AiReasoningLevels[index];
        }

        // 五档 → OpenAI 的 reasoning_effort
        string ReasoningEffort()
        {
            switch (aiReasoningIndex)
            {
                case 0: return "minimal";
                case 1: return "low";
                case 2: return "medium";
                case 3: return "high";
                default: return "xhigh";
            }
        }

        static double ReasoningStopX(int index)
        {
            double gap = (AiSliderWidth - 2 * AiSliderPad) / (AiReasoningLevels.Length - 1);
            return AiSliderPad + index * gap;
        }

        // 自己画的滑块：一条细轨 + 五个吸点 + 一颗带辉光的旋钮。
        // 不照抄参考图（那套是纯渐变铺满），这里用"已选段才上色"的表达，更安静。
        StackPanel BuildAiReasoningBlock()
        {
            var block = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

            var head = new Grid { Height = 26 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock
            {
                Text = "推理等级",
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(AiMutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            aiReasoningValueText = new TextBlock
            {
                Text = ReasoningName(aiReasoningIndex),
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(196, 186, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(aiReasoningValueText, 1);
            head.Children.Add(aiReasoningValueText);
            block.Children.Add(head);

            aiReasoningSlider = new Canvas
            {
                Width = AiSliderWidth,
                Height = 20,
                Margin = new Thickness(8, 0, 8, 2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };

            var track = new Border
            {
                Width = AiSliderWidth,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                // 整条轨道用主色的暗版，而不是灰白——这样"未选段"也属于这套配色
                Background = new SolidColorBrush(Color.FromArgb(70, 124, 108, 242))
            };
            Canvas.SetTop(track, 9);
            aiReasoningSlider.Children.Add(track);

            aiReasoningFill = new Border
            {
                Width = AiSliderWidth,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new LinearGradientBrush(
                    Color.FromRgb(124, 108, 242), Color.FromRgb(186, 166, 255),
                    new Point(0, 0), new Point(1, 0)),
                RenderTransformOrigin = new Point(0, 0.5)
            };
            aiReasoningFillScale = new ScaleTransform(1, 1);
            aiReasoningFill.RenderTransform = aiReasoningFillScale;
            Canvas.SetTop(aiReasoningFill, 9);
            aiReasoningSlider.Children.Add(aiReasoningFill);

            for (int i = 0; i < AiReasoningLevels.Length; i++)
            {
                var dot = new Border
                {
                    Width = 3,
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                };
                Canvas.SetLeft(dot, ReasoningStopX(i) - 1.5);
                Canvas.SetTop(dot, 9.5);
                aiReasoningSlider.Children.Add(dot);
            }

            aiReasoningKnob = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.White,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9, Color = AiAccentColor }
            };
            aiReasoningKnobMove = new TranslateTransform(ReasoningStopX(aiReasoningIndex) - 7, 0);
            aiReasoningKnob.RenderTransform = aiReasoningKnobMove;
            Canvas.SetTop(aiReasoningKnob, 4);
            aiReasoningKnob.IsHitTestVisible = false;
            aiReasoningSlider.Children.Add(aiReasoningKnob);

            aiReasoningFillScale.ScaleX = ReasoningStopX(aiReasoningIndex) / AiSliderWidth;

            aiReasoningSlider.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                aiReasoningDragging = true;
                try { aiReasoningSlider.CaptureMouse(); } catch (Exception) { }
                SetAiReasoningFromX(e.GetPosition(aiReasoningSlider).X);
            };
            aiReasoningSlider.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!aiReasoningDragging) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    aiReasoningDragging = false;
                    return;
                }
                SetAiReasoningFromX(e.GetPosition(aiReasoningSlider).X);
            };
            aiReasoningSlider.MouseLeftButtonUp += delegate
            {
                aiReasoningDragging = false;
                try { aiReasoningSlider.ReleaseMouseCapture(); } catch (Exception) { }
            };

            block.Children.Add(aiReasoningSlider);
            return block;
        }

        void SetAiReasoningFromX(double x)
        {
            double gap = (AiSliderWidth - 2 * AiSliderPad) / (AiReasoningLevels.Length - 1);
            int index = (int)Math.Round((x - AiSliderPad) / gap);
            SetAiReasoning(index);
        }

        void SetAiReasoning(int index)
        {
            if (index < 0) index = 0;
            if (index > AiReasoningLevels.Length - 1) index = AiReasoningLevels.Length - 1;
            bool changed = index != aiReasoningIndex;
            aiReasoningIndex = index;

            if (aiReasoningValueText != null) aiReasoningValueText.Text = ReasoningName(index);

            if (aiReasoningFillScale != null)
            {
                double target = ReasoningStopX(index) / AiSliderWidth;
                Motion.AnimateDouble(aiReasoningFillScale, ScaleTransform.ScaleXProperty,
                    aiReasoningFillScale.ScaleX, target, changed ? 150 : 0, Motion.Enter, 0, null);
            }
            if (aiReasoningKnobMove != null)
            {
                Motion.AnimateDouble(aiReasoningKnobMove, TranslateTransform.XProperty,
                    aiReasoningKnobMove.X, ReasoningStopX(index) - 7, changed ? 150 : 0, Motion.Enter, 0, null);
            }
        }

        Border BuildAiModelOption(AiModelConfig model)
        {
            bool current = model.Id == aiModelId;
            bool missingKey = !string.IsNullOrEmpty(model.KeyRef) && !AiConfigStore.HasKey(model.KeyRef);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 当前模型用一个小圆点标记，不整行高亮
            grid.Children.Add(new Border
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(current ? AiAccentColor : Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var label = new TextBlock
            {
                Text = model.DisplayName(),
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(current ? AiTextColor : AiMutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            if (missingKey)
            {
                var warn = new TextBlock
                {
                    Text = "缺 Key",
                    FontFamily = AiFont,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(240, 180, 120)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 8, 0)
                };
                Grid.SetColumn(warn, 2);
                grid.Children.Add(warn);
            }

            var row = new Border
            {
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = "ai-popup",
                Child = grid
            };
            AiModelConfig captured = model;
            row.MouseLeftButtonUp += delegate
            {
                ApplyAiModel(captured);
                CloseAiChip();
            };
            // 右键 = 直接编辑这一条（填/改 Key）
            row.MouseRightButtonUp += delegate
            {
                OpenAiModelSheet(captured);
            };
            return row;
        }

        Border BuildAiAddSheet()
        {
            var card = new Border
            {
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(32, 37, 45)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 16, 18, 14),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.45 }
            };

            var stack = new StackPanel();
            aiAddSheetTitle = new TextBlock
            {
                Text = "加入模型",
                FontFamily = AiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AiTextColor)
            };
            stack.Children.Add(aiAddSheetTitle);
            stack.Children.Add(new TextBlock
            {
                Text = "保存后写入 data\\models.txt，Key 用 DPAPI 加密写进 data\\keys.txt。",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(AiMutedColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });

            stack.Children.Add(AiFieldLabel("显示名称（例子：智谱 GLM-4.7-Flash）"));
            aiAddNameBox = AiFieldBox();
            stack.Children.Add(aiAddNameBox);

            stack.Children.Add(AiFieldLabel("接口地址（例子：https://open.bigmodel.cn/api/paas/v4）"));
            aiAddEndpointBox = AiFieldBox();
            stack.Children.Add(aiAddEndpointBox);

            stack.Children.Add(AiFieldLabel("模型 ID（例子：glm-4.7-flash）"));
            aiAddModelBox = AiFieldBox();
            stack.Children.Add(aiAddModelBox);

            aiAddKeyHint = AiFieldLabel("API Key（可以留空，之后手改 keys.txt）");
            stack.Children.Add(aiAddKeyHint);
            aiAddKeyBox = AiFieldBox();
            stack.Children.Add(aiAddKeyBox);

            var buttons = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            aiDeleteModelButton = ToolButton("删除", 10);
            aiDeleteModelButton.HorizontalAlignment = HorizontalAlignment.Left;
            aiDeleteModelButton.Foreground = new SolidColorBrush(Color.FromRgb(240, 130, 130));
            aiDeleteModelButton.Visibility = Visibility.Collapsed;
            aiDeleteModelButton.Click += delegate { DeleteEditingModel(); };
            Grid.SetColumn(aiDeleteModelButton, 0);
            buttons.Children.Add(aiDeleteModelButton);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = ToolButton("取消", 10);
            cancel.Click += delegate { CloseAiAddSheet(); };
            var save = ToolButton("保存", 10);
            save.Click += delegate { ConfirmAiAddModel(); };
            right.Children.Add(cancel);
            right.Children.Add(save);
            Grid.SetColumn(right, 1);
            buttons.Children.Add(right);
            stack.Children.Add(buttons);

            card.Child = stack;

            var layer = new Grid();
            layer.Children.Add(card);
            layer.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!card.IsMouseOver) CloseAiAddSheet();
            };

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 8, 10, 14)),
                CornerRadius = new CornerRadius(30),
                Child = layer,
                Visibility = Visibility.Collapsed
            };
        }

        // ---------------------------------------------------------- 图片 → AI
        // ---------------------------------------------------------- 图片 → AI

        // 右键「发送到 AI」：**不跳转**，先在图片面板里进入"选图"状态。
        // 点卡片按顺序标号（1/2/3…），点右下角那个按钮才切到 AI 面板。
        internal void BeginAiPick(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                ShowToolMessage("发送到 AI", "这张图片已经不在了（可能被移走或删除）。");
                return;
            }
            if (!expanded) SetExpanded(true);
            SetTab(false);                      // 确保在图片页
            aiPickMode = true;
            aiPickOrder.Clear();
            aiPickOrder.Add(path);
            UpdateAiPickUi();
        }

        internal bool AiPickMode { get { return aiPickMode; } }

        internal void ToggleAiPick(string path)
        {
            if (!aiPickMode || string.IsNullOrEmpty(path)) return;
            if (IsVideoFile(path))
            {
                ShowToolMessage("发送到 AI", "视频还不能发给模型，先选图片。");
                return;
            }
            if (aiPickOrder.Contains(path))
            {
                aiPickOrder.Remove(path);
            }
            else
            {
                if (aiPickOrder.Count >= 4)
                {
                    ShowToolMessage("发送到 AI", "一次最多带 4 张图片。");
                    return;
                }
                aiPickOrder.Add(path);
            }
            UpdateAiPickUi();
        }

        internal void CancelAiPick()
        {
            if (!aiPickMode) return;
            aiPickMode = false;
            aiPickOrder.Clear();
            UpdateAiPickUi();
        }

        // 号码牌 + 底部按钮条
        internal void UpdateAiPickUi()
        {
            if (cardBadges != null)
            {
                foreach (KeyValuePair<string, Border> pair in cardBadges)
                {
                    Border badge = pair.Value;
                    if (badge == null) continue;
                    int index = aiPickOrder.FindIndex(delegate(string item)
                    {
                        return string.Equals(item, pair.Key, StringComparison.OrdinalIgnoreCase);
                    });
                    if (aiPickMode && index >= 0)
                    {
                        var text = badge.Child as TextBlock;
                        if (text != null) text.Text = (index + 1).ToString();
                        badge.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        badge.Visibility = Visibility.Collapsed;
                    }
                }
            }

            if (aiPickBar != null)
            {
                aiPickBar.Visibility = aiPickMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (aiPickCountText != null)
            {
                aiPickCountText.Text = aiPickMode
                    ? (aiPickOrder.Count == 0 ? "点图片选择要发的张数" : "已选 " + aiPickOrder.Count + " 张")
                    : "";
            }
            if (aiPickSendButton != null)
            {
                aiPickSendButton.IsEnabled = aiPickOrder.Count > 0;
                aiPickSendButton.Opacity = aiPickOrder.Count > 0 ? 1.0 : 0.45;
            }
        }

        // 点"发送到 AI"：这时才切面板，把选中的图按顺序挂到输入框上方
        internal void ConfirmAiPick()
        {
            var picked = new List<string>(aiPickOrder);
            aiPickMode = false;
            aiPickOrder.Clear();
            UpdateAiPickUi();
            if (picked.Count == 0) return;

            OpenAiPanel();
            for (int i = 0; i < picked.Count; i++) AddAiAttachment(picked[i]);

            // 图片只有网页版能发：当前是 API 模型就顺手切过去，省得发的时候被拦
            if (!CurrentModelIsWeb())
            {
                AiModelConfig web = FirstWebModel();
                if (web != null)
                {
                    ApplyAiModel(web);
                    ShowToolMessage("发送到 AI", "已切到「" + web.DisplayName() + "」（图片要网页版才能发）");
                }
            }
            FocusAiInput();
        }

        AiModelConfig FirstWebModel()
        {
            for (int i = 0; i < aiModels.Count; i++)
            {
                AiModelConfig model = aiModels[i];
                if (model != null && WebSiteOf(model).Length > 0) return model;
            }
            return null;
        }


        void AddAiAttachment(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < aiAttachments.Count; i++)
            {
                if (string.Equals(aiAttachments[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    RenderAiAttachments();
                    return;
                }
            }
            if (aiAttachments.Count >= 4)
            {
                ShowToolMessage("发送到 AI", "一次最多带 4 张图片。");
                return;
            }
            aiAttachments.Add(path);
            RenderAiAttachments();
        }

        void RenderAiAttachments()
        {
            if (aiAttachmentStrip == null) return;
            aiAttachmentStrip.Children.Clear();
            foreach (string path in aiAttachments)
            {
                string captured = path;
                var thumb = new Border
                {
                    Width = 42,
                    Height = 42,
                    CornerRadius = new CornerRadius(8),
                    ClipToBounds = true,
                    Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255))
                };
                try { thumb.Child = new Image { Source = LoadAiThumb(captured, 84), Stretch = Stretch.UniformToFill }; }
                catch (Exception) { }

                var close = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(220, 30, 32, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -5, -5, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "×",
                        FontFamily = AiFont,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(AiTextColor),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                close.MouseLeftButtonUp += delegate
                {
                    aiAttachments.Remove(captured);
                    RenderAiAttachments();
                };

                var host = new Grid { Margin = new Thickness(0, 0, 8, 0), ToolTip = captured };
                host.Children.Add(thumb);
                host.Children.Add(close);
                aiAttachmentStrip.Children.Add(host);
            }
            aiAttachmentStrip.Visibility = aiAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static BitmapImage LoadAiThumb(string path, int decodeWidth)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 别锁文件
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        // 图片面板"清空图片"之后：输入框上方挂着的图也一起清掉（文件已经没了）
        internal void ClearAiAttachmentsFromDeletedFiles()
        {
            if (aiAttachments.Count == 0) return;
            aiAttachments.Clear();
            RenderAiAttachments();
        }

        // 网页版使用注意（点小球右边那个「!」）
        Border BuildAiRulesSheet()
        {
            var card = new Border
            {
                Width = 330,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(32, 37, 45)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20, 16, 20, 14),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.45 }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "网页版使用注意",
                FontFamily = AiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AiTextColor)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "这两个球是「自动操作网页」，不是官方接口。按下面用，风险最低：",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(AiMutedColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 10)
            });

            string[] rules = new string[]
            {
                "一次只问一条，隔几秒再发，不连发、不并发。",
                "弹出验证码/滑块就停下，手动处理，别连着重试。",
                "只在自己电脑上用自己账号，别多开、别外借。",
                "别拿它当产品对外接口——那是必封的。",
                "出现限流就先切回 API 模型，过一阵再试。",
                "网页改版可能失效，报错就用回 API。"
            };
            foreach (string rule in rules)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "·  " + rule,
                    FontFamily = AiFont,
                    FontSize = 11,
                    LineHeight = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(206, 210, 220)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = "API 模型是官方通道，没这类风险，日常优先用它。",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(158, 190, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 12)
            });

            var ok = ToolButton("知道了", 10);
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Click += delegate { CloseAiRules(); };
            stack.Children.Add(ok);

            card.Child = stack;

            var sheet = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                Visibility = Visibility.Collapsed,
                Child = card
            };
            // 点卡片外面 = 收起
            sheet.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!card.IsMouseOver) CloseAiRules();
            };
            return sheet;
        }

        void ShowAiRules()
        {
            if (aiRulesSheet == null) return;
            aiRulesSheet.Visibility = Visibility.Visible;
            Motion.PopIn(aiRulesSheet, new Point(0.5, 0.5), 0.96, 6, 170, 0, Motion.Enter);
        }

        void CloseAiRules()
        {
            if (aiRulesSheet == null) return;
            aiRulesSheet.Visibility = Visibility.Collapsed;
        }

        TextBlock AiFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(AiMutedColor),
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        TextBox AiFieldBox()
        {
            return new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 2, 0, 8),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 12,
                FontFamily = AiFont,
                Foreground = new SolidColorBrush(AiTextColor),
                Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                CaretBrush = new SolidColorBrush(AiAccentColor)
            };
        }

        // ================================================================ 开关与高度

        void InitAiSessions()
        {
            // M5：先把上次的会话读回来（chats.txt），没有就开一个新的
            List<ChatSession> loaded = ChatStore.Load();
            aiSessions.Clear();
            if (loaded.Count > 0)
            {
                aiSessions.AddRange(loaded);
                aiCurrent = aiSessions[aiSessions.Count - 1];
            }
            else
            {
                aiCurrent = new ChatSession();
                aiCurrent.Id = "s" + DateTime.Now.ToString("yyyyMMddHHmmss");
                aiCurrent.Title = "新对话";
                aiCurrent.Provider = aiProvider;
                aiSessions.Add(aiCurrent);
            }

            RenderAiSessions();
            RenderChat();
            UpdateAiOrbSelection();
            LoadAiModels();
            UpdateAiSendButton();
        }

        // ---------------------------------------------------------- 模型配置

        void LoadAiModels()
        {
            aiModels = AiConfigStore.LoadModels();
            AiModelConfig model = null;
            for (int i = 0; i < aiModels.Count; i++)
            {
                if (aiModels[i].Id == aiModelId) { model = aiModels[i]; break; }
            }
            if (model == null && aiModels.Count > 0) model = aiModels[0];
            ApplyAiModel(model);
        }

        AiModelConfig CurrentAiModel()
        {
            for (int i = 0; i < aiModels.Count; i++)
            {
                if (aiModels[i].Id == aiModelId) return aiModels[i];
            }
            return aiModels.Count > 0 ? aiModels[0] : null;
        }

        void ApplyAiModel(AiModelConfig model)
        {
            if (model == null) return;
            aiModelId = model.Id;
            aiModelLabel = model.DisplayName();
            aiProvider = WebSiteOf(model);   // 网页版模型时同步球的状态；API 模型置空
            UpdateAiChipTexts();
            RebuildAiModelOptions();
            UpdateAiOrbSelection();
        }

        // 从模型配置反推网页版站点；不是网页版模型就返回空串
        static string WebSiteOf(AiModelConfig model)
        {
            if (model == null || model.BaseUrl == null) return "";
            if (!model.BaseUrl.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return "";
            return model.BaseUrl.Substring(6).Trim('/');
        }

        // 当前模型是不是网页版（只有网页版能吃图片）
        bool CurrentModelIsWeb()
        {
            return WebSiteOf(CurrentAiModel()).Length > 0;
        }

        void RebuildAiModelOptions()
        {
            if (aiModelOptions == null) return;
            var stack = new StackPanel();
            foreach (AiModelConfig model in aiModels)
            {
                stack.Children.Add(BuildAiModelOption(model));
            }
            aiModelOptions.Child = stack;
        }

        void OpenAiPanel()
        {
            if (aiPanel == null) return;
            if (aiVisible && expanded && aiPanel.Visibility == Visibility.Visible)
            {
                FocusAiInput();
                return;
            }

            bool fromImagePanel = expanded;
            aiVisible = true;
            aiLastSurface = true;
            EnsureAiShadow();
            if (panel != null) panel.Visibility = Visibility.Collapsed;
            ApplyAiWindowHeight(true);

            if (fromImagePanel)
            {
                // 从图片面板里点球进来：不走胶囊形变，直接淡入
                aiPanel.Visibility = Visibility.Visible;
                aiPanel.IsHitTestVisible = true;
                aiPanel.Opacity = 0;
                // 关键：上一次收起把内容层（shell）的 Opacity 淡到了 0，
                // 这条路径必须自己把它淡回来，否则第二次打开只剩一块底色，看着像黑屏。
                UIElement aiBody = aiPanel.Child as UIElement;
                if (aiBody != null) Motion.AnimateDouble(aiBody, UIElement.OpacityProperty, 0, 1, 220, Motion.Enter, 40, null);
                Motion.PopIn(aiPanel, new Point(0.5, 0.15), 0.96, 10, 200, 0, Motion.Drop);
            }
            else
            {
                SetExpanded(true);
            }

            FocusAiInput();
        }

        // 返回图片面板：窗口保持展开态，只是把内容从 AI 面板换回图片面板。
        // 注意不能走 SetExpanded(true)——expanded 已经是 true，会直接 return。
        void BackToImagePanel()
        {
            if (!aiVisible) return;
            if (aiChipOpen) CloseAiChip();
            CloseAiAddSheet();
            if (aiStreaming) StopAiStream();

            aiVisible = false;
            aiLastSurface = false;
            // 先淡出、再 Collapsed、最后摘掉投影：三步顺序不能乱，
            // 否则分层窗口会残留 AI 面板的阴影（DropShadowEffect 的缓存）。
            Motion.Fade(aiPanel, 0, 120, Motion.Exit, 0, delegate
            {
                HideAiSurface();
            });

            if (panel == null) return;
            panel.Visibility = Visibility.Visible;
            panel.IsHitTestVisible = true;
            // 上次收起把面板缩到了胶囊大小，这里要先把形变复位，否则会以极小的尺寸淡入
            Motion.Prepare(panel, PanelMorphOrigin);
            ScaleTransform scale = Motion.ScaleOf(panel, PanelMorphOrigin);
            TranslateTransform move = Motion.MoveOf(panel);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            move.Y = 0;
            panel.Opacity = 0;
            Motion.Fade(panel, 1, 180, Motion.Enter, 60, null);
        }

        // WPF 的 DropShadowEffect 会缓存元素内容：面板隐藏后那块缓存仍可能被画出来，
        // 表现为"返回图片面板后，下面残留一条 AI 面板的阴影"。所以隐藏时把投影摘掉。
        void EnsureAiShadow()
        {
            if (aiPanel == null) return;
            if (aiPanel.Effect == null)
                aiPanel.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Opacity = 0.4 };
        }

        void HideAiSurface()
        {
            if (aiPanel == null) return;
            aiPanel.Visibility = Visibility.Collapsed;
            aiPanel.Effect = null;
        }

        void CloseAiPanel()
        {
            if (!aiVisible) return;
            if (aiChipOpen) CloseAiChip();
            CloseAiAddSheet();
            CloseAiRules();
            CancelAiPick();
            if (aiStreaming) StopAiStream();
            if (!expanded)
            {
                aiVisible = false;
                ApplyAiWindowHeight(false);
                HideAiSurface();
                return;
            }
            SetExpanded(false);
        }

        void ApplyAiWindowHeight(bool ai)
        {
            // 方案 B：窗口在构造时就定好高度，运行时一律不改 Height。
            // 之前在这里 Height = 260 ↔ 440 来回改，分层窗口 resize 后会出现整块黑屏。
            if (!ai || aiPanel == null) return;
            double avail = MaxWindowHeight() - AiPanelTop - 12;
            aiPanel.Height = Math.Min(AiPanelHeight, Math.Max(300, avail));
        }

        double MaxWindowHeight()
        {
            try
            {
                Forms.Screen screen = Forms.Screen.PrimaryScreen;
                double dpi = DpiScale();
                return Math.Max(300, screen.WorkingArea.Height / dpi - 4);
            }
            catch (Exception) { return AiPanelHeight + AiPanelTop + 12; }
        }

        void FocusAiInput()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Activate();
                    if (aiInput == null) return;
                    Keyboard.Focus(aiInput);
                    aiInput.CaretIndex = aiInput.Text.Length;
                }
                catch (Exception) { }
            }), DispatcherPriority.Input);
        }

        // ================================================================ 左栏会话

        void NewAiChat()
        {
            if (aiStreaming) StopAiStream();
            if (aiCurrent != null && aiCurrent.Messages.Count == 0)
            {
                FocusAiInput();
                return;
            }
            var session = new ChatSession();
            session.Id = NewSessionId();
            session.Title = "新对话";
            session.Provider = aiProvider;
            aiSessions.Add(session);
            aiCurrent = session;
            RenderAiSessions();
            RenderChat();
            FocusAiInput();
        }

        static string NewSessionId()
        {
            return "s" + DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        void SwitchAiSession(ChatSession session)
        {
            if (session == null || session == aiCurrent) return;
            if (aiStreaming) StopAiStream();
            aiCurrent = session;
            UpdateAiOrbSelection();
            RenderAiSessions();
            // 换会话保留旧的滚动偏移没意义：直接把最新一条露出来
            try { if (aiChatScroll != null) aiChatScroll.ScrollToVerticalOffset(0); }
            catch (Exception) { }
            RenderChat();
            ScrollAiToEnd();
        }

        void RenderAiSessions()
        {
            if (aiSessionList == null) return;
            aiSessionList.Children.Clear();
            for (int i = 0; i < aiSessions.Count; i++)
            {
                ChatSession session = aiSessions[i];
                bool active = session == aiCurrent;

                // 当前会话用左侧 3px 强调条表示，而不是整块高亮底色——安静一点
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border
                {
                    Width = 3,
                    Height = 14,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(AiAccentColor),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = active ? Visibility.Visible : Visibility.Collapsed
                });
                var label = new TextBlock
                {
                    Text = session.Title,
                    FontFamily = AiFont,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(active ? AiTextColor : AiMutedColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);

                var item = new Border
                {
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "左键切换 · 右键删除",
                    Background = active ? new SolidColorBrush(Color.FromArgb(26, 124, 108, 242)) : Brushes.Transparent,
                    Child = row
                };
                ChatSession captured = session;
                item.MouseEnter += delegate
                {
                    if (!active) item.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
                };
                item.MouseLeave += delegate
                {
                    if (!active) item.Background = Brushes.Transparent;
                };
                item.MouseLeftButtonUp += delegate { SwitchAiSession(captured); };
                item.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    OpenAiSessionMenu(captured, e.GetPosition(aiShell));
                };
                aiSessionList.Children.Add(item);
            }
        }

        // 右键会话弹出的迷你菜单（面板内叠层，不用 ContextMenu：分层窗口里它会跑到所有窗口最上层）
        Border BuildAiSessionMenu()
        {
            var stack = new StackPanel();
            var row = new Border
            {
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = "ai-popup",
                Child = new TextBlock
                {
                    Text = "删除会话",
                    FontFamily = AiFont,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(240, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                }
            };
            row.MouseLeftButtonUp += delegate
            {
                ChatSession target = aiSessionMenuTarget;
                CloseAiSessionMenu();
                DeleteAiSession(target);
            };
            row.MouseEnter += delegate { row.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            stack.Children.Add(row);

            return new Border
            {
                Width = 130,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(38, 43, 52)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.5 },
                Visibility = Visibility.Collapsed,
                Child = stack
            };
        }

        void OpenAiSessionMenu(ChatSession session, Point at)
        {
            if (aiSessionMenu == null || aiShell == null) return;
            aiSessionMenuTarget = session;
            CloseAiChip();

            double maxX = Math.Max(0, aiShell.ActualWidth - 140);
            double maxY = Math.Max(0, aiShell.ActualHeight - 50);
            double x = Math.Max(0, Math.Min(at.X, maxX));
            double y = Math.Max(0, Math.Min(at.Y, maxY));
            aiSessionMenu.Margin = new Thickness(x, y, 0, 0);
            aiSessionMenu.Visibility = Visibility.Visible;
        }

        void CloseAiSessionMenu()
        {
            if (aiSessionMenu != null) aiSessionMenu.Visibility = Visibility.Collapsed;
            aiSessionMenuTarget = null;
        }

        // 删除一个会话：删掉当前会话时自动切到最近的一个；删空了就新建一个空会话
        // 删掉胶囊会话后，顺手把网页版那边的对话归档掉（不然对方账号里会越积越多）
        async void ArchiveWebThreads(List<KeyValuePair<string, string>> threads)
        {
            int archived = 0;
            for (int i = 0; i < threads.Count; i++)
            {
                try
                {
                    if (await WebBackend.TryArchiveAsync(threads[i].Key, threads[i].Value)) archived++;
                }
                catch (Exception) { }
            }
            if (archived > 0)
            {
                ShowToolMessage("删除会话", "网页版那边也归档了 " + archived + " 条对话。");
            }
            else
            {
                ShowToolMessage("删除会话", "网页版那条对话没能自动归档（没开过网页版或网站改版了），需要的话去网页版里手动删。");
            }
        }

        void DeleteAiSession(ChatSession session)
        {
            if (session == null || aiSessions == null) return;
            if (aiStreaming && session == aiCurrent) StopAiStream();

            // 这条会话在网页版那边对应的对话也一并归档（拿 URL 精确匹配，不靠标题猜）
            List<KeyValuePair<string, string>> threads = WebThreadStore.Take(session.Id);
            if (threads.Count > 0) ArchiveWebThreads(threads);

            aiSessions.Remove(session);

            if (aiSessions.Count == 0)
            {
                aiCurrent = new ChatSession();
                aiCurrent.Id = NewSessionId();
                aiCurrent.Title = "新对话";
                aiCurrent.Provider = aiProvider;
                aiSessions.Add(aiCurrent);
            }
            else if (session == aiCurrent)
            {
                aiCurrent = aiSessions[aiSessions.Count - 1];
            }

            if (aiCurrent != null)
            {
                UpdateAiOrbSelection();   // 球高亮跟着当前模型走，不再单独存 provider
            }
            ChatStore.SaveAll(aiSessions);   // 删除要整体重写，否则文件里还留着
            RenderAiSessions();
            RenderChat();
        }

        // ================================================================ 渲染

        void RenderChat()
        {
            if (aiMessageList == null) return;
            // 重建会把滚动位置复位到顶部，这里先记下来再还原：
            // 回答结束时也会走这里，不还原的话就会"一跳到底"（用户实测踩到）
            double offset = 0;
            try { if (aiChatScroll != null) offset = aiChatScroll.VerticalOffset; }
            catch (Exception) { }

            aiMessageList.Children.Clear();
            int count = aiCurrent == null ? 0 : aiCurrent.Messages.Count;
            for (int i = 0; i < count; i++)
            {
                aiMessageList.Children.Add(BuildBubble(aiCurrent.Messages[i]));
            }
            if (aiEmptyState != null)
            {
                aiEmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { if (aiChatScroll != null) aiChatScroll.ScrollToVerticalOffset(offset); }
                catch (Exception) { }
                UpdateAiJumpLatest();
            }), DispatcherPriority.Background);
        }

        Border BuildBubble(ChatMessage message)
        {
            bool user = message.Role == ChatRole.User;
            var body = new StackPanel();
            message.Body = body;
            RenderMessageContent(message);

            var outer = new StackPanel();
            // 用户消息带的图片：气泡里先摆图，再摆文字
            FrameworkElement images = BuildMessageImages(message);
            if (images != null) outer.Children.Add(images);
            outer.Children.Add(body);
            if (!user && !message.Streaming) outer.Children.Add(BuildMessageFooter(message));

            var bubble = new Border
            {
                MaxWidth = AiBubbleMaxWidth,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = user
                    ? (Brush)new LinearGradientBrush(Color.FromRgb(124, 108, 242), Color.FromRgb(94, 82, 196), new Point(0, 0), new Point(1, 1))
                    : new SolidColorBrush(AiBubbleColor),
                BorderBrush = new SolidColorBrush(user
                    ? Color.FromArgb(64, 255, 255, 255)
                    : Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = outer
            };
            message.Bubble = bubble;
            return bubble;
        }

        // 气泡里的图片（用户发的图）。文件还在就画缩略图，不在了就给个占位。
        FrameworkElement BuildMessageImages(ChatMessage message)
        {
            if (message == null || message.Images.Count == 0) return null;
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (string path in message.Images)
            {
                string file = path;
                var frame = new Border
                {
                    Width = 96,
                    Height = 96,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 6, 6),
                    ClipToBounds = true,
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    ToolTip = file
                };
                if (System.IO.File.Exists(file))
                {
                    try
                    {
                        frame.Child = new Image { Source = LoadAiThumb(file, 192), Stretch = Stretch.UniformToFill };
                        frame.Cursor = Cursors.Hand;
                        frame.MouseLeftButtonUp += delegate { ShowFullImage(file); };
                    }
                    catch (Exception) { }
                }
                if (frame.Child == null)
                {
                    frame.Child = new TextBlock
                    {
                        Text = "图片已删除",
                        FontFamily = AiFont,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                wrap.Children.Add(frame);
            }
            return wrap;
        }

        FrameworkElement BuildMessageFooter(ChatMessage message)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            if (message.Failed)
            {
                var retry = ToolButton("重试", 8);
                retry.Height = 22;
                retry.FontSize = 11;
                retry.Margin = new Thickness(0, 0, 6, 0);
                retry.Click += delegate { RetryAiMessage(message); };
                row.Children.Add(retry);
            }
            if (!string.IsNullOrEmpty(message.Text))
            {
                var copy = ToolButton("复制", 8);
                copy.Height = 22;
                copy.FontSize = 11;
                copy.Click += delegate { SafeCopy(message.Text); };
                row.Children.Add(copy);
            }
            return row;
        }

        void RenderMessageContent(ChatMessage message)
        {
            if (message == null || message.Body == null) return;
            bool user = message.Role == ChatRole.User;
            message.Body.Children.Clear();

            string text = message.Text == null ? "" : message.Text;

            // 还没出第一个字：给个"思考中…"，否则空气泡看着像卡死了
            if (text.Length == 0)
            {
                if (message.Streaming && !user) message.Body.Children.Add(BuildThinkingIndicator());
                return;
            }
            string[] parts = text.Split(new string[] { "```" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                if (i % 2 == 1)
                {
                    message.Body.Children.Add(BuildCodeBlock(part));
                }
                else
                {
                    message.Body.Children.Add(new TextBlock
                    {
                        Text = part,
                        FontFamily = AiFont,
                        FontSize = 13,
                        LineHeight = 21,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = user ? Brushes.White : new SolidColorBrush(AiTextColor)
                    });
                }
            }
        }

        // 等待回答时的占位：一个呼吸的小圆点 + 思考中…
        FrameworkElement BuildThinkingIndicator()
        {
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = new SolidColorBrush(AiAccentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            try
            {
                var pulse = new System.Windows.Media.Animation.DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(650));
                pulse.AutoReverse = true;
                pulse.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
                dot.BeginAnimation(OpacityProperty, pulse);
            }
            catch (Exception) { }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock
            {
                Text = "思考中…",
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 208, 202, 232)),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        FrameworkElement BuildCodeBlock(string raw)
        {
            string code = raw;
            if (code.StartsWith("\r\n")) code = code.Substring(2);
            else if (code.StartsWith("\n")) code = code.Substring(1);
            if (code.EndsWith("\r\n")) code = code.Substring(0, code.Length - 2);
            else if (code.EndsWith("\n")) code = code.Substring(0, code.Length - 1);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(110, 12, 15, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 4),
                Child = new TextBlock
                {
                    Text = code,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(214, 218, 230)),
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        void ScrollAiToEnd()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { if (aiChatScroll != null) aiChatScroll.ScrollToEnd(); }
                catch (Exception) { }
                UpdateAiJumpLatest();
            }), DispatcherPriority.Background);
        }

        // 把某条消息的顶部对齐到可视区顶部（发完问题停在这儿，方便从头读回答）
        void ScrollAiToMessageTop(ChatMessage message)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    if (aiChatScroll == null || aiMessageList == null) return;
                    if (message == null || message.Bubble == null) return;
                    Point origin = message.Bubble.TranslatePoint(new Point(0, 0), aiMessageList);
                    aiChatScroll.ScrollToVerticalOffset(Math.Max(0, origin.Y - 8));
                }
                catch (Exception) { }
                UpdateAiJumpLatest();
            }), DispatcherPriority.Background);
        }

        // ================================================================ 发送 / 流式

        void SubmitAiInput()
        {
            if (aiStreaming || !aiVisible || aiInput == null) return;

            // 图片只有网页版能发（网页自己上传）；API 模型还没有视觉能力
            if (aiAttachments.Count > 0 && !CurrentModelIsWeb())
            {
                ShowToolMessage("发送到 AI", "当前模型（API）不支持图片。点 D / 豆 小球切到网页版再发。");
                return;
            }

            string text = aiInput.Text == null ? "" : aiInput.Text.Trim();
            if (text.Length == 0 && aiAttachments.Count == 0) return;
            if (text.Length == 0) text = "请看看这张图片。";   // 只拖图不写字时给个默认提问
            aiInput.Text = "";

            if (aiCurrent == null) NewAiChat();
            if (aiCurrent == null) return;

            var userMessage = new ChatMessage { Role = ChatRole.User, Text = text };
            // 图片挂到这条用户消息上（气泡里能看见），落盘时也会带上
            if (aiAttachments.Count > 0)
            {
                for (int i = 0; i < aiAttachments.Count; i++) userMessage.Images.Add(aiAttachments[i]);
            }
            aiCurrent.Messages.Add(userMessage);

            if (aiCurrent.Title == "新对话")
            {
                aiCurrent.Title = text.Length > 12 ? text.Substring(0, 12) + "…" : text;
                RenderAiSessions();
            }

            var aiMessage = new ChatMessage { Role = ChatRole.Ai, Text = "", Streaming = true };
            aiCurrent.Messages.Add(aiMessage);
            aiLastUserText = text;

            if (aiMessageList != null)
            {
                aiMessageList.Children.Add(BuildBubble(userMessage));
                aiMessageList.Children.Add(BuildBubble(aiMessage));
            }
            if (aiEmptyHint != null) aiEmptyHint.Visibility = Visibility.Collapsed;
            // 停在"这条提问"的顶部：回答往下长，你从头读，不会被拽到末尾
            ScrollAiToMessageTop(userMessage);

            aiStreaming = true;
            UpdateAiSendButton();
            aiCancellation = new CancellationTokenSource();
            RunAiStream(text, aiMessage, aiCancellation.Token);
        }

        async void RunAiStream(string prompt, ChatMessage message, CancellationToken token)
        {
            try
            {
                // 每次发送前按当前模型解析后端：切换 chip 之后下一条就换服务商了。
                // 配置不全（没 key / 没地址）时 Resolve 抛"人话"异常，下面统一变成错误气泡。
                IChatBackend backend = ChatBackends.Resolve(CurrentAiModel(), ReasoningEffort());
                WebBackend webBackend = backend as WebBackend;
                if (webBackend != null && aiCurrent != null)
                {
                    // 胶囊会话 ↔ 网页会话 绑定：有记录就回那条，没有就开一条新的
                    string siteId = WebSiteOf(CurrentAiModel());
                    string sessionId = aiCurrent.Id;
                    string thread = WebThreadStore.Get(sessionId, siteId);
                    webBackend.SetThread(thread, thread == null, delegate(string url)
                    {
                        WebThreadStore.Set(sessionId, siteId, url);
                    });
                }
                if (webBackend != null && aiAttachments.Count > 0)
                {
                    // 图片只在网页版能发（网页自己会上传），交给它之后清空图片条
                    webBackend.SetAttachments(aiAttachments.ToArray());
                    aiAttachments.Clear();
                    RenderAiAttachments();
                }
                await backend.SendAsync(prompt, delegate(string delta) { AppendDelta(message, delta); }, token);
                message.Streaming = false;
                FinishAiMessage(message, null);
            }
            catch (OperationCanceledException)
            {
                message.Streaming = false;
                FinishAiMessage(message, null);
            }
            catch (Exception exception)
            {
                message.Streaming = false;
                message.Failed = true;
                FinishAiMessage(message, exception.Message);
            }
            finally
            {
                aiStreaming = false;
                UpdateAiSendButton();
            }
        }

        // 用户往上翻着看的时候，别再把他拽回底部
        bool IsAiChatNearBottom()
        {
            try
            {
                if (aiChatScroll == null) return true;
                return aiChatScroll.VerticalOffset >= aiChatScroll.ScrollableHeight - 48;
            }
            catch (Exception) { return true; }
        }

        // 翻上去看长回答时，右下角出现一个"回到最新"
        void UpdateAiJumpLatest()
        {
            if (aiJumpLatest == null) return;
            bool away = false;
            try
            {
                away = aiChatScroll != null
                    && aiChatScroll.ScrollableHeight > 140
                    && !IsAiChatNearBottom();
            }
            catch (Exception) { }
            aiJumpLatest.Visibility = away ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement BuildAiJumpLatest()
        {
            var pill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(220, 42, 46, 56)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "↓ 最新",
                    FontFamily = AiFont,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(214, 208, 240))
                }
            };
            pill.MouseLeftButtonUp += delegate { ScrollAiToEnd(); };
            aiJumpLatest = pill;
            return pill;
        }

        void AppendDelta(ChatMessage message, string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            // 不再自动跟随：回答增长时视图停在原位，让你从头读；要到底部点「↓ 最新」或自己滚
            message.Text += delta;
            RenderMessageContent(message);
            UpdateAiJumpLatest();
        }

        void FinishAiMessage(ChatMessage message, string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                message.Text = (message.Text.Length == 0 ? "" : message.Text + "\n\n") + "[出错了] " + error;
                message.Failed = true;
            }
            if (aiCurrent == null || !aiCurrent.Messages.Contains(message)) return;

            // 一个字都没出来就被停掉（比如刚发就点停止）：别留一个空气泡
            if (string.IsNullOrEmpty(error) && message.Text.Length == 0)
            {
                aiCurrent.Messages.Remove(message);
            }
            else
            {
                PersistPendingMessages();
            }
            RenderChat();
        }

        // 把还没落盘的消息按顺序追加到 chats.txt（用户消息和回答一起写）
        void PersistPendingMessages()
        {
            if (aiCurrent == null) return;
            for (int i = 0; i < aiCurrent.Messages.Count; i++)
            {
                ChatMessage item = aiCurrent.Messages[i];
                if (item == null || item.Persisted || item.Streaming) continue;
                if (string.IsNullOrEmpty(item.Text)) continue;
                ChatStore.Append(aiCurrent, item);
                item.Persisted = true;
            }
        }

        void StopAiStream()
        {
            if (aiCancellation == null) return;
            try { aiCancellation.Cancel(); }
            catch (Exception) { }
        }

        void RetryAiMessage(ChatMessage message)
        {
            if (aiStreaming || message == null || string.IsNullOrEmpty(aiLastUserText)) return;
            message.Text = "";
            message.Failed = false;
            message.Streaming = true;
            if (aiCurrent != null && !aiCurrent.Messages.Contains(message)) aiCurrent.Messages.Add(message);
            RenderChat();
            aiStreaming = true;
            UpdateAiSendButton();
            aiCancellation = new CancellationTokenSource();
            RunAiStream(aiLastUserText, message, aiCancellation.Token);
        }

        void UpdateAiSendButton()
        {
            if (aiSendButton == null) return;
            aiSendButton.Content = aiStreaming ? "停止" : "发送";
            aiSendButton.Background = aiStreaming
                ? new LinearGradientBrush(Color.FromRgb(214, 74, 82), Color.FromRgb(168, 48, 60), new Point(0, 0), new Point(0, 1))
                : new LinearGradientBrush(Color.FromRgb(124, 108, 242), Color.FromRgb(94, 82, 196), new Point(0, 0), new Point(0, 1));
        }

        static void SafeCopy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetText(text); return; }
                catch (Exception) { Thread.Sleep(40); }
            }
        }

        // ================================================================ 网页版球 / 模型 chip

        // 点球 = 直接把"当前模型"切到对应的网页版模型（chip 立刻跟着变）。
        // 之前只改了个球的高亮、没动模型，导致"点了 DS 球还是发给智谱"——用户实测踩到过。
        void SetAiProvider(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            AiModelConfig target = FindWebModel(id);
            if (target == null)
            {
                bool supported = string.Equals(id, "deepseek", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(id, "doubao", StringComparison.OrdinalIgnoreCase);
                if (!supported)
                {
                    ShowToolMessage("网页版驱动", "这个站点还没接，先用 DeepSeek 网页版或 API 模型。");
                    return;
                }
                target = EnsureWebModel(id);
            }

            if (target != null) ApplyAiModel(target);
            if (aiChipOpen) CloseAiChip();
        }

        AiModelConfig FindWebModel(string siteId)
        {
            for (int i = 0; i < aiModels.Count; i++)
            {
                AiModelConfig m = aiModels[i];
                if (m == null || m.BaseUrl == null) continue;
                if (!m.BaseUrl.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(m.BaseUrl.Substring(6).Trim('/'), siteId, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        // 第一次点某个球时，把对应的网页版模型写进 models.txt（之后就是普通条目）
        AiModelConfig EnsureWebModel(string siteId)
        {
            var config = new AiModelConfig();
            config.Id = siteId + "-web";
            config.Label = (siteId == "doubao" ? "豆包网页版（实验）" : "DeepSeek 网页版（实验）");
            config.BaseUrl = "web://" + siteId;
            config.Model = siteId;
            config.KeyRef = "";
            config.Vision = false;
            AiConfigStore.SaveModel(config);
            aiModels = AiConfigStore.LoadModels();
            return FindWebModel(siteId);
        }

        // 球高亮不再单独存状态：直接看当前模型是不是某个网页版
        void UpdateAiOrbSelection()
        {
            if (aiOrbs == null) return;
            string siteId = null;
            AiModelConfig current = CurrentAiModel();
            if (current != null && current.BaseUrl != null
                && current.BaseUrl.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
            {
                siteId = current.BaseUrl.Substring(6).Trim('/');
            }

            for (int i = 0; i < aiOrbs.Length; i++)
            {
                Border orb = aiOrbs[i];
                if (orb == null) continue;
                bool active = string.Equals(aiOrbIds[i], siteId, StringComparison.OrdinalIgnoreCase);
                orb.BorderThickness = new Thickness(active ? 1.6 : 1);
                orb.BorderBrush = new SolidColorBrush(active
                    ? AiAccentColor
                    : Color.FromArgb(60, 255, 255, 255));
                orb.Effect = active
                    ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85, Color = AiAccentColor }
                    : null;
            }
        }

        void UpdateAiChipTexts()
        {
            AiModelConfig current = CurrentAiModel();
            bool missingKey = current != null
                && !string.IsNullOrEmpty(current.KeyRef)
                && !AiConfigStore.HasKey(current.KeyRef);

            if (aiModelChipText != null) aiModelChipText.Text = aiModelLabel;
            if (aiModelValueText != null)
            {
                aiModelValueText.Text = missingKey ? aiModelLabel + "（缺 Key）" : aiModelLabel;
                aiModelValueText.Foreground = new SolidColorBrush(missingKey ? Color.FromRgb(240, 180, 120) : AiTextColor);
            }
            if (aiReasoningValueText != null) aiReasoningValueText.Text = ReasoningName(aiReasoningIndex);
        }

        void ToggleAiChip()
        {
            if (aiChipOpen)
            {
                CloseAiChip();
                return;
            }
            aiChipOpen = true;
            UpdateAiChipTexts();
            if (aiChipPopup == null) return;
            aiChipPopup.Visibility = Visibility.Visible;
            Motion.PopIn(aiChipPopup, new Point(0.5, 1), 0.96, 8, 160, 0, Motion.Enter);
        }

        void CloseAiChip()
        {
            aiChipOpen = false;
            if (aiModelOptions != null) aiModelOptions.Visibility = Visibility.Collapsed;
            if (aiChipPopup != null) aiChipPopup.Visibility = Visibility.Collapsed;
        }

        void ToggleAiModelOptions()
        {
            if (aiModelOptions == null) return;
            aiModelOptions.Visibility = aiModelOptions.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // model == null → 新增；否则 → 编辑这一条（关键用途：给已有模型填 Key）
        void OpenAiModelSheet(AiModelConfig model)
        {
            CloseAiChip();
            if (aiAddSheet == null) return;

            aiEditingModelId = model == null ? null : model.Id;
            if (aiDeleteModelButton != null)
            {
                aiDeleteModelButton.Visibility = model == null ? Visibility.Collapsed : Visibility.Visible;
            }
            if (aiAddSheetTitle != null)
            {
                aiAddSheetTitle.Text = model == null ? "加入模型" : "编辑模型";
            }
            if (aiAddNameBox != null) aiAddNameBox.Text = model == null ? "" : model.Label;
            if (aiAddEndpointBox != null) aiAddEndpointBox.Text = model == null ? "" : model.BaseUrl;
            if (aiAddModelBox != null) aiAddModelBox.Text = model == null ? "" : model.Model;
            if (aiAddKeyBox != null) aiAddKeyBox.Text = "";
            if (aiAddKeyHint != null)
            {
                if (model == null) aiAddKeyHint.Text = "API Key（可以留空，之后手改 keys.txt）";
                else if (AiConfigStore.HasKey(model.KeyRef)) aiAddKeyHint.Text = "API Key（已保存，留空表示不改）";
                else aiAddKeyHint.Text = "API Key（还没填，这次填上就能用了）";
            }

            aiAddSheet.Visibility = Visibility.Visible;
            if (aiAddNameBox != null)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { Keyboard.Focus(aiAddNameBox); }
                    catch (Exception) { }
                }), DispatcherPriority.Input);
            }
        }

        void CloseAiAddSheet()
        {
            if (aiAddSheet != null) aiAddSheet.Visibility = Visibility.Collapsed;
        }

        void DeleteEditingModel()
        {
            if (string.IsNullOrEmpty(aiEditingModelId)) return;
            string id = aiEditingModelId;
            aiEditingModelId = null;
            CloseAiAddSheet();

            if (aiStreaming) StopAiStream();
            AiConfigStore.DeleteModel(id);
            aiModels = AiConfigStore.LoadModels();
            aiModelId = "";
            ApplyAiModel(aiModels.Count > 0 ? aiModels[0] : null);
            ShowToolMessage("删除模型", "已从 models.txt 里删掉这一条（keys.txt 里的 key 保留）。");
        }

        void ConfirmAiAddModel()
        {
            string label = aiAddNameBox == null ? "" : aiAddNameBox.Text.Trim();
            string baseUrl = aiAddEndpointBox == null ? "" : aiAddEndpointBox.Text.Trim();
            string modelId = aiAddModelBox == null ? "" : aiAddModelBox.Text.Trim();
            string key = aiAddKeyBox == null ? "" : aiAddKeyBox.Text.Trim();

            if (label.Length == 0 || baseUrl.Length == 0 || modelId.Length == 0)
            {
                ShowToolMessage("加入模型", "显示名称 / 接口地址 / 模型 ID 三项都要填。");
                return;
            }

            bool editing = !string.IsNullOrEmpty(aiEditingModelId);
            var config = new AiModelConfig();
            if (editing)
            {
                AiModelConfig existing = null;
                for (int i = 0; i < aiModels.Count; i++)
                {
                    if (aiModels[i].Id == aiEditingModelId) { existing = aiModels[i]; break; }
                }
                config.Id = aiEditingModelId;
                config.KeyRef = existing != null && !string.IsNullOrEmpty(existing.KeyRef)
                    ? existing.KeyRef
                    : aiEditingModelId;
                config.Vision = existing != null && existing.Vision;
            }
            else
            {
                config.Id = "m" + Guid.NewGuid().ToString("N").Substring(0, 8);
                config.KeyRef = config.Id;
            }

            config.Label = label;
            config.BaseUrl = baseUrl.TrimEnd('/');
            config.Model = modelId;
            AiConfigStore.SaveModel(config);

            string hint = editing ? "已更新 data\\models.txt。" : "已写入 data\\models.txt。";
            if (key.Length > 0)
            {
                if (AiConfigStore.SaveKey(config.KeyRef, key)) hint += " Key 已加密保存，可以发消息了。";
                else hint += " Key 加密失败，请手改 keys.txt：" + config.KeyRef + "|plain:你的key";
            }
            else if (!AiConfigStore.HasKey(config.KeyRef))
            {
                hint += " 还没填 Key，所以还不能对话。";
            }

            if (aiAddNameBox != null) aiAddNameBox.Text = "";
            if (aiAddEndpointBox != null) aiAddEndpointBox.Text = "";
            if (aiAddModelBox != null) aiAddModelBox.Text = "";
            if (aiAddKeyBox != null) aiAddKeyBox.Text = "";
            aiEditingModelId = null;
            CloseAiAddSheet();

            aiModels = AiConfigStore.LoadModels();
            ApplyAiModel(config);
            ShowToolMessage(editing ? "编辑模型" : "加入模型", hint);
        }
    }
}
