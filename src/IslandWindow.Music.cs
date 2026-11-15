using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Interop;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 音乐面板（第一步：只做壳）
    //
    // 现在能用的：
    //   · 点绿球 → 面板长出来；顶栏 ← 回图片面板、× 收起回胶囊；
    //   · 没登录时盖着一张平台选择卡（网易云 / QQ 音乐 / 酷狗 / 汽水），
    //     点一个就弹出独立的登录窗（WebView2 打开官方网页，用户自己扫码）；
    //     登录成功或点了「我已登录」后自动回来，面板切成"已登录"的样子；
    //   · 已登录的样子是完整的播放器骨架：账号卡 / 歌单栏 / 封面 / 进度 / 控制键。
    //
    // 还没做的（下一步，别把它当 bug）：
    //   · 读歌单：歌单行现在是灰条占位，不是假数据；
    //   · 播放：控制键故意做成不可点（40% 透明），点不出声也不会假装在播。
    //
    // 为什么登录要另开窗口：胶囊窗口是分层透明窗口（AllowsTransparency），
    // WebView2 是子 HWND，塞进去一点画面都不会有——抖音窗、AI 网页版都是这么绕开的。
    // 详细说明见 docs\音乐面板-交接包.md。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        const double MusicPanelWidth = 900;
        const double MusicPanelHeight = 420;
        const double MusicPanelTop = 8;
        const double MusicSidebarWidth = 168;

        static readonly Color MusicTextColor = Color.FromRgb(232, 236, 243);
        static readonly Color MusicMutedColor = Color.FromRgb(148, 156, 172);
        static readonly Color MusicAccentFrom = Color.FromRgb(52, 211, 153);
        static readonly Color MusicAccentTo = Color.FromRgb(14, 165, 233);

        Border musicPanel;
        Border musicLoginOverlay;
        Border musicAccountChip;
        Ellipse musicChipDot;
        TextBlock musicChipText;
        Border musicAccountAvatar;
        TextBlock musicAccountBadge;
        TextBlock musicAccountName;
        TextBlock musicAccountSub;
        StackPanel musicPlaylistList;
        StackPanel musicSongList;
        TextBlock musicNowTitle;
        TextBlock musicNowArtist;
        Border musicCoverHost;
        Border musicProgressRail;
        Border musicProgressFill;
        Ellipse musicProgressKnob;
        TextBlock musicTimeLeft;
        TextBlock musicTimeRight;
        Border musicPlayButton;
        TextBlock musicStateHint;
        TextBlock musicStateTag;
        TextBlock musicLoginTitle;
        Button musicLoginCancel;
        Border musicLoginCard;
        StackPanel musicPlatformStage;
        StackPanel musicQrStage;
        TextBlock musicQrTitle;
        Image musicQrImage;
        Border musicQrCard;
        TextBlock musicQrStatus;
        Button musicQrRefresh;
        Button musicQrFallback;

        // 汽水扫码流程
        DispatcherTimer qishuiQrTimer;
        string qishuiQrToken = "";
        MusicPlatform qishuiQrPlatform;
        MusicQrLogin musicQrSession;
        MusicPlatform musicQrPlatform;
        Border musicSettingsSheet;
        TextBlock musicSettingsAccount;
        TextBlock musicListStatus;
        bool musicImporting;
        bool musicRowsIntroduced;
        FrameworkElement musicPlayerStage;
        Border musicPosterHost;
        Image musicPosterBlur;
        StackPanel musicLyricsPanel;
        ScrollViewer musicLyricsScroll;
        List<MusicLyricLine> musicLyricLines = new List<MusicLyricLine>();
        TimeSpan? musicPendingSeek;
        string musicPendingSeekMid = "";
        Border musicPosterProgressFill;
        Ellipse musicPosterProgressKnob;
        TextBlock musicPosterTimeLeft;
        TextBlock musicPosterTimeRight;
        Border musicPosterPlayButton;
        MediaPlayer musicPlayer;
        DispatcherTimer musicTimer;
        bool musicPlaying;
        bool musicResolving;
        string musicCurrentUrl = "";
        Grid capsuleMiniPlayer;
        Border capsuleMiniDisc;
        RotateTransform capsuleMiniDiscSpin;
        Border capsuleMiniPlayButton;
        DispatcherTimer capsuleSpectrumTimer;
        readonly List<Border> capsuleSpectrumBars = new List<Border>();
        readonly Random musicRandom = new Random();
        // 随机播放的一个「轮次」：本轮已经放过的歌不再抽，放完一轮再清空重来
        readonly HashSet<string> musicShufflePlayed = new HashSet<string>(StringComparer.Ordinal);
        bool capsuleMiniActive;
        const double CapsuleIdleWidth = 148;
        const double CapsuleMusicWidth = 238;
        Grid musicPosterStage;
        Grid musicQueueSheet;
        Border musicQueueCoverHost;
        StackPanel musicQueueList;
        TextBlock musicQueueTitle;
        MusicSongInfo musicCurrentSong;
        readonly Dictionary<string, Border> musicPlaylistBgs = new Dictionary<string, Border>();
        readonly Dictionary<string, TextBlock> musicPlaylistCounts = new Dictionary<string, TextBlock>();
        List<MusicPlaylistInfo> musicPlaylists = new List<MusicPlaylistInfo>();
        List<MusicSongInfo> musicSongs = new List<MusicSongInfo>();
        string musicSelectedPlaylistId = "";

        bool musicVisible;
        bool musicLastSurface;
        string musicPlatformId = "";
        MusicLoginWindow musicLoginWindow;

        // ================================================================ 构建

        Border BuildMusicPanel()
        {
            musicPlatformId = MusicSession.Load();

            var shell = new Grid();
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---------------------------------------------------------- 顶栏
            var header = new Grid { Margin = new Thickness(18, 0, 12, 0) };
            var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerLeft.Children.Add(new TextBlock
            {
                Text = "音乐",
                FontFamily = AiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerLeft.Children.Add(new Border
            {
                Width = 1,
                Height = 16,
                Margin = new Thickness(12, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerLeft.Children.Add(BuildMusicAccountChip());

            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var back = AiGhostButton("←");
            back.FontSize = 15;
            back.Margin = new Thickness(0, 0, 4, 0);
            back.ToolTip = "返回图片面板";
            back.Click += delegate { BackToImagePanelFromMusic(); };
            headerRight.Children.Add(back);
            var close = AiGhostButton("×");
            close.FontSize = 15;
            close.ToolTip = "收起";
            close.Click += delegate { CloseMusicPanel(); };
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
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MusicSidebarWidth) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = BuildMusicSidebar();
            Grid.SetColumn(side, 0);
            body.Children.Add(side);

            var main = BuildMusicMain();
            Grid.SetColumn(main, 1);
            body.Children.Add(main);

            FrameworkElement queue = BuildMusicQueueSheet();
            Grid.SetColumn(queue, 0);
            Grid.SetColumnSpan(queue, 2);
            body.Children.Add(queue);


            // 登录选择卡：盖住整个主体（连左栏一起），所以它是 body 的最后一个孩子
            musicLoginOverlay = BuildMusicLoginOverlay();
            musicLoginOverlay.Visibility = Visibility.Collapsed;
            Grid.SetColumn(musicLoginOverlay, 0);
            Grid.SetColumnSpan(musicLoginOverlay, 2);
            body.Children.Add(musicLoginOverlay);

            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            shell.Children.Add(layout);

            musicSettingsSheet = BuildMusicSettingsSheet();
            shell.Children.Add(musicSettingsSheet);

            var panelBorder = new Border
            {
                Width = MusicPanelWidth,
                Height = MusicPanelHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, MusicPanelTop, 0, 0),
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
            panelBorder.Resources[typeof(ScrollBar)] = BuildAiScrollBarStyle();

            ApplyMusicLoginVisual();
            return panelBorder;
        }

        Border BuildMusicAccountChip()
        {
            musicChipDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(120, 128, 145)),
                VerticalAlignment = VerticalAlignment.Center
            };
            musicChipText = new TextBlock
            {
                Text = "未登录",
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(MusicMutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(musicChipDot);
            row.Children.Add(musicChipText);

            musicAccountChip = new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "登录 / 换一个平台",
                Child = row
            };
            musicAccountChip.MouseLeftButtonUp += delegate { ShowMusicLoginPicker(); };
            musicAccountChip.MouseEnter += delegate { musicAccountChip.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); };
            musicAccountChip.MouseLeave += delegate { musicAccountChip.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)); };
            return musicAccountChip;
        }

        Border BuildMusicSidebar()
        {
            var side = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(10, 10, 10, 10)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 账号卡
            musicAccountBadge = new TextBlock
            {
                Text = "?",
                FontFamily = AiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            musicAccountAvatar = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromRgb(48, 55, 66)),
                Child = musicAccountBadge,
                VerticalAlignment = VerticalAlignment.Center
            };
            musicAccountName = new TextBlock
            {
                Text = "未登录",
                FontFamily = AiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MusicTextColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            musicAccountSub = new TextBlock
            {
                Text = "点这里登录",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(MusicMutedColor),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            nameStack.Children.Add(musicAccountName);
            nameStack.Children.Add(musicAccountSub);

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(musicAccountAvatar, 0);
            cardGrid.Children.Add(musicAccountAvatar);
            Grid.SetColumn(nameStack, 1);
            cardGrid.Children.Add(nameStack);

            var accountCard = new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "登录 / 换一个平台",
                Child = cardGrid
            };
            accountCard.MouseLeftButtonUp += delegate { ShowMusicLoginPicker(); };
            Grid.SetRow(accountCard, 0);
            grid.Children.Add(accountCard);

            // 歌单栏
            var listGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var listHead = new Grid { Height = 20, Margin = new Thickness(2, 0, 2, 4) };
            var listTitle = new TextBlock
            {
                Text = "我的歌单",
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(MusicMutedColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            listHead.Children.Add(listTitle);
            var count = new TextBlock
            {
                Text = "待接入",
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 156, 172)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "歌单接口是下一步"
            };
            musicListStatus = count;

            var rightSide = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            rightSide.Children.Add(count);
            listHead.Children.Add(rightSide);
            Grid.SetRow(listHead, 0);
            listGrid.Children.Add(listHead);

            musicPlaylistList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = musicPlaylistList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent
            };
            Grid.SetRow(scroll, 1);
            listGrid.Children.Add(scroll);

            Grid.SetRow(listGrid, 1);
            grid.Children.Add(listGrid);

            // 底部：设置入口（只建一次！之前建了两次，第二份没设行号就叠到顶部去了）
            FrameworkElement footer = BuildMusicFooter();
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            side.Child = grid;
            return side;
        }

        Grid BuildMusicMain()
        {
            var main = new Grid { Margin = new Thickness(18, 14, 16, 12) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // 播放器区（选歌后出现）
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // 歌曲列表（铺满）

            // ---- 封面 + 现在播放
            var coverRow = new Grid();
            coverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(BuildMusicCoverOnce(coverRow), 0);

            var info = new StackPanel { Margin = new Thickness(18, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            musicNowTitle = new TextBlock
            {
                Text = "还没有在播放",
                FontFamily = AiFont,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            musicNowArtist = new TextBlock
            {
                Text = "登录后从左边挑一个歌单",
                FontFamily = AiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MusicMutedColor),
                Margin = new Thickness(0, 8, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            musicStateTag = new TextBlock
            {
                Text = "",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(126, 226, 194)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tagBorder = new Border
            {
                Height = 21,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(22, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(56, 52, 211, 153)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 14, 0, 0),
                Child = musicStateTag
            };
            musicStateHint = new TextBlock
            {
                FontFamily = AiFont,
                FontSize = 10.5,
                LineHeight = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 148, 156, 172)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                Margin = new Thickness(0, 12, 0, 0)
            };
            info.Children.Add(musicNowTitle);
            info.Children.Add(musicNowArtist);
            info.Children.Add(tagBorder);
            info.Children.Add(musicStateHint);
            Grid.SetColumn(info, 1);
            coverRow.Children.Add(info);

            var stage = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 封面 + 现在播放
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 进度条
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 控制键
            musicPlayerStage = stage;

            Grid.SetRow(coverRow, 0);
            stage.Children.Add(coverRow);

            // ---- 进度条
            Grid progressRow = BuildMusicProgress();
            Grid.SetRow(progressRow, 1);
            stage.Children.Add(progressRow);

            // ---- 控制键
            Grid controlRow = BuildMusicControls();
            Grid.SetRow(controlRow, 2);
            stage.Children.Add(controlRow);

            // 播放器区默认收起：选歌单后整个右区让给歌曲列表
            Grid.SetRow(stage, 0);
            main.Children.Add(stage);

            // ---- 当前歌单
            var songArea = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            songArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            songArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var songHead = new Grid { Height = 22, Margin = new Thickness(0, 0, 0, 2) };
            songHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            songHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            songHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var songHeadTitle = new TextBlock
            {
                Text = "当前歌单",
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(MusicMutedColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(songHeadTitle, 1);
            songHead.Children.Add(songHeadTitle);

            var songHeadTime = new TextBlock
            {
                Text = "时长",
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 156, 172)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(songHeadTime, 2);
            songHead.Children.Add(songHeadTime);
            Grid.SetRow(songHead, 0);
            songArea.Children.Add(songHead);

            musicSongList = new StackPanel();
            var songScroll = new ScrollViewer
            {
                Content = musicSongList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent
            };
            Grid.SetRow(songScroll, 1);
            songArea.Children.Add(songScroll);

            Grid.SetRow(songArea, 1);
            main.Children.Add(songArea);

            // 底部迷你条：浏览歌曲时也能看到当前在放什么、随时暂停/切歌
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement browseBar = BuildBrowseMiniBar();
            Grid.SetRow(browseBar, 2);
            main.Children.Add(browseBar);

            // 海报模式：选歌后铺满整个右区（盖住歌曲列表），不显示歌名文字
            musicPosterStage = BuildMusicPosterStage();
            Grid.SetRow(musicPosterStage, 0);
            Grid.SetRowSpan(musicPosterStage, 2);
            main.Children.Add(musicPosterStage);

            RebuildMusicPlaceholders();
            return main;
        }

        // 满屏海报 + 右下角队列按钮
        Grid BuildMusicPosterStage()
        {
            var grid = new Grid { Visibility = Visibility.Collapsed };
            var fallbackBrush = new LinearGradientBrush(
                Color.FromRgb(44, 52, 64), Color.FromRgb(18, 22, 30),
                new Point(0, 0), new Point(1, 1));
            grid.Children.Add(new Border { Background = fallbackBrush });
            // 没有封面时不是一块黑：中间放个唱片占位
            var placeholder = MusicGlyph("note", Color.FromArgb(46, 255, 255, 255), 96);
            grid.Children.Add(placeholder);

            // 背景层：同一张封面铺满 + 重度模糊，负责填满空间又不裁切前景
            musicPosterBlur = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.85,
                Effect = new BlurEffect { Radius = 46 }
            };
            grid.Children.Add(musicPosterBlur);

            // 压暗一层，前景封面和按钮才跳得出来
            grid.Children.Add(new Border
            {
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(
                    Color.FromArgb(86, 8, 10, 16), Color.FromArgb(170, 6, 8, 12),
                    new Point(0, 0), new Point(0, 1))
            });

            // 内容：左边封面，右边歌词
            var content = new Grid { Margin = new Thickness(24, 12, 20, 92) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 圆角靠 Border 的 ImageBrush 实现（Border 不会裁剪子元素，之前用 Clip 没生效）
            musicPosterHost = new Border
            {
                Width = 250,
                Height = 250,
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "点封面回到歌曲列表",
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Opacity = 0.55, Color = Colors.Black }
            };
            musicPosterHost.MouseLeftButtonUp += delegate { ExitMusicPoster(); };
            Grid.SetColumn(musicPosterHost, 0);
            content.Children.Add(musicPosterHost);

            // 歌词：一行一个控件，悬停高亮并出现"▶ 时间"按钮，点了从这里播
            musicLyricsPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 4, 4, 4)
            };
            musicLyricsScroll = new ScrollViewer
            {
                Content = musicLyricsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(musicLyricsScroll, 1);
            content.Children.Add(musicLyricsScroll);

            grid.Children.Add(content);

            // 底部：细进度条 + 小控制键（给封面让出空间）
            var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 88, 4) };
            bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            FrameworkElement posterProgress = BuildPosterProgress();
            Grid.SetRow(posterProgress, 0);
            bottom.Children.Add(posterProgress);

            FrameworkElement posterControls = BuildPosterControls();
            Grid.SetRow(posterControls, 1);
            bottom.Children.Add(posterControls);

            grid.Children.Add(bottom);

            // 底部压暗，保证按钮看得清
            grid.Children.Add(new Border
            {
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb(120, 0, 0, 0),
                    new Point(0, 0.6), new Point(0, 1))
            });

            return grid;
        }

        // 海报模式专用的细进度条（比普通播放器那根矮一半）
        FrameworkElement BuildPosterProgress()
        {
            var progress = new Grid { Height = 16, Margin = new Thickness(0, 0, 0, 2) };
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            musicPosterTimeLeft = MusicTimeText("0:00", HorizontalAlignment.Left);
            musicPosterTimeLeft.FontSize = 9.5;
            Grid.SetColumn(musicPosterTimeLeft, 0);
            progress.Children.Add(musicPosterTimeLeft);

            var track = new Grid { Height = 14, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            track.Children.Add(new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            musicPosterProgressFill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 0,
                Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 0))
            };
            track.Children.Add(musicPosterProgressFill);
            musicPosterProgressKnob = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(240, 244, 250)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            track.Children.Add(musicPosterProgressKnob);
            Grid.SetColumn(track, 1);
            progress.Children.Add(track);

            musicPosterTimeRight = MusicTimeText("0:00", HorizontalAlignment.Right);
            musicPosterTimeRight.FontSize = 9.5;
            Grid.SetColumn(musicPosterTimeRight, 2);
            progress.Children.Add(musicPosterTimeRight);
            return progress;
        }

        // 海报模式专用的小号控制键
        FrameworkElement BuildPosterControls()
        {
            var row = new Grid { Height = 58 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var center = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var prev = MusicCircleButton("prev", 30, Color.FromArgb(20, 255, 255, 255));
            var play = MusicCircleButton("play", 48, Color.FromArgb(0, 0, 0, 0));
            play.Margin = new Thickness(14, 0, 14, 0);
            play.Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 1));
            play.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.55, Color = MusicAccentFrom };
            musicPosterPlayButton = play;
            var next = MusicCircleButton("next", 30, Color.FromArgb(20, 255, 255, 255));
            prev.MouseLeftButtonUp += delegate { NextMusicSong(-1); };
            play.MouseLeftButtonUp += delegate { ToggleMusicPlayback(); };
            next.MouseLeftButtonUp += delegate { NextMusicSong(1); };
            center.Children.Add(prev);
            center.Children.Add(play);
            center.Children.Add(next);
            Grid.SetColumn(center, 1);
            row.Children.Add(center);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(BuildMusicVolume());
            FrameworkElement queuePill = BuildQueuePill();
            queuePill.Margin = new Thickness(14, 0, 0, 0);
            right.Children.Add(queuePill);
            FrameworkElement castPill = BuildMusicCastPill();
            castPill.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(castPill);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            return row;
        }

        // 投射到桌面：功能待接，先做状态切换
        Border BuildMusicCastPill()
        {
            musicCastButton = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            musicCastButton.MouseEnter += delegate { if (!musicCasting) musicCastButton.Background = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)); };
            musicCastButton.MouseLeave += delegate { UpdateMusicCastVisual(); };
            musicCastButton.MouseLeftButtonUp += delegate { ToggleMusicCast(); };
            UpdateMusicCastVisual();
            return musicCastButton;
        }

        void ToggleMusicCast()
        {
            if (musicStageWindow != null || musicWebStageWindow != null)
            {
                CloseMusicStage();
                return;
            }

            musicCasting = true;
            MusicDebug("cast web open");
            try
            {
                MusicDesktopWebStageWindow web = null;
                web = new MusicDesktopWebStageWindow(
                    BuildMusicStageSnapshot,
                    ToggleMusicPlayback,
                    delegate { NextMusicSong(1); },
                    delegate { NextMusicSong(-1); },
                    SeekMusicTo,
                    CycleMusicPlayMode,
                    delegate
                    {
                        if (musicWebStageWindow == web)
                        {
                            musicWebStageWindow = null;
                            musicCasting = false;
                            UpdateMusicCastVisual();
                        }
                    },
                    RaiseCapsuleAboveCast);
                musicWebStageWindow = web;
                web.Failed += delegate
                {
                    if (musicWebStageWindow != web) return;
                    musicWebStageWindow = null;
                    try { web.Close(); } catch (Exception) { }
                    MusicDebug("web stage failed, fallback to WPF");
                    OpenWpfCastFallback();
                };
                web.Show();
                RaiseCapsuleAboveCast();
                UpdateMusicCastVisual();
                ShowToolMessage("音乐", "已投射到桌面：WebView2 花火原型，Esc 或 × 收回");
            }
            catch (Exception exception)
            {
                MusicDebug("cast web open failed: " + exception.Message);
                if (musicWebStageWindow != null)
                {
                    try { musicWebStageWindow.Close(); } catch (Exception) { }
                    musicWebStageWindow = null;
                }
                OpenWpfCastFallback();
            }
        }

        void OpenWpfCastFallback()
        {
            if (musicStageWindow != null)
            {
                musicCasting = true;
                UpdateMusicCastVisual();
                return;
            }
            try
            {
                MusicDesktopStageWindow stage = null;
                stage = new MusicDesktopStageWindow(
                    BuildMusicStageSnapshot,
                    ToggleMusicPlayback,
                    delegate { NextMusicSong(1); },
                    delegate { NextMusicSong(-1); },
                    SeekMusicTo,
                    CycleMusicPlayMode,
                    delegate
                    {
                        if (musicStageWindow == stage)
                        {
                            musicStageWindow = null;
                            musicCasting = false;
                            UpdateMusicCastVisual();
                        }
                    },
                    RaiseCapsuleAboveCast);
                musicStageWindow = stage;
                stage.Show();
                musicCasting = true;
                RaiseCapsuleAboveCast();
                UpdateMusicCastVisual();
                ShowToolMessage("音乐", "已投射到桌面（WPF 回退版）");
            }
            catch (Exception exception)
            {
                musicCasting = false;
                musicStageWindow = null;
                musicWebStageWindow = null;
                UpdateMusicCastVisual();
                MusicDebug("cast fallback failed: " + exception.Message);
                ShowToolMessage("音乐", "投射打开失败：" + exception.Message);
            }
        }

        void CloseMusicStage()
        {
            MusicDesktopStageWindow window = musicStageWindow;
            MusicDesktopWebStageWindow web = musicWebStageWindow;
            musicStageWindow = null;
            musicWebStageWindow = null;
            musicCasting = false;
            if (window != null)
            {
                try { window.Close(); } catch (Exception) { }
            }
            if (web != null)
            {
                try { web.Close(); } catch (Exception) { }
            }
            UpdateMusicCastVisual();
        }

        // 把胶囊/音乐面板所在的宿主窗口提到 Topmost 最上层，但不抢焦点。
        // 投射窗是全屏 Topmost，单靠 WPF 的 Topmost 切换在“投射窗刚激活”时
        // 仍可能盖住胶囊；用 SetWindowPos(HWND_TOPMOST) 明确把宿主放到它上面。
        void RaiseCapsuleAboveCast()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                // HWND_TOPMOST = -1, SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE
                // 先把投射层钉到 Topmost 最上层（盖住任务栏），再把宿主放它上面。
                if (musicWebStageWindow != null)
                {
                    IntPtr stageHwnd = new WindowInteropHelper(musicWebStageWindow).Handle;
                    if (stageHwnd != IntPtr.Zero)
                    {
                        SetWindowPos(stageHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001u | 0x0002u | 0x0010u);
                    }
                }
                else if (musicStageWindow != null)
                {
                    IntPtr stageHwnd = new WindowInteropHelper(musicStageWindow).Handle;
                    if (stageHwnd != IntPtr.Zero)
                    {
                        SetWindowPos(stageHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001u | 0x0002u | 0x0010u);
                    }
                }
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001u | 0x0002u | 0x0010u);
            }
            catch (Exception) { }
        }

        // 投射窗每 33ms 拉一次快照；这里只做字段拷贝，不启动新线程。
        MusicStageSnapshot BuildMusicStageSnapshot()
        {
            var snapshot = new MusicStageSnapshot();
            if (musicCurrentSong != null)
            {
                snapshot.SongMid = musicCurrentSong.Mid == null ? "" : musicCurrentSong.Mid;
                snapshot.Title = musicCurrentSong.Title == null ? "" : musicCurrentSong.Title;
                snapshot.Artist = musicCurrentSong.Artists == null ? "" : musicCurrentSong.Artists;
                snapshot.AlbumMid = musicCurrentSong.AlbumMid == null ? "" : musicCurrentSong.AlbumMid;
            }
            if (musicPlayer != null)
            {
                try
                {
                    snapshot.Position = musicPlayer.Position;
                    snapshot.Duration = musicPlayer.NaturalDuration.HasTimeSpan
                        ? musicPlayer.NaturalDuration.TimeSpan
                        : TimeSpan.Zero;
                }
                catch (Exception) { }
            }
            snapshot.Playing = musicPlaying;
            snapshot.Resolving = musicResolving;
            MusicPlatform stagePlatform = MusicPlatforms.Find(musicPlatformId);
            snapshot.Source = stagePlatform == null ? "" : stagePlatform.Name;
            snapshot.PlayMode = musicPlayMode == MusicPlayMode.RepeatOne
                ? "repeat"
                : (musicPlayMode == MusicPlayMode.Sequential ? "sequential" : "shuffle");
            snapshot.Lyrics = musicStageLyrics;
            snapshot.LyricsVersion = musicStageLyricsVersion;
            return snapshot;
        }

        // 投射控制条拖动进度：和点歌词行共用同一条 seek 逻辑
        void SeekMusicTo(TimeSpan position)
        {
            if (musicCurrentSong == null) return;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (musicResolving)
            {
                musicPendingSeek = position;
                musicPendingSeekMid = musicCurrentSong.Mid;
                return;
            }
            if (string.IsNullOrEmpty(musicCurrentUrl))
            {
                musicPendingSeek = position;
                musicPendingSeekMid = musicCurrentSong.Mid;
                PlayMusicSong(musicCurrentSong);
                return;
            }
            EnsureMusicPlayer();
            try
            {
                musicPlayer.Position = position;
                if (!musicPlaying)
                {
                    musicPlayer.Play();
                    SetMusicPlaying(true);
                }
            }
            catch (Exception) { }
        }

        void UpdateMusicCastVisual()
        {
            if (musicCastButton == null) return;
            Color glyphColor = musicCasting ? MusicAccentFrom : Color.FromArgb(215, 226, 232, 242);
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(MusicGlyph("cast", glyphColor, 11));
            content.Children.Add(new TextBlock
            {
                Text = musicCasting ? "收回投射" : "投射到桌面",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(musicCasting ? MusicAccentFrom : Color.FromArgb(205, 226, 232, 242)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            musicCastButton.Child = content;
            musicCastButton.Background = musicCasting
                ? new SolidColorBrush(Color.FromArgb(26, MusicAccentFrom.R, MusicAccentFrom.G, MusicAccentFrom.B))
                : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            musicCastButton.BorderBrush = musicCasting
                ? new SolidColorBrush(Color.FromArgb(90, MusicAccentFrom.R, MusicAccentFrom.G, MusicAccentFrom.B))
                : new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            musicCastButton.ToolTip = musicCasting ? "收回桌面投射" : "投射到桌面";
        }

        // 队列按钮（胶囊形小条），浏览条和海报底栏共用
        Border BuildQueuePill()
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(MusicGlyph("queue", Color.FromArgb(225, 232, 236, 243), 12));
            content.Children.Add(new TextBlock
            {
                Text = "队列",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 226, 232, 242)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });

            var pill = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "播放队列",
                Child = content
            };
            pill.MouseEnter += delegate { pill.Background = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)); };
            pill.MouseLeave += delegate { pill.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)); };
            pill.MouseLeftButtonUp += delegate { OpenMusicQueueSheet(); };
            return pill;
        }

        // 浏览歌曲时的底部迷你条（图三那种）：播放键 + 歌名 + 进度 + 队列
        FrameworkElement BuildBrowseMiniBar()
        {
            var bar = new Border
            {
                Height = 58,
                Margin = new Thickness(0, 8, 0, 0),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var play = MusicCircleButton("play", 34, Color.FromArgb(0, 0, 0, 0));
            play.Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 1));
            play.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5, Color = MusicAccentFrom };
            play.MouseLeftButtonUp += delegate { ToggleMusicPlayback(); };
            musicBrowsePlayButton = play;
            Grid.SetColumn(play, 0);
            grid.Children.Add(play);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            musicBrowseTitle = new TextBlock
            {
                Text = "未在播放",
                FontFamily = AiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MusicTextColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            mid.Children.Add(musicBrowseTitle);

            var progress = new Grid { Height = 14, Margin = new Thickness(0, 3, 0, 0) };
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            musicBrowseTimeLeft = MusicTimeText("0:00", HorizontalAlignment.Left);
            musicBrowseTimeLeft.FontSize = 9;
            Grid.SetColumn(musicBrowseTimeLeft, 0);
            progress.Children.Add(musicBrowseTimeLeft);

            var track = new Grid { Height = 12, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            musicBrowseProgressFill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Width = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 0))
            };
            track.Children.Add(musicBrowseProgressFill);
            Grid.SetColumn(track, 1);
            progress.Children.Add(track);

            musicBrowseTimeRight = MusicTimeText("0:00", HorizontalAlignment.Right);
            musicBrowseTimeRight.FontSize = 9;
            Grid.SetColumn(musicBrowseTimeRight, 2);
            progress.Children.Add(musicBrowseTimeRight);

            mid.Children.Add(progress);
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            FrameworkElement pill = BuildQueuePill();
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);

            bar.Child = grid;
            bar.Cursor = Cursors.Hand;
            bar.ToolTip = "点一下打开海报播放";
            // 点条子空白处 = 打开当前歌曲的海报；播放键和队列胶囊各自有功能，不抢
            bar.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (musicBrowsePlayButton != null && musicBrowsePlayButton.IsMouseOver) return;
                if (pill.IsMouseOver) return;
                OpenMusicPoster();
            };

            musicBrowseBar = bar;
            return bar;
        }

        // 打开海报播放（列表 / 迷你条 → 海报）
        void OpenMusicPoster()
        {
            if (musicPosterStage == null || musicCurrentSong == null) return;
            musicPosterStage.Visibility = Visibility.Visible;
            if (musicBrowseBar != null) musicBrowseBar.Visibility = Visibility.Collapsed;
            CloseMusicQueueSheet();
        }

        void ExitMusicPoster()
        {
            if (musicPosterStage == null) return;
            musicPosterStage.Visibility = Visibility.Collapsed;
            if (musicBrowseBar != null) musicBrowseBar.Visibility = Visibility.Visible;
            CloseMusicQueueSheet();
        }

        // 给元素套一个圆角裁剪（WPF 的 Border 不会自动裁剪子元素）
        static void ApplyRoundedClip(FrameworkElement element, double radius)
        {
            if (element == null) return;
            var clip = new RectangleGeometry { RadiusX = radius, RadiusY = radius };
            element.Clip = clip;
            element.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
            {
                clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            };
        }

        // 封面：圆角方块 + 唱片纹路 + 中间渐变圆（真实封面接进来时替换中间那层）
        FrameworkElement BuildMusicCoverOnce(Grid coverRow)
        {
            FrameworkElement cover = BuildMusicCover();
            coverRow.Children.Add(cover);
            return cover;
        }

        FrameworkElement BuildMusicCover()
        {
            var disc = new Grid();
            disc.Children.Add(new Ellipse { Margin = new Thickness(12), Stroke = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)), StrokeThickness = 1 });
            disc.Children.Add(new Ellipse { Margin = new Thickness(28), Stroke = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), StrokeThickness = 1 });
            disc.Children.Add(new Ellipse { Margin = new Thickness(44), Stroke = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), StrokeThickness = 1 });

            var core = new Border
            {
                Width = 62,
                Height = 62,
                CornerRadius = new CornerRadius(31),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.45, Color = MusicAccentFrom },
                Child = MusicGlyph("note", Colors.White, 26)
            };
            disc.Children.Add(core);

            musicCoverHost = new Border
            {
                Width = 168,
                Height = 168,
                CornerRadius = new CornerRadius(16),
                Background = new LinearGradientBrush(
                    Color.FromRgb(45, 52, 64), Color.FromRgb(25, 30, 40),
                    new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = disc
            };
            // 高光：左上角一层很淡的白，让方块不那么平
            var sheen = new Border
            {
                CornerRadius = new CornerRadius(16),
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(
                    Color.FromArgb(18, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0.7, 0.7))
            };
            var host = new Grid { Width = 148, Height = 148 };
            host.Children.Add(musicCoverHost);
            host.Children.Add(sheen);
            return host;
        }

        Grid BuildMusicProgress()
        {
            var progress = new Grid { Height = 20, Margin = new Thickness(0, 10, 0, 0) };
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            musicTimeLeft = MusicTimeText("0:00", HorizontalAlignment.Left);
            Grid.SetColumn(musicTimeLeft, 0);
            progress.Children.Add(musicTimeLeft);

            var trackHost = new Grid { Height = 20, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            musicProgressRail = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            trackHost.Children.Add(musicProgressRail);

            musicProgressFill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 0,
                Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 0))
            };
            trackHost.Children.Add(musicProgressFill);

            musicProgressKnob = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Color.FromRgb(240, 244, 250)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(0, 0),
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black }
            };
            trackHost.Children.Add(musicProgressKnob);

            // 悬停时轨道和圆点"长胖"，暗示可以拖
            trackHost.MouseEnter += delegate
            {
                Motion.AnimateDouble(musicProgressRail, FrameworkElement.HeightProperty, musicProgressRail.Height, 6, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressFill, FrameworkElement.HeightProperty, musicProgressFill.Height, 6, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressKnob, FrameworkElement.WidthProperty, musicProgressKnob.Width, 13, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressKnob, FrameworkElement.HeightProperty, musicProgressKnob.Height, 13, Motion.HoverMs, Motion.Enter, 0, null);
            };
            trackHost.MouseLeave += delegate
            {
                Motion.AnimateDouble(musicProgressRail, FrameworkElement.HeightProperty, musicProgressRail.Height, 4, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressFill, FrameworkElement.HeightProperty, musicProgressFill.Height, 4, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressKnob, FrameworkElement.WidthProperty, musicProgressKnob.Width, 10, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(musicProgressKnob, FrameworkElement.HeightProperty, musicProgressKnob.Height, 10, Motion.HoverMs, Motion.Enter, 0, null);
            };

            Grid.SetColumn(trackHost, 1);
            progress.Children.Add(trackHost);

            musicTimeRight = MusicTimeText("0:00", HorizontalAlignment.Right);
            Grid.SetColumn(musicTimeRight, 2);
            progress.Children.Add(musicTimeRight);
            return progress;
        }

        Grid BuildMusicControls()
        {
            var controls = new Grid { Height = 50, Margin = new Thickness(0, 6, 0, 0) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var center = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var prev = MusicCircleButton("prev", 34, Color.FromArgb(16, 255, 255, 255));
            var play = MusicCircleButton("play", 52, Color.FromArgb(0, 0, 0, 0));
            play.Margin = new Thickness(16, 0, 16, 0);
            play.Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 1));
            play.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55, Color = MusicAccentFrom };
            var next = MusicCircleButton("next", 34, Color.FromArgb(16, 255, 255, 255));
            center.Children.Add(prev);
            center.Children.Add(play);
            center.Children.Add(next);
            musicPlayButton = play;
            Grid.SetColumn(center, 1);
            controls.Children.Add(center);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(MusicGlyph("repeat", Color.FromArgb(140, 148, 156, 172), 14));
            right.Children.Add(BuildMusicVolume());
            Grid.SetColumn(right, 2);
            controls.Children.Add(right);
            return controls;
        }

        // 音量条：轨道 + 填充 + 圆点（先做静态 70%，接播放后连到 MediaPlayer.Volume）
        FrameworkElement BuildMusicVolume()
        {
            var host = new Grid
            {
                Width = 74,
                Height = 20,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "音量"
            };
            host.Children.Add(new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });
            musicVolumeFill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Width = 74 * musicVolume,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(190, 160, 168, 184))
            };
            host.Children.Add(musicVolumeFill);
            musicVolumeKnob = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(232, 236, 243)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(74 * musicVolume - 4.5, 0)
            };
            host.Children.Add(musicVolumeKnob);

            // 点击 / 拖动都能调音量
            host.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                host.CaptureMouse();
                SetMusicVolumeFromPoint(e.GetPosition(host).X);
                e.Handled = true;
            };
            host.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                SetMusicVolumeFromPoint(e.GetPosition(host).X);
            };
            host.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                try { if (host.IsMouseCaptured) host.ReleaseMouseCapture(); } catch (Exception) { }
                SetMusicVolumeFromPoint(e.GetPosition(host).X);
                e.Handled = true;
            };

            var wrap = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            musicVolumeIcon = new Border
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "静音 / 恢复音量",
                Child = MusicGlyph("volume", Color.FromArgb(200, 168, 176, 190), 13)
            };
            musicVolumeIcon.MouseLeftButtonUp += delegate { ToggleMusicMute(); };
            wrap.Children.Add(musicVolumeIcon);
            wrap.Children.Add(host);
            ApplyMusicVolume(musicVolume);
            return wrap;
        }

        // 把 0~1 的音量同时应用到滑块和播放器
        void ApplyMusicVolume(double ratio)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            musicVolume = ratio;
            if (musicVolumeFill != null) musicVolumeFill.Width = 74 * ratio;
            if (musicVolumeKnob != null) musicVolumeKnob.RenderTransform = new TranslateTransform(74 * ratio - 4.5, 0);
            if (musicVolumeIcon != null) musicVolumeIcon.Opacity = ratio <= 0.001 ? 0.45 : 1;
            if (musicPlayer != null)
            {
                try { musicPlayer.Volume = ratio; } catch (Exception) { }
            }
        }

        void SetMusicVolumeFromPoint(double x)
        {
            ApplyMusicVolume(x / 74.0);
        }

        void ToggleMusicMute()
        {
            if (musicVolume > 0.001)
            {
                musicMutedVolume = musicVolume;
                ApplyMusicVolume(0);
            }
            else
            {
                ApplyMusicVolume(musicMutedVolume > 0.001 ? musicMutedVolume : 0.7);
            }
        }

        Border BuildMusicLoginOverlay()
        {
            var card = new Border
            {
                Width = 640,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(31, 36, 44)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 20, 24, 18),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Opacity = 0.5 }
            };
            musicLoginCard = card;

            var host = new Grid();
            host.Children.Add(BuildMusicPlatformStage());
            host.Children.Add(BuildMusicQrStage());
            card.Child = host;

            var overlay = new Border
            {
                // 不透明：底下是播放器骨架，半透明会透出"还没有在播放"那行大字，看着像残影
                Background = new SolidColorBrush(Color.FromRgb(27, 33, 40)),
                CornerRadius = new CornerRadius(0, 0, 29, 29),
                Child = card
            };
            return overlay;
        }

        // ------------------------------------------------------------ 平台选择

        StackPanel BuildMusicPlatformStage()
        {
            var stack = new StackPanel();
            musicPlatformStage = stack;

            musicLoginTitle = new TextBlock
            {
                Text = "先登录一个音乐账号",
                FontFamily = AiFont,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor)
            };
            stack.Children.Add(musicLoginTitle);
            stack.Children.Add(new TextBlock
            {
                Text = "登录后读取账号里的歌单。这一版先把登录链路打通，歌单和播放下一步接。",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(MusicMutedColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 16)
            });

            var tiles = new Grid();
            MusicPlatform[] platforms = MusicPlatforms.All;
            for (int i = 0; i < platforms.Length; i++)
            {
                tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                FrameworkElement tile = BuildMusicLoginTile(platforms[i]);
                Grid.SetColumn(tile, i);
                tiles.Children.Add(tile);
            }
            stack.Children.Add(tiles);

            stack.Children.Add(new TextBlock
            {
                Text = "网易云可以直接在面板里扫码；其它平台暂时是独立登录窗（胶囊里塞不进网页）。账号只存在本机 data\\music 下。",
                FontFamily = AiFont,
                FontSize = 10,
                LineHeight = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(130, 148, 156, 172)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            });

            // 已登录时这张卡是"换平台"，得有路退回去；没登录时它就是主体，不需要取消。
            var foot = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            musicLoginCancel = ToolButton("取消", 12);
            musicLoginCancel.HorizontalAlignment = HorizontalAlignment.Right;
            musicLoginCancel.Visibility = Visibility.Collapsed;
            musicLoginCancel.Click += delegate { HideMusicLoginPicker(); };
            foot.Children.Add(musicLoginCancel);
            stack.Children.Add(foot);

            return stack;
        }

        // ------------------------------------------------------------ 原生扫码

        StackPanel BuildMusicQrStage()
        {
            var stack = new StackPanel { Visibility = Visibility.Collapsed };
            musicQrStage = stack;

            musicQrTitle = new TextBlock
            {
                Text = "扫码登录",
                FontFamily = AiFont,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(musicQrTitle);

            musicQrImage = new Image
            {
                Width = 172,
                Height = 172,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            musicQrCard = new Border
            {
                Width = 196,
                Height = 196,
                Margin = new Thickness(0, 14, 0, 0),
                CornerRadius = new CornerRadius(14),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Grid
                {
                    Children = { musicQrImage }
                }
            };
            stack.Children.Add(musicQrCard);

            musicQrStatus = new TextBlock
            {
                Text = "正在生成二维码…",
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(MusicMutedColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                MinHeight = 32
            };
            stack.Children.Add(musicQrStatus);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            musicQrRefresh = ToolButton("刷新二维码", 12);
            musicQrRefresh.Margin = new Thickness(0, 0, 8, 0);
            musicQrRefresh.Visibility = Visibility.Collapsed;
            musicQrRefresh.Click += delegate { if (musicQrPlatform != null) ShowMusicQr(musicQrPlatform); };
            buttons.Children.Add(musicQrRefresh);

            // 扫码确认不上时的兜底：改用网页登录窗
            musicQrFallback = ToolButton("改用网页登录", 12);
            musicQrFallback.Margin = new Thickness(0, 0, 8, 0);
            musicQrFallback.Visibility = Visibility.Collapsed;
            musicQrFallback.Click += delegate { FallbackToWebLogin(musicQrPlatform); };
            buttons.Children.Add(musicQrFallback);

            var back = ToolButton("返回", 12);
            back.Click += delegate { ResetMusicLoginStage(); };
            buttons.Children.Add(back);
            stack.Children.Add(buttons);

            return stack;
        }

        void ChooseMusicPlatform(MusicPlatform platform)
        {
            if (platform == null) return;
            if (platform.NativeQr) ShowMusicQr(platform);
            else OpenMusicLogin(platform);
        }

        void ShowMusicQr(MusicPlatform platform)
        {
            if (platform == null || musicLoginCard == null) return;
            if (platform.Id == "kugou") { ShowKugouQr(platform); return; }
            if (platform.Id == "qishui") { ShowQishuiQr(platform); return; }
            StopMusicQr();

            musicQrPlatform = platform;
            musicLoginCard.Width = 320;
            if (musicPlatformStage != null) musicPlatformStage.Visibility = Visibility.Collapsed;
            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Visible;
            if (musicLoginCancel != null) musicLoginCancel.Visibility = Visibility.Collapsed;

            musicQrTitle.Text = "扫码登录 " + platform.Name;
            musicQrStatus.Text = "正在生成二维码…";
            musicQrImage.Source = null;
            musicQrRefresh.Visibility = Visibility.Collapsed;
            if (musicQrFallback != null)
            {
                musicQrFallback.Visibility = platform.Id == "netease" ? Visibility.Visible : Visibility.Collapsed;
            }

            var session = new MusicQrLogin(platform);
            musicQrSession = session;
            session.QrReady += delegate(ImageSource image)
            {
                if (musicQrSession != session) return;
                musicQrImage.Source = image;
                // 按图片原始像素显示（模块是整数倍），不做缩放，扫码才稳
                musicQrImage.Width = image.Width;
                musicQrImage.Height = image.Height;
                if (musicQrCard != null)
                {
                    musicQrCard.Width = image.Width + 24;
                    musicQrCard.Height = image.Height + 24;
                }
            };
            session.StatusChanged += delegate(string text)
            {
                if (musicQrSession != session) return;
                musicQrStatus.Text = text;
                musicQrRefresh.Visibility = Visibility.Collapsed;
            };
            session.Expired += delegate
            {
                if (musicQrSession != session) return;
                musicQrStatus.Text = "二维码过期了，点「刷新二维码」重新生成";
                musicQrRefresh.Visibility = Visibility.Visible;
            };
            session.Failed += delegate(string message)
            {
                if (musicQrSession != session) return;
                musicQrStatus.Text = message;
                musicQrRefresh.Visibility = Visibility.Visible;
            };
            session.Succeeded += delegate(Dictionary<string, string> cookies)
            {
                if (musicQrSession != session) return;
                OnMusicQrSucceeded(platform, cookies);
            };
            session.Start();
        }

        // ---------------- 酷狗：官方扫码接口（拿到的 token 才能被歌单网关接受）

        DispatcherTimer kugouQrTimer;
        string kugouQrKey = "";

        // ---------------------------------------------------------------- 汽水扫码

        void StopQishuiQrPoll()
        {
            if (qishuiQrTimer != null)
            {
                try { qishuiQrTimer.Stop(); } catch (Exception) { }
                qishuiQrTimer = null;
            }
            qishuiQrToken = "";
        }

        void ShowQishuiQr(MusicPlatform platform)
        {
            if (platform == null || musicLoginCard == null) return;
            StopQishuiQrPoll();
            qishuiQrPlatform = platform;
            musicQrPlatform = platform;
            musicLoginCard.Width = 320;
            if (musicPlatformStage != null) musicPlatformStage.Visibility = Visibility.Collapsed;
            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Visible;
            if (musicLoginCancel != null) musicLoginCancel.Visibility = Visibility.Collapsed;
            musicQrTitle.Text = "扫码登录 " + platform.Name;
            musicQrStatus.Text = "正在生成二维码…";
            musicQrImage.Source = null;
            musicQrRefresh.Visibility = Visibility.Collapsed;

            Task.Run(delegate
            {
                string token;
                byte[] png;
                bool ok = QishuiApi.BeginLogin(out token, out png);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (qishuiQrPlatform != platform) return;
                    if (!ok || png == null)
                    {
                        musicQrStatus.Text = "二维码生成失败：" + QishuiApi.LastError;
                        musicQrRefresh.Visibility = Visibility.Visible;
                        return;
                    }
                    qishuiQrToken = token;
                    ShowKugouQrImage(ImageFromBytes(png));
                    musicQrStatus.Text = "用抖音 / 汽水 App 扫码授权";
                    qishuiQrTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    qishuiQrTimer.Tick += delegate { PollQishuiQr(); };
                    qishuiQrTimer.Start();
                }));
            });
        }

        static ImageSource ImageFromBytes(byte[] bytes)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new System.IO.MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        void PollQishuiQr()
        {
            string token = qishuiQrToken;
            MusicPlatform platform = qishuiQrPlatform;
            if (string.IsNullOrEmpty(token) || platform == null) { StopQishuiQrPoll(); return; }
            Task.Run(delegate
            {
                string authCode;
                QishuiApi.QrState state = QishuiApi.CheckLogin(token, out authCode);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (qishuiQrPlatform != platform || qishuiQrToken != token) return;
                    if (state == QishuiApi.QrState.Waiting) { musicQrStatus.Text = "等待扫码…"; return; }
                    if (state == QishuiApi.QrState.Scanned) { musicQrStatus.Text = "已扫码，请在手机上确认"; return; }
                    if (state == QishuiApi.QrState.Expired || state == QishuiApi.QrState.Refused)
                    {
                        StopQishuiQrPoll();
                        musicQrStatus.Text = "二维码已失效，点「刷新二维码」重试";
                        musicQrRefresh.Visibility = Visibility.Visible;
                        return;
                    }
                    if (state == QishuiApi.QrState.Failed) return;

                    // confirmed：换凭据
                    StopQishuiQrPoll();
                    musicQrStatus.Text = "登录成功，正在换取凭据…";
                    Task.Run(delegate
                    {
                        bool ok = QishuiApi.FinishLogin(authCode);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (qishuiQrPlatform != platform) return;
                            if (!ok)
                            {
                                musicQrStatus.Text = "换取凭据失败：" + QishuiApi.LastError;
                                musicQrRefresh.Visibility = Visibility.Visible;
                                return;
                            }
                            musicPlatformId = platform.Id;
                            MusicSession.Save(platform.Id);
                            HideMusicLoginPicker();
                            ApplyMusicLoginVisual();
                            StartMusicImport(true);
                            ShowToolMessage("音乐", "已登录 " + platform.Name + "。正在读取歌单。");
                        }));
                    });
                }));
            });
        }
        void ShowKugouQr(MusicPlatform platform)
        {
            if (platform == null || musicLoginCard == null) return;
            StopKugouQrPoll();
            musicQrPlatform = platform;
            musicLoginCard.Width = 320;
            if (musicPlatformStage != null) musicPlatformStage.Visibility = Visibility.Collapsed;
            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Visible;
            if (musicLoginCancel != null) musicLoginCancel.Visibility = Visibility.Collapsed;
            musicQrTitle.Text = "扫码登录 " + platform.Name;
            musicQrStatus.Text = "正在生成二维码…";
            musicQrImage.Source = null;
            musicQrRefresh.Visibility = Visibility.Collapsed;

            Task.Run(delegate
            {
                string key;
                ImageSource qrImage;
                bool ok = KugouApi.RequestLoginQr(out key, out qrImage);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (musicQrPlatform != platform) return;
                    if (!ok)
                    {
                        musicQrStatus.Text = "二维码生成失败，点「刷新二维码」重试";
                        musicQrRefresh.Visibility = Visibility.Visible;
                        return;
                    }
                    kugouQrKey = key;
                    ShowKugouQrImage(qrImage);
                    musicQrStatus.Text = "用酷狗 App「扫一扫」登录";
                    StartKugouQrPoll(platform);
                }));
            });
        }

        void ShowKugouQrImage(ImageSource image)
        {
            if (image == null) return;
            try
            {
                musicQrImage.Source = image;
                double size = 220;
                musicQrImage.Width = size;
                musicQrImage.Height = size;
                if (musicQrCard != null)
                {
                    musicQrCard.Width = size + 40;
                    musicQrCard.Height = size + 40;
                }
            }
            catch (Exception) { }
        }

        void StartKugouQrPoll(MusicPlatform platform)
        {
            StopKugouQrPoll();
            kugouQrTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            kugouQrTimer.Tick += delegate
            {
                string key = kugouQrKey;
                if (string.IsNullOrEmpty(key)) return;
                Task.Run(delegate
                {
                    string token;
                    string userId;
                    int status = KugouApi.PollLoginQr(key, out token, out userId);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (musicQrPlatform != platform) return;
                        if (status == 1) musicQrStatus.Text = "等待扫码…";
                        else if (status == 2) musicQrStatus.Text = "已扫码，请在手机上确认";
                        else if (status == 4)
                        {
                            StopKugouQrPoll();
                            KugouApi.SetSession(token, userId);
                            musicPlatformId = platform.Id;
                            MusicSession.Save(platform.Id);
                            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Collapsed;
                            HideMusicLoginPicker();
                            ApplyMusicLoginVisual();
                            // 先补上网页 profile 里的 dfid/kg_mid，再拉歌单
                            KugouApi.FillDevice(platform, delegate { StartMusicImport(); });
                            ShowToolMessage("音乐", "已登录 " + platform.Name + "。正在读取歌单。");
                        }
                        else if (status == 0)
                        {
                            StopKugouQrPoll();
                            musicQrStatus.Text = "二维码过期了，点「刷新二维码」重新生成";
                            musicQrRefresh.Visibility = Visibility.Visible;
                        }
                    }));
                });
            };
            kugouQrTimer.Start();
        }

        void StopKugouQrPoll()
        {
            if (kugouQrTimer == null) return;
            try { kugouQrTimer.Stop(); } catch (Exception) { }
            kugouQrTimer = null;
        }

        // 扫码确认不上时，退回网页登录窗（页面里可以扫码/账号登录）
        internal void FallbackToWebLogin(MusicPlatform platform)
        {
            if (platform == null) return;
            StopMusicQr();
            OpenMusicLogin(platform);
        }

        void OnMusicQrSucceeded(MusicPlatform platform, Dictionary<string, string> cookies)
        {
            MusicCookieStore.Save(platform.Id, cookies);
            musicPlatformId = platform.Id;
            MusicSession.Save(platform.Id);
            StopMusicQr();
            HideMusicLoginPicker();
            ApplyMusicLoginVisual();
            StartMusicImport();
            ShowToolMessage("音乐", "已登录 " + platform.Name + "（App 扫码）。正在读取歌单。");
        }

        void StopMusicQr()
        {
            StopKugouQrPoll();
            StopQishuiQrPoll();
            if (musicQrSession == null) return;
            MusicQrLogin session = musicQrSession;
            musicQrSession = null;
            try { session.Stop(); } catch (Exception) { }
        }

        // 从"扫码"退回平台列表
        void ResetMusicLoginStage()
        {
            StopMusicQr();
            musicQrPlatform = null;
            if (musicLoginCard != null) musicLoginCard.Width = 640;
            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Collapsed;
            if (musicPlatformStage != null) musicPlatformStage.Visibility = Visibility.Visible;
            ApplyMusicLoginVisual();
        }

        FrameworkElement BuildMusicLoginTile(MusicPlatform platform)
        {
            var badge = new TextBlock
            {
                Text = platform.Badge,
                FontFamily = AiFont,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var circle = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(23),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new LinearGradientBrush(platform.ColorFrom, platform.ColorTo, new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45, Color = platform.ColorFrom },
                Child = badge
            };

            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(circle);
            content.Children.Add(new TextBlock
            {
                Text = platform.Name,
                FontFamily = AiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(MusicTextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = platform.Note,
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 148, 156, 172)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var tile = new Border
            {
                Height = 132,
                Margin = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "用 " + platform.Name + " 的官方网页登录",
                Child = content
            };
            System.Windows.Media.TransformGroup group = new System.Windows.Media.TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            tile.RenderTransform = group;
            tile.RenderTransformOrigin = new Point(0.5, 0.5);

            tile.MouseEnter += delegate
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                ScaleTransform scale = group.Children[0] as ScaleTransform;
                if (scale != null)
                {
                    Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, 1.04, Motion.HoverMs, Motion.Enter, 0, null);
                    Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, 1.04, Motion.HoverMs, Motion.Enter, 0, null);
                }
            };
            tile.MouseLeave += delegate
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
                ScaleTransform scale = group.Children[0] as ScaleTransform;
                if (scale != null)
                {
                    Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, 1.0, Motion.HoverMs, Motion.Enter, 0, null);
                    Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, 1.0, Motion.HoverMs, Motion.Enter, 0, null);
                }
            };
            tile.MouseLeftButtonUp += delegate { ChooseMusicPlatform(platform); };
            return tile;
        }

        // ------------------------------------------------------------ 小零件

        static TextBlock MusicTimeText(string text, HorizontalAlignment align)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 148, 156, 172)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = align
            };
        }

        // 圆形控制键：悬停放大 + 底色变亮（第一步还没接播放，按下不做事，但看得出"是按钮"）
        Border MusicCircleButton(string kind, double size, Color background, double glyphScale = 0.40)
        {
            var button = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(background),
                BorderBrush = new SolidColorBrush(Color.FromArgb(background.A == 0 ? (byte)0 : (byte)30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = MusicGlyph(kind, Color.FromArgb(240, 255, 255, 255), size * glyphScale)
            };

            var group = new TransformGroup();
            var scale = new ScaleTransform(1, 1);
            group.Children.Add(scale);
            button.RenderTransform = group;
            button.RenderTransformOrigin = new Point(0.5, 0.5);

            button.MouseEnter += delegate
            {
                Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, 1.08, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, 1.08, Motion.HoverMs, Motion.Enter, 0, null);
                if (background.A > 0) button.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            };
            button.MouseLeave += delegate
            {
                Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, 1.0, Motion.HoverMs, Motion.Enter, 0, null);
                Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, 1.0, Motion.HoverMs, Motion.Enter, 0, null);
                if (background.A > 0) button.Background = new SolidColorBrush(background);
            };
            return button;
        }

        FrameworkElement MusicGlyph(string kind, Color color, double size)
        {
            var canvas = new Canvas { Width = 20, Height = 20 };
            var brush = new SolidColorBrush(color);
            if (kind == "prev" || kind == "next")
            {
                var bar = new Rectangle
                {
                    Width = 2.4,
                    Height = 13,
                    RadiusX = 1.2,
                    RadiusY = 1.2,
                    Fill = brush
                };
                Canvas.SetLeft(bar, kind == "prev" ? 4 : 13.6);
                Canvas.SetTop(bar, 3.5);
                canvas.Children.Add(bar);

                var triangle = new Path
                {
                    Data = Geometry.Parse(kind == "prev" ? "M16,3.5 L8,10 L16,16.5 Z" : "M4,3.5 L12,10 L4,16.5 Z"),
                    Fill = brush
                };
                canvas.Children.Add(triangle);
            }
            else if (kind == "play")
            {
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M6,3.2 L16.4,10 L6,16.8 Z"),
                    Fill = brush
                });
            }
            else if (kind == "repeat")
            {
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M4.5,9.5 L3.4,8.6 L6.6,5.4 L3.4,2.2 L4.5,1.3 L8.8,5.4 Z"),
                    Fill = brush
                });
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M15.5,10.5 L16.6,11.4 L13.4,14.6 L16.6,17.8 L15.5,18.7 L11.2,14.6 Z"),
                    Fill = brush
                });
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M4,6.5 A8,8 0 0 1 16,6.5"),
                    Stroke = brush,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M16,13.5 A8,8 0 0 1 4,13.5"),
                    Stroke = brush,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            else if (kind == "volume")
            {
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M3,7.4 L6.6,7.4 L11,3.4 L11,16.6 L6.6,12.6 L3,12.6 Z"),
                    Fill = brush
                });
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M13.4,7.2 A3.9,3.9 0 0 1 13.4,12.8"),
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M15.4,4.9 A7.2,7.2 0 0 1 15.4,15.1"),
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            else if (kind == "pause")
            {
                // 两条竖条
                var bar1 = new Rectangle { Width = 4, Height = 13, RadiusX = 1.2, RadiusY = 1.2, Fill = brush };
                Canvas.SetLeft(bar1, 5.2); Canvas.SetTop(bar1, 3.5);
                canvas.Children.Add(bar1);
                var bar2 = new Rectangle { Width = 4, Height = 13, RadiusX = 1.2, RadiusY = 1.2, Fill = brush };
                Canvas.SetLeft(bar2, 10.8); Canvas.SetTop(bar2, 3.5);
                canvas.Children.Add(bar2);
            }
            else if (kind == "person")
            {
                var head = new Ellipse { Width = 5.2, Height = 5.2, Fill = brush };
                Canvas.SetLeft(head, 7.4);
                Canvas.SetTop(head, 3.2);
                canvas.Children.Add(head);
                canvas.Children.Add(new Path { Data = Geometry.Parse("M6,16.4 A4.2,4.2 0 0 1 14.4,16.4"), Stroke = brush, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "cast")
            {
                // 投射：一块屏幕 + 中心点
                var screen = new Rectangle
                {
                    Width = 12.5,
                    Height = 9,
                    RadiusX = 2,
                    RadiusY = 2,
                    Stroke = brush,
                    StrokeThickness = 1.4,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(screen, 3.8);
                Canvas.SetTop(screen, 4.5);
                canvas.Children.Add(screen);
                var dot = new Ellipse { Width = 2.4, Height = 2.4, Fill = brush };
                Canvas.SetLeft(dot, 6);
                Canvas.SetTop(dot, 8);
                canvas.Children.Add(dot);
            }
            else if (kind == "refresh")
            {
                // 圆箭头：一段圆弧 + 箭头
                canvas.Children.Add(new Path { Data = Geometry.Parse("M12.4,4.6 A5.5,5.5 0 1 0 14.2,10.4"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M9.4,4.6 L12.7,4.6 L12.7,7.9"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "queue")
            {
                // 队列：三横线（最上面一条短一点，像列表）
                for (int i = 0; i < 3; i++)
                {
                    var line = new Rectangle
                    {
                        Width = i == 2 ? 12 : 16,
                        Height = 1.8,
                        RadiusX = 0.9,
                        RadiusY = 0.9,
                        Fill = brush
                    };
                    Canvas.SetLeft(line, i == 2 ? 2 : 2);
                    Canvas.SetTop(line, 4 + i * 5.2);
                    canvas.Children.Add(line);
                }
            }
            else if (kind == "shuffle")
            {
                // 随机：两条交叉箭头
                canvas.Children.Add(new Path { Data = Geometry.Parse("M3,5 L9,5 L15,15"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M3,15 L9,15 L15,5"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M13,3 L16,5 L13,7"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M13,13 L16,15 L13,17"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "sequential")
            {
                // 顺序：两条线 + 向下箭头
                var l1 = new Rectangle { Width = 12, Height = 1.8, RadiusX = 0.9, RadiusY = 0.9, Fill = brush };
                Canvas.SetLeft(l1, 2); Canvas.SetTop(l1, 5);
                canvas.Children.Add(l1);
                var l2 = new Rectangle { Width = 12, Height = 1.8, RadiusX = 0.9, RadiusY = 0.9, Fill = brush };
                Canvas.SetLeft(l2, 2); Canvas.SetTop(l2, 10);
                canvas.Children.Add(l2);
                canvas.Children.Add(new Path { Data = Geometry.Parse("M13,13 L15,16 L17,13"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "sliders")
            {
                // 两条滑杆：横线 + 两个圆点（队列按钮用）
                var line1 = new Rectangle { Width = 16, Height = 2, RadiusX = 1, RadiusY = 1, Fill = brush };
                Canvas.SetLeft(line1, 2); Canvas.SetTop(line1, 5.2);
                canvas.Children.Add(line1);
                var line2 = new Rectangle { Width = 16, Height = 2, RadiusX = 1, RadiusY = 1, Fill = brush };
                Canvas.SetLeft(line2, 2); Canvas.SetTop(line2, 12.8);
                canvas.Children.Add(line2);
                var dot1 = new Ellipse { Width = 4.6, Height = 4.6, Fill = brush };
                Canvas.SetLeft(dot1, 5.5); Canvas.SetTop(dot1, 3.9);
                canvas.Children.Add(dot1);
                var dot2 = new Ellipse { Width = 4.6, Height = 4.6, Fill = brush };
                Canvas.SetLeft(dot2, 10.2); Canvas.SetTop(dot2, 11.5);
                canvas.Children.Add(dot2);
            }
            else if (kind == "gear")
            {
                // 齿轮：外圈 + 8 个齿 + 中心孔
                var ring = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Stroke = brush,
                    StrokeThickness = 1.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                canvas.Children.Add(ring);
                for (int i = 0; i < 4; i++)
                {
                    var tooth = new Rectangle
                    {
                        Width = 2,
                        Height = 17,
                        RadiusX = 1,
                        RadiusY = 1,
                        Fill = brush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        RenderTransform = new RotateTransform(i * 45),
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                    canvas.Children.Add(tooth);
                }
                var hole = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new SolidColorBrush(Color.FromRgb(34, 39, 47)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                canvas.Children.Add(hole);
            }
            else
            {
                // note：封面占位的音符，几何跟顶栏绿球同一套
                canvas.Width = 16;
                canvas.Height = 16;
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M11.7,2.0 L11.7,8.9 A2.9,2.9 0 1 1 10.3,6.3 L10.3,4.5 L6.0,5.3 L6.0,11.2 A2.9,2.9 0 1 1 4.6,8.6 L4.6,3.5 Z"),
                    Fill = brush
                });
            }
            return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        }

        // 占位条：歌单行 / 歌曲行。故意做成"骨架"（灰条），不做假数据。
        FrameworkElement MusicSkeletonRow(double barWidth, double height)
        {
            var row = new Grid { Height = height, Margin = new Thickness(0, 0, 4, 4) };
            var bg = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255))
            };
            row.Children.Add(bg);
            row.Children.Add(new Border
            {
                Width = barWidth,
                Height = Math.Max(6, height * 0.26),
                CornerRadius = new CornerRadius(height * 0.13),
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            return row;
        }

        // 歌单行占位：小方封面 + 两条灰条（名字 / 数量），带悬停高亮
        FrameworkElement MusicPlaylistRow(double nameWidth, double countWidth)
        {
            var grid = new Grid { Height = 38, Margin = new Thickness(0, 0, 2, 4), Cursor = Cursors.Hand };
            var bg = new Border { CornerRadius = new CornerRadius(9), Background = Brushes.Transparent };
            grid.Children.Add(bg);

            var thumb = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromArgb(70, MusicAccentFrom.R, MusicAccentFrom.G, MusicAccentFrom.B),
                    Color.FromArgb(40, MusicAccentTo.R, MusicAccentTo.G, MusicAccentTo.B),
                    new Point(0, 0), new Point(1, 1)),
                Child = MusicGlyph("note", Color.FromArgb(90, 255, 255, 255), 12)
            };
            grid.Children.Add(thumb);

            var bars = new StackPanel { Margin = new Thickness(40, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            bars.Children.Add(new Border
            {
                Width = nameWidth,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
            });
            bars.Children.Add(new Border
            {
                Width = countWidth,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
            });
            grid.Children.Add(bars);

            grid.MouseEnter += delegate { bg.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)); };
            grid.MouseLeave += delegate { bg.Background = Brushes.Transparent; };
            return grid;
        }

        // 歌曲行占位：序号 + 两条灰条 + 尾部时长条
        FrameworkElement MusicSongRow(int index, double titleWidth, double artistWidth)
        {
            var grid = new Grid { Height = 34, Margin = new Thickness(0, 0, 4, 2), Cursor = Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var bg = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            Grid.SetColumnSpan(bg, 3);
            grid.Children.Add(bg);

            var number = new TextBlock
            {
                Text = index.ToString(),
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(110, 148, 156, 172)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(number, 0);
            grid.Children.Add(number);

            var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bars.Children.Add(new Border
            {
                Width = titleWidth,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255))
            });
            bars.Children.Add(new Border
            {
                Width = artistWidth,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(9, 255, 255, 255))
            });
            Grid.SetColumn(bars, 1);
            grid.Children.Add(bars);

            var duration = new Border
            {
                Width = 30,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))
            };
            Grid.SetColumn(duration, 2);
            grid.Children.Add(duration);

            grid.MouseEnter += delegate { bg.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)); };
            grid.MouseLeave += delegate { bg.Background = Brushes.Transparent; };
            return grid;
        }

        void RebuildMusicPlaceholders()
        {
            if (musicPlaylistList != null)
            {
                musicPlaylistList.Children.Clear();
                double[] names = new double[] { 72, 58, 80, 52, 66 };
                double[] counts = new double[] { 34, 28, 38, 26, 32 };
                for (int i = 0; i < names.Length; i++)
                {
                    musicPlaylistList.Children.Add(MusicPlaylistRow(names[i], counts[i]));
                }
            }
            if (musicSongList != null)
            {
                musicSongList.Children.Clear();
                double[] titles = new double[] { 210, 168, 236, 184, 152 };
                double[] artists = new double[] { 88, 72, 96, 64, 80 };
                for (int i = 0; i < titles.Length; i++)
                {
                    musicSongList.Children.Add(MusicSongRow(i + 1, titles[i], artists[i]));
                }
            }
        }

        static void MusicDebug(string message)
        {
            try
            {
                string folder = System.IO.Path.Combine(AiConfigStore.DataDir, "music");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "ui.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        // ================================================================ 胶囊联动

        // 胶囊收起时如果正在放歌：变宽成迷你播放器（高度不变，只改宽度）
        Grid BuildCapsuleMiniPlayer()
        {
            var grid = new Grid
            {
                Visibility = Visibility.Collapsed,
                Opacity = 0,
                IsHitTestVisible = true,
                // 透明背景：让整条迷你播放器都可命中，点空白处不会误触发展开
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 三块紧凑地排成一行（不再用 Star 列撑开）
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 左边：旋转的唱片（专辑封面）
            capsuleMiniDisc = new Border
            {
                Width = 25,
                Height = 25,
                CornerRadius = new CornerRadius(12.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            capsuleMiniDiscSpin = new RotateTransform(0);
            capsuleMiniDisc.RenderTransform = capsuleMiniDiscSpin;
            capsuleMiniDisc.RenderTransformOrigin = new Point(0.5, 0.5);
            capsuleMiniDisc.Child = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(230, 22, 26, 32)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(capsuleMiniDisc);

            // 中间：上一首 / 播放暂停 / 下一首（图案放大，按钮尺寸不变）
            var prev = MusicCircleButton("prev", 20, Color.FromArgb(22, 255, 255, 255), 0.62);
            var play = MusicCircleButton("play", 26, Color.FromArgb(0, 0, 0, 0), 0.56);
            play.Background = new LinearGradientBrush(MusicAccentFrom, MusicAccentTo, new Point(0, 0), new Point(1, 1));
            play.Margin = new Thickness(10, 0, 10, 0);
            var next = MusicCircleButton("next", 20, Color.FromArgb(22, 255, 255, 255), 0.62);
            capsuleMiniPlayButton = play;
            capsuleMiniButtons.Clear();
            capsuleMiniButtons.Add(prev);
            capsuleMiniButtons.Add(play);
            capsuleMiniButtons.Add(next);
            prev.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; NextMusicSong(-1); };
            play.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; ToggleMusicPlayback(); };
            next.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; NextMusicSong(1); };
            row.Children.Add(prev);
            row.Children.Add(play);
            row.Children.Add(next);

            // 右边：小频谱
            var spectrum = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            capsuleSpectrumBars.Clear();
            for (int i = 0; i < 4; i++)
            {
                var bar = new Border
                {
                    Width = 3,
                    Height = 5,
                    CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(0, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(220, 126, 226, 194))
                };
                spectrum.Children.Add(bar);
                capsuleSpectrumBars.Add(bar);
            }
            row.Children.Add(spectrum);

            grid.Children.Add(row);
            return grid;
        }

        void UpdateCapsuleMusicMode()
        {
            if (capsule == null || capsuleMiniPlayer == null) return;
            bool shouldShow = musicPlaying && !expanded;
            if (shouldShow == capsuleMiniActive) return;
            capsuleMiniActive = shouldShow;
            if (shouldShow) EnterCapsuleMusicMode();
            else ExitCapsuleMusicMode();
        }

        void EnterCapsuleMusicMode()
        {
            capsuleMiniPlayer.Visibility = Visibility.Visible;
            capsuleMiniPlayer.Opacity = 0;
            Motion.Fade(capsuleMiniPlayer, 1, 180, Motion.Enter, 60, null);
            if (capsuleEyes != null)
            {
                Motion.Fade(capsuleEyes, 0, 120, Motion.Exit, 0, delegate
                {
                    if (capsuleEyes != null && capsuleMiniActive) capsuleEyes.Visibility = Visibility.Collapsed;
                });
            }
            AnimateCapsuleWidth(CapsuleMusicWidth);
            SetCapsuleMiniPlaying(musicPlaying);
        }

        void ExitCapsuleMusicMode()
        {
            AnimateCapsuleWidth(CapsuleIdleWidth);
            if (capsuleMiniPlayer != null)
            {
                Motion.Fade(capsuleMiniPlayer, 0, 110, Motion.Exit, 0, delegate
                {
                    if (capsuleMiniPlayer != null && !capsuleMiniActive) capsuleMiniPlayer.Visibility = Visibility.Collapsed;
                });
            }
            if (capsuleEyes != null)
            {
                capsuleEyes.Visibility = Visibility.Visible;
                Motion.Fade(capsuleEyes, 1, 170, Motion.Enter, 90, null);
            }
            SetCapsuleMiniPlaying(false);
        }

        void AnimateCapsuleWidth(double to)
        {
            if (capsule == null) return;
            Motion.AnimateDouble(capsule, FrameworkElement.WidthProperty, capsule.Width, to, 220, Motion.Enter, 0, null);
        }

        void SetCapsuleMiniPlaying(bool playing)
        {
            if (capsuleMiniPlayButton != null)
            {
                capsuleMiniPlayButton.Child = MusicGlyph(playing ? "pause" : "play", Color.FromArgb(240, 255, 255, 255), 26 * 0.56);
            }
            if (capsuleMiniDiscSpin != null)
            {
                if (playing)
                {
                    var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.6)) { RepeatBehavior = RepeatBehavior.Forever };
                    capsuleMiniDiscSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
                    StartCapsuleSpectrum();
                }
                else
                {
                    capsuleMiniDiscSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                    StopCapsuleSpectrum();
                }
            }
        }

        void StartCapsuleSpectrum()
        {
            if (capsuleSpectrumTimer == null)
            {
                capsuleSpectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
                capsuleSpectrumTimer.Tick += delegate { TickCapsuleSpectrum(); };
            }
            capsuleSpectrumTimer.Start();
        }

        void StopCapsuleSpectrum()
        {
            if (capsuleSpectrumTimer != null) capsuleSpectrumTimer.Stop();
            foreach (Border bar in capsuleSpectrumBars) bar.Height = 5;
        }

        void TickCapsuleSpectrum()
        {
            if (!musicPlaying) return;
            for (int i = 0; i < capsuleSpectrumBars.Count; i++)
            {
                double wave = Math.Abs(Math.Sin(DateTime.Now.Ticks / 1800000.0 + i * 1.7));
                capsuleSpectrumBars[i].Height = 4 + wave * 13 + musicRandom.Next(0, 3);
            }
        }

        // ================================================================ 播放

        // 播放模式：随机 → 单曲循环 → 顺序 → 随机
        enum MusicPlayMode { Shuffle, RepeatOne, Sequential }

        MusicPlayMode musicPlayMode = MusicPlayMode.Shuffle;
        Border musicQueueModeButton;
        FrameworkElement musicBrowseBar;
        Border musicVolumeFill;
        Ellipse musicVolumeKnob;
        Border musicVolumeIcon;
        double musicVolume = 0.7;
        double musicMutedVolume = 0.7;
        Border musicCastButton;
        bool musicCasting;
        MusicDesktopStageWindow musicStageWindow;
        MusicDesktopWebStageWindow musicWebStageWindow;
        readonly List<MusicStageLyric> musicStageLyrics = new List<MusicStageLyric>();
        int musicStageLyricsVersion;
        Border musicBrowsePlayButton;
        TextBlock musicBrowseTitle;
        TextBlock musicBrowseTimeLeft;
        TextBlock musicBrowseTimeRight;
        Border musicBrowseProgressFill;
        int musicAutoSkip;
        bool musicFailureSkipping;

        void EnsureMusicPlayer()
        {
            if (musicPlayer != null) return;
            musicPlayer = new MediaPlayer();
            musicPlayer.Volume = musicVolume;
            musicPlayer.MediaOpened += delegate
            {
                MusicDebug("media opened dur=" + musicPlayer.NaturalDuration);
                musicAutoSkip = 0;
                musicFailureSkipping = false;
                string currentMid = musicCurrentSong == null ? "" : musicCurrentSong.Mid;
                if (musicPendingSeek.HasValue && string.Equals(musicPendingSeekMid, currentMid, StringComparison.Ordinal))
                {
                    try { musicPlayer.Position = musicPendingSeek.Value; } catch (Exception) { }
                }
                musicPendingSeek = null;
                musicPendingSeekMid = "";
                UpdateMusicProgress();
            };
            musicPlayer.MediaEnded += delegate
            {
                // 单曲循环：原地重播；其它模式：按模式选下一首
                if (musicPlayMode == MusicPlayMode.RepeatOne && musicCurrentSong != null)
                {
                    try { musicPlayer.Position = TimeSpan.Zero; musicPlayer.Play(); } catch (Exception) { }
                    SetMusicPlaying(true);
                    return;
                }
                NextMusicSong(1);
            };
            musicPlayer.MediaFailed += delegate(object sender, ExceptionEventArgs e)
            {
                MusicDebug("media failed: " + (e.ErrorException == null ? "?" : e.ErrorException.Message));
                SetMusicPlaying(false);
                if (musicFailureSkipping) return;
                musicFailureSkipping = true;
                string failedMid = musicCurrentSong == null ? "" : musicCurrentSong.Mid;
                MusicDebug("auto skip: media failed");
                // MediaFailed 里直接 Open 下一首容易和 MediaPlayer 的失败状态重入，
                // 放到下一次 Dispatcher 再切歌，保证旧的失败回调先退出。
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!musicFailureSkipping) return;
                    string nowMid = musicCurrentSong == null ? "" : musicCurrentSong.Mid;
                    if (!string.Equals(nowMid, failedMid, StringComparison.Ordinal))
                    {
                        musicFailureSkipping = false;
                        return;
                    }
                    musicFailureSkipping = false;
                    SkipAfterFailure();
                }));
            };

            musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            musicTimer.Tick += delegate { UpdateMusicProgress(); };
            musicTimer.Start();
        }

        // 选中并开始播放一首（先取 vkey 再交给 MediaPlayer）
        void PlayMusicSong(MusicSongInfo song)
        {
            PlayMusicSong(song, false);
        }

        void PlayMusicSong(MusicSongInfo song, bool auto)
        {
            if (song == null) return;
            if (!auto) musicAutoSkip = 0;
            musicFailureSkipping = false;
            SelectMusicSong(song);
            if (!song.Playable)
            {
                // 手动点播到没版权的歌也自动往后找，不要停在这里
                MusicDebug("auto skip: no copyright");
                SkipAfterFailure();
                return;
            }
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            if (platform == null)
            {
                MusicDebug("auto skip: platform missing");
                SkipAfterFailure();
                return;
            }

            SetMusicPlaying(false);
            // 切歌先把旧音源停掉、旧地址清掉：
            // 否则新歌地址还在路上时点歌词，会拿旧地址 seek，听起来就是"又放回上一首"
            musicCurrentUrl = "";
            musicPendingSeek = null;
            if (musicPlayer != null)
            {
                try { musicPlayer.Stop(); } catch (Exception) { }
            }
            musicResolving = true;
            string mid = song.Mid;
            if (platform.Id == "kugou")
            {
                ResolveKugouUrl(platform, mid, auto);
                LoadMusicLyricsAfterPlay(song);
                return;
            }
            if (platform.Id == "netease")
            {
                ResolveNeteaseUrl(mid, auto);
                LoadMusicLyricsAfterPlay(song);
                return;
            }
            if (platform.Id == "qishui")
            {
                ResolveQishuiUrl(mid, auto);
                LoadMusicLyricsAfterPlay(song);
                return;
            }
            MusicImport.ResolvePlayUrl(platform, mid, delegate(string url)
            {
                musicResolving = false;
                MusicDebug("play url len=" + (url == null ? 0 : url.Length) + " head=" + (url == null ? "" : (url.Length > 90 ? url.Substring(0, 90) : url)));
                if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                if (string.IsNullOrEmpty(url))
                {
                    MusicDebug("auto skip: no play url");
                    SkipAfterFailure();
                    return;
                }
                EnsureMusicPlayer();
                musicCurrentUrl = url;
                try
                {
                    MusicDebug("open mid=" + mid + " title=" + song.Title);
                    musicPlayer.Open(new Uri(url));
                    musicPlayer.Play();
                    SetMusicPlaying(true);
                }
                catch (Exception exception)
                {
                    MusicDebug("auto skip: play failed " + exception.Message);
                    SkipAfterFailure();
                }
            }, delegate(string message)
            {
                musicResolving = false;
                MusicDebug("play url failed: " + message);
                if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                MusicDebug("auto skip: play failed " + message);
                SkipAfterFailure();
            });

            LoadMusicLyricsAfterPlay(song);
        }

        // 酷狗：v5/url 取直链再交给 MediaPlayer
        void ResolveKugouUrl(MusicPlatform platform, string mid, bool auto)
        {
            KugouApi.GetSession(platform, delegate(KugouApi.KugouSession session)
            {
                Task.Run(delegate
                {
                    string url = null;
                    string error = null;
                    try { url = KugouApi.GetSongUrl(session, mid); }
                    catch (Exception exception) { error = exception.Message; }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        musicResolving = false;
                        MusicDebug("kugou url len=" + (url == null ? 0 : url.Length) + " err=" + error + " api=" + KugouApi.LastError);
                        if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                        if (string.IsNullOrEmpty(url))
                        {
                            if (auto) SkipAfterFailure();
                            else ShowToolMessage("音乐", error == null ? "这首没有播放地址" : error);
                            return;
                        }
                        EnsureMusicPlayer();
                        musicCurrentUrl = url;
                        try
                        {
                            musicPlayer.Open(new Uri(url));
                            musicPlayer.Play();
                            SetMusicPlaying(true);
                        }
                        catch (Exception exception) { ShowToolMessage("音乐", "播放失败：" + exception.Message); }
                    }));
                });
            }, delegate(string message)
            {
                musicResolving = false;
                if (auto) SkipAfterFailure();
                else ShowToolMessage("音乐", message);
            });
        }

        // 汽水：media/mget 里 player_info 取直链
        void ResolveQishuiUrl(string mid, bool auto)
        {
            Task.Run(delegate
            {
                string url = null;
                string error = null;
                try
                {
                    url = QishuiApi.GetSongUrl(mid);
                    // 汽水给的是 audio/mp4 且没有扩展名，MediaPlayer 靠扩展名认容器，
                    // 所以先落到本地再播（顺便当缓存）
                    if (!string.IsNullOrEmpty(url))
                    {
                        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IslandShotMusic");
                        if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                        string local = System.IO.Path.Combine(folder, "qishui-" + mid + ".m4a");
                        if (!System.IO.File.Exists(local) || new System.IO.FileInfo(local).Length < 1024)
                        {
                            using (var web = new System.Net.WebClient())
                            {
                                web.Headers[System.Net.HttpRequestHeader.UserAgent] = "LunaOpenSDK/1.0.10";
                                web.DownloadFile(url, local);
                            }
                        }
                        url = local;
                    }
                }
                catch (Exception exception) { error = exception.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    musicResolving = false;
                    MusicDebug("qishui url len=" + (url == null ? 0 : url.Length) + " err=" + error + " api=" + QishuiApi.LastError);
                    if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                    if (string.IsNullOrEmpty(url))
                    {
                        if (auto) SkipAfterFailure();
                        else ShowToolMessage("音乐", error == null ? "这首没有播放地址（版权或 VIP）" : error);
                        return;
                    }
                    EnsureMusicPlayer();
                    musicCurrentUrl = url;
                    try
                    {
                        musicPlayer.Open(new Uri(url));
                        musicPlayer.Play();
                        SetMusicPlaying(true);
                    }
                    catch (Exception exception) { ShowToolMessage("音乐", "播放失败：" + exception.Message); }
                }));
            });
        }

        // 网易云：song/enhance/player/url 取直链
        void ResolveNeteaseUrl(string mid, bool auto)
        {
            Task.Run(delegate
            {
                string url = null;
                string error = null;
                try { url = NeteaseApi.GetSongUrl(mid); }
                catch (Exception exception) { error = exception.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    musicResolving = false;
                    MusicDebug("netease url len=" + (url == null ? 0 : url.Length) + " err=" + error + " api=" + NeteaseApi.LastError);
                    if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                    if (string.IsNullOrEmpty(url))
                    {
                        if (auto) SkipAfterFailure();
                        else ShowToolMessage("音乐", error == null ? "这首没有播放地址（版权或 VIP）" : error);
                        return;
                    }
                    EnsureMusicPlayer();
                    musicCurrentUrl = url;
                    try
                    {
                        musicPlayer.Open(new Uri(url));
                        musicPlayer.Play();
                        SetMusicPlaying(true);
                    }
                    catch (Exception exception) { ShowToolMessage("音乐", "播放失败：" + exception.Message); }
                }));
            });
        }

        void ToggleMusicPlayback()
        {
            if (musicResolving) return;
            if (musicCurrentSong == null)
            {
                if (musicSongs.Count > 0) PlayMusicSong(musicSongs[0]);
                return;
            }
            if (string.IsNullOrEmpty(musicCurrentUrl))
            {
                PlayMusicSong(musicCurrentSong);
                return;
            }
            EnsureMusicPlayer();
            if (musicPlaying)
            {
                musicPlayer.Pause();
                SetMusicPlaying(false);
            }
            else
            {
                musicPlayer.Play();
                SetMusicPlaying(true);
            }
        }

        // 自动播放遇到没版权/取不到地址时：继续往后找，最多绕完整一圈
        void SkipAfterFailure()
        {
            if (musicSongs.Count == 0)
            {
                musicAutoSkip = 0;
                ShowToolMessage("音乐", "这个歌单里没有能播放的歌了");
                return;
            }
            if (musicAutoSkip >= musicSongs.Count)
            {
                musicAutoSkip = 0;
                ShowToolMessage("音乐", "这个歌单里没有能播放的歌了");
                return;
            }
            musicAutoSkip++;
            NextMusicSong(1, true);
        }

        static string ShuffleKey(MusicSongInfo song)
        {
            if (song == null) return "";
            return string.IsNullOrEmpty(song.Mid) ? song.Title : song.Mid;
        }

        void NextMusicSong(int step)
        {
            NextMusicSong(step, false);
        }

        void NextMusicSong(int step, bool auto)
        {
            if (musicSongs.Count == 0) return;

            // 随机：洗牌袋。一轮内每首只放一次，全部放完才开新一轮
            if (musicPlayMode == MusicPlayMode.Shuffle && step > 0)
            {
                if (musicCurrentSong != null && musicCurrentSong.Playable)
                {
                    musicShufflePlayed.Add(ShuffleKey(musicCurrentSong));
                }

                var pool = new List<MusicSongInfo>();
                for (int i = 0; i < musicSongs.Count; i++)
                {
                    MusicSongInfo song = musicSongs[i];
                    if (!song.Playable) continue;
                    if (song == musicCurrentSong) continue;
                    if (musicShufflePlayed.Contains(ShuffleKey(song))) continue;
                    pool.Add(song);
                }

                if (pool.Count == 0)
                {
                    // 本轮放完了：开新一轮
                    musicShufflePlayed.Clear();
                    for (int i = 0; i < musicSongs.Count; i++)
                    {
                        MusicSongInfo song = musicSongs[i];
                        if (!song.Playable) continue;
                        if (song == musicCurrentSong) continue;
                        pool.Add(song);
                    }
                }

                if (pool.Count > 0)
                {
                    MusicSongInfo pick = pool[musicRandom.Next(pool.Count)];
                    musicShufflePlayed.Add(ShuffleKey(pick));
                    PlayMusicSong(pick, auto);
                    return;
                }
                // 歌单里能播的太少：落回顺序查找
            }

            int index = musicCurrentSong == null ? -1 : musicSongs.IndexOf(musicCurrentSong);
            if (index < 0) index = 0;
            for (int i = 1; i <= musicSongs.Count; i++)
            {
                int next = ((index + step * i) % musicSongs.Count + musicSongs.Count) % musicSongs.Count;
                if (!musicSongs[next].Playable) continue;
                PlayMusicSong(musicSongs[next], auto);
                return;
            }
            if (auto) ShowToolMessage("音乐", "这个歌单里没有能播放的歌了");
        }

        void SetMusicPlaying(bool playing)
        {
            musicPlaying = playing;
            SetCapsuleMiniPlaying(playing);
            UpdateCapsuleMusicMode();
            if (musicPosterPlayButton != null)
            {
                musicPosterPlayButton.Child = MusicGlyph(playing ? "pause" : "play", Color.FromArgb(240, 255, 255, 255), 48 * 0.40);
            }
            if (musicBrowsePlayButton != null)
            {
                musicBrowsePlayButton.Child = MusicGlyph(playing ? "pause" : "play", Color.FromArgb(240, 255, 255, 255), 34 * 0.44);
            }
        }

        void UpdateMusicProgress()
        {
            if (musicPlayer == null) return;
            TimeSpan position = musicPlayer.Position;
            TimeSpan total = musicPlayer.NaturalDuration.HasTimeSpan ? musicPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
            if (musicBrowseTimeLeft != null) musicBrowseTimeLeft.Text = FormatMusicTime(position);
            if (musicBrowseTimeRight != null) musicBrowseTimeRight.Text = total > TimeSpan.Zero ? FormatMusicTime(total) : "0:00";
            if (musicPosterTimeLeft != null) musicPosterTimeLeft.Text = FormatMusicTime(position);
            if (musicPosterTimeRight != null) musicPosterTimeRight.Text = total > TimeSpan.Zero ? FormatMusicTime(total) : "0:00";

            double ratio = total.TotalMilliseconds > 0 ? position.TotalMilliseconds / total.TotalMilliseconds : 0;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            FrameworkElement browseTrack = musicBrowseProgressFill == null ? null : musicBrowseProgressFill.Parent as FrameworkElement;
            if (browseTrack != null && musicBrowseProgressFill != null) musicBrowseProgressFill.Width = Math.Max(0, browseTrack.ActualWidth * ratio);

            FrameworkElement track = musicPosterProgressFill == null ? null : musicPosterProgressFill.Parent as FrameworkElement;
            if (track == null || musicPosterProgressFill == null) return;
            double width = Math.Max(0, track.ActualWidth * ratio);
            musicPosterProgressFill.Width = width;
            if (musicPosterProgressKnob != null)
            {
                musicPosterProgressKnob.RenderTransform = new TranslateTransform(Math.Max(0, width - 4.5), 0);
            }
        }

        static string FormatMusicTime(TimeSpan time)
        {
            if (time.TotalSeconds < 0) time = TimeSpan.Zero;
            return ((int)time.TotalMinutes) + ":" + time.Seconds.ToString("00");
        }

        // 歌词在播放后加载（顺便避免切歌太快时白等）
        void LoadMusicLyricsAfterPlay(MusicSongInfo song)
        {
            LoadMusicLyrics(song);
        }

        // ================================================================ 歌单导入

        void StartMusicImport()
        {
            StartMusicImport(false);
        }

        void StartMusicImport(bool force)
        {
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            if (platform == null)
            {
                SetMusicListStatus("未登录");
                RebuildMusicPlaceholders();
                return;
            }
            // QQ + 酷狗已接歌单导入；网易云/汽水还没接
            // QQ / 酷狗 / 网易云已接；汽水没接
            if (platform.Id != "qq" && platform.Id != "kugou" && platform.Id != "netease" && platform.Id != "qishui")
            {
                SetMusicListStatus("未接导入");
                RebuildMusicPlaceholders();
                return;
            }
            if (musicImporting) return;
            musicImporting = true;

            // 有上次的歌单列表：先显示，再后台刷新（避免打开面板每次都空等）
            List<MusicPlaylistInfo> cachedLists = force ? null : MusicCache.LoadPlaylists(platform.Id);
            if (cachedLists != null)
            {
                musicPlaylists = cachedLists;
                SetMusicListStatus(cachedLists.Count + " 个歌单 · 刷新中");
                RenderMusicPlaylists();
                if (musicSongs.Count == 0) RebuildMusicSongPlaceholders();
            }
            else
            {
                SetMusicListStatus("读取中…");
            }

            MusicImport.LoadPlaylists(platform, delegate(List<MusicPlaylistInfo> list)
            {
                musicImporting = false;
                MusicCache.SavePlaylists(platform.Id, list);
                musicPlaylists = list;
                SetMusicListStatus(list.Count + " 个歌单");
                RenderMusicPlaylists();
                if (musicSongs.Count == 0) RebuildMusicSongPlaceholders();
                // 手动刷新：顺便把当前歌单的歌曲也实时重拉一遍
                if (force && !string.IsNullOrEmpty(musicSelectedPlaylistId))
                {
                    foreach (MusicPlaylistInfo item in list)
                    {
                        if (item != null && string.Equals(item.Id, musicSelectedPlaylistId, StringComparison.Ordinal))
                        {
                            SelectMusicPlaylist(item, true);
                            break;
                        }
                    }
                }
            }, delegate(string message)
            {
                musicImporting = false;
                if (cachedLists != null)
                {
                    SetMusicListStatus(cachedLists.Count + " 个歌单（离线）");
                    return;
                }
                SetMusicListStatus("读取失败");
                RebuildMusicPlaceholders();
                ShowToolMessage("音乐", "歌单读取失败：" + message);
            });
        }

        // 手动刷新：清掉缓存读取路径，直接实时拉一次（歌单 + 当前歌单的歌）
        void RefreshMusicNow()
        {
            if (musicImporting) return;
            MusicDebug("manual refresh");
            StartMusicImport(true);
        }

        void SetMusicListStatus(string text)
        {
            if (musicListStatus != null) musicListStatus.Text = text;
        }

        void RenderMusicPlaylists()
        {
            if (musicPlaylistList == null) return;
            musicPlaylistList.Children.Clear();
            musicPlaylistBgs.Clear();
            musicPlaylistCounts.Clear();
            for (int i = 0; i < musicPlaylists.Count; i++)
            {
                MusicPlaylistInfo info = musicPlaylists[i];
                FrameworkElement row = BuildMusicPlaylistItem(info);
                musicPlaylistList.Children.Add(row);
            }
            // 只有第一次把占位换成真数据时才做入场动画；之后重建一律不动画，
            // 否则用户正在滚动时行会被重置成透明再淡入（看起来就是"闪一下"）
            if (!musicRowsIntroduced)
            {
                musicRowsIntroduced = true;
                AnimateMusicRows();
            }
        }

        FrameworkElement BuildMusicPlaylistItem(MusicPlaylistInfo info)
        {
            var grid = new Grid { Height = 40, Margin = new Thickness(0, 0, 2, 4), Cursor = Cursors.Hand };
            var bg = new Border { CornerRadius = new CornerRadius(9), Background = Brushes.Transparent };
            grid.Children.Add(bg);

            var thumb = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromArgb(90, MusicAccentFrom.R, MusicAccentFrom.G, MusicAccentFrom.B),
                    Color.FromArgb(45, MusicAccentTo.R, MusicAccentTo.G, MusicAccentTo.B),
                    new Point(0, 0), new Point(1, 1)),
                Child = MusicGlyph("note", Color.FromArgb(170, 255, 255, 255), 13)
            };
            grid.Children.Add(thumb);

            var name = new TextBlock
            {
                Text = info.Name,
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(MusicTextColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(42, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            grid.Children.Add(name);

            if (info.Count > 0)
            {
                var countText = new TextBlock
                {
                    Text = info.Count.ToString(),
                    FontFamily = AiFont,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 156, 172)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                grid.Children.Add(countText);
                musicPlaylistCounts[info.Id] = countText;
            }
            musicPlaylistBgs[info.Id] = bg;

            grid.MouseEnter += delegate { bg.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            grid.MouseLeave += delegate
            {
                bg.Background = string.Equals(musicSelectedPlaylistId, info.Id, StringComparison.Ordinal)
                    ? new SolidColorBrush(Color.FromArgb(18, 52, 211, 153))
                    : Brushes.Transparent;
            };
            grid.MouseLeftButtonUp += delegate { SelectMusicPlaylist(info); };
            if (string.Equals(musicSelectedPlaylistId, info.Id, StringComparison.Ordinal))
            {
                bg.Background = new SolidColorBrush(Color.FromArgb(18, 52, 211, 153));
            }
            return grid;
        }

        void SelectMusicPlaylist(MusicPlaylistInfo info)
        {
            SelectMusicPlaylist(info, false);
        }

        void SelectMusicPlaylist(MusicPlaylistInfo info, bool force)
        {
            if (info == null) return;
            musicSelectedPlaylistId = info.Id;
            // 只换高亮，不重建左栏——重建会让正在滚动的歌曲列表闪一下
            UpdateMusicPlaylistHighlight();
            // 选歌单 = 浏览模式：海报和播放器区都收起，歌曲列表铺满右区
            if (musicPlayerStage != null) musicPlayerStage.Visibility = Visibility.Collapsed;
            if (musicPosterStage != null) musicPosterStage.Visibility = Visibility.Collapsed;
            if (musicBrowseBar != null) musicBrowseBar.Visibility = Visibility.Visible;
            CloseMusicQueueSheet();

            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            string platformId = platform == null ? "" : platform.Id;
            List<MusicSongInfo> cached = (platform == null || force) ? null : MusicCache.LoadSongs(platformId, info.Id);

            if (cached != null)
            {
                // 有缓存：立刻显示，不等网络
                musicSongs = cached;
            musicShufflePlayed.Clear();
                SetMusicListStatus(info.Name + " · " + cached.Count + " 首");
                RenderMusicSongs();
                musicNowTitle.Text = info.Name;
                musicNowArtist.Text = cached.Count + " 首歌 · 本地缓存";
                MusicDebug("songs from cache: " + info.Name + " " + cached.Count);
            }
            else
            {
                SetMusicListStatus("读取歌曲…");
                musicNowTitle.Text = "正在读取歌单";
                musicNowArtist.Text = info.Name;
                MusicDebug("songs fetch (cold): " + info.Name);
            }

            // 后台刷新：有缓存时静默更新，只在内容真的变了才重绘
            MusicImport.LoadSongs(platform, info, delegate(List<MusicSongInfo> songs)
            {
                MusicCache.SaveSongs(platformId, info.Id, songs);
                if (!string.Equals(musicSelectedPlaylistId, info.Id, StringComparison.Ordinal)) return;
                bool changed = cached == null || !SameMusicSongs(cached, songs);
                if (info.Count != songs.Count)
                {
                    info.Count = songs.Count;
                    UpdateMusicPlaylistCount(info);
                }
                SetMusicListStatus(info.Name + " · " + songs.Count + " 首");
                if (changed)
                {
                    musicSongs = songs;
                musicShufflePlayed.Clear();
                    RenderMusicSongs();
                }
                musicNowTitle.Text = info.Name;
                musicNowArtist.Text = songs.Count + " 首歌 · 点一首开始（播放下一步接）";
            }, delegate(string message)
            {
                if (!string.Equals(musicSelectedPlaylistId, info.Id, StringComparison.Ordinal)) return;
                // 有缓存就静默失败，不要把已经显示出来的列表清掉
                if (cached != null)
                {
                    MusicDebug("songs refresh failed (keep cache): " + message);
                    SetMusicListStatus(info.Name + " · " + cached.Count + " 首（离线）");
                    return;
                }
                SetMusicListStatus("读取失败");
                RebuildMusicSongPlaceholders();
                ShowToolMessage("音乐", "歌曲读取失败：" + message);
            });
        }

        // 两次歌曲列表是否一致（比较数量和 mid），用来决定要不要重绘
        static bool SameMusicSongs(List<MusicSongInfo> a, List<MusicSongInfo> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                string midA = a[i] == null ? "" : a[i].Mid;
                string midB = b[i] == null ? "" : b[i].Mid;
                if (!string.Equals(midA, midB, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // 点一首歌：进入海报模式（满屏封面，不显示歌名文字），队列可从右下角按钮打开
        void SelectMusicSong(MusicSongInfo song)
        {
            if (song == null) return;
            musicCurrentSong = song;

            if (musicBrowseTitle != null) musicBrowseTitle.Text = song.Title + (string.IsNullOrEmpty(song.Artists) ? "" : "  ·  " + song.Artists);
            if (musicPlayerStage != null) musicPlayerStage.Visibility = Visibility.Collapsed;
            if (musicPosterStage != null) musicPosterStage.Visibility = Visibility.Visible;
            if (musicBrowseBar != null) musicBrowseBar.Visibility = Visibility.Collapsed;
            SetCoverBrush(musicPosterHost, song.AlbumMid);
            SetCoverBrush(capsuleMiniDisc, song.AlbumMid);
            SetMusicCover(musicPosterBlur, song.AlbumMid);
            SetCoverBrush(musicQueueCoverHost, song.AlbumMid);
            LoadMusicLyrics(song);

            // 海报模式下不显示文字信息，只留封面
            if (musicNowTitle != null) musicNowTitle.Text = song.Title;
            if (musicNowArtist != null) musicNowArtist.Text = string.IsNullOrEmpty(song.Artists) ? "未知歌手" : song.Artists;
            if (musicStateTag != null) musicStateTag.Text = song.Playable ? "已选中 · 播放下一步接" : "无版权 · 播不了";
        }

        // 拉歌词：拿到带时间轴的行，逐行渲染
        void LoadMusicLyrics(MusicSongInfo song)
        {
            musicLyricLines.Clear();
            SyncMusicStageLyrics(null);
            if (musicLyricsPanel == null) return;
            if (song == null || !song.Playable)
            {
                ShowLyricPlaceholder("暂无歌词");
                return;
            }
            ShowLyricPlaceholder("正在加载歌词…");
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            if (platform == null)
            {
                ShowLyricPlaceholder("暂无歌词");
                return;
            }
            string mid = song.Mid;
            MusicImport.LoadLyrics(platform, mid, delegate(List<MusicLyricLine> lines)
            {
                if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                musicLyricLines = lines;
                SyncMusicStageLyrics(lines);
                RenderMusicLyrics();
                }, delegate(string message)
            {
                if (musicCurrentSong == null || !string.Equals(musicCurrentSong.Mid, mid, StringComparison.Ordinal)) return;
                ShowLyricPlaceholder("暂无歌词");
            });
        }

        // 投射窗用自己的一份轻量歌词表；面板的 MusicLyricLine 只在加载时转换一次。
        void SyncMusicStageLyrics(List<MusicLyricLine> lines)
        {
            musicStageLyrics.Clear();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] == null) continue;
                    musicStageLyrics.Add(new MusicStageLyric
                    {
                        Time = lines[i].Time,
                        Text = lines[i].Text == null ? "" : lines[i].Text
                    });
                }
            }
            musicStageLyricsVersion++;
        }

        void ShowLyricPlaceholder(string text)
        {
            if (musicLyricsPanel == null) return;
            musicLyricsPanel.Children.Clear();
            musicLyricsPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = AiFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 200, 208, 220)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }

        void RenderMusicLyrics()
        {
            if (musicLyricsPanel == null) return;
            musicLyricsPanel.Children.Clear();
            for (int i = 0; i < musicLyricLines.Count; i++)
            {
                musicLyricsPanel.Children.Add(BuildMusicLyricLine(i, musicLyricLines[i]));
            }
        }

        // 一行歌词：悬停变亮变大 + 右侧出现"▶ 01:56"，点了从那句开始播
        FrameworkElement BuildMusicLyricLine(int index, MusicLyricLine line)
        {
            var grid = new Grid { MinHeight = 36, Margin = new Thickness(0, 1, 0, 1) };
            var bg = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            grid.Children.Add(bg);

            var text = new TextBlock
            {
                Text = line.Text,
                FontFamily = AiFont,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 226, 232, 242)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 4, 70, 4)
            };
            grid.Children.Add(text);

            var chipContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chipContent.Children.Add(MusicGlyph("play", Color.FromArgb(235, 255, 255, 255), 9));
            chipContent.Children.Add(new TextBlock
            {
                Text = FormatMusicTime(line.Time),
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            });
            var chip = new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(9, 0, 9, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Opacity = 0,
                IsHitTestVisible = false,
                ToolTip = "从这里开始播放",
                Child = chipContent
            };
            grid.Children.Add(chip);
            chip.MouseLeftButtonUp += delegate { SeekMusicToLine(line); };

            grid.MouseEnter += delegate
            {
                text.FontSize = 16;
                text.Foreground = new SolidColorBrush(Colors.White);
                bg.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                chip.Opacity = 1;
                chip.IsHitTestVisible = true;
            };
            grid.MouseLeave += delegate
            {
                text.FontSize = 14;
                text.Foreground = new SolidColorBrush(Color.FromArgb(190, 226, 232, 242));
                bg.Background = Brushes.Transparent;
                chip.Opacity = 0;
                chip.IsHitTestVisible = false;
            };
            return grid;
        }

        // 从这句歌词开始播（还没加载音源就先播当前歌，播到后再跳）
        void SeekMusicToLine(MusicLyricLine line)
        {
            if (line == null || musicCurrentSong == null) return;
            if (musicResolving)
            {
                // 新歌地址还在路上：记下要跳的位置，等它加载完再应用
                musicPendingSeek = line.Time;
                musicPendingSeekMid = musicCurrentSong.Mid;
                return;
            }
            if (string.IsNullOrEmpty(musicCurrentUrl))
            {
                musicPendingSeek = line.Time;
                musicPendingSeekMid = musicCurrentSong.Mid;
                PlayMusicSong(musicCurrentSong);
                return;
            }
            EnsureMusicPlayer();
            try
            {
                musicPlayer.Position = line.Time;
                if (!musicPlaying)
                {
                    musicPlayer.Play();
                    SetMusicPlaying(true);
                }
            }
            catch (Exception) { }
        }

        // 圆角封面：Border 的 Background 用 ImageBrush，圆角一定生效
        static void SetCoverBrush(Border host, string albumMid)
        {
            if (host == null) return;
            Color fallback = Color.FromArgb(26, 255, 255, 255);
            if (string.IsNullOrEmpty(albumMid))
            {
                host.Background = new SolidColorBrush(fallback);
                return;
            }
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = CoverUri(albumMid);
                bitmap.EndInit();
                bitmap.DownloadFailed += delegate
                {
                    try { host.Background = new SolidColorBrush(fallback); } catch (Exception) { }
                };
                host.Background = new ImageBrush(bitmap) { Stretch = Stretch.Uniform };
            }
            catch (Exception)
            {
                host.Background = new SolidColorBrush(fallback);
            }
        }

        // 专辑封面：QQ 音乐的封面地址是固定格式。加载失败就把图清掉，露出底下的唱片占位。
        // 酷狗给的是完整 URL，QQ 给的是 albumMid，要拼前缀
        static Uri CoverUri(string albumMid)
        {
            if (albumMid != null && albumMid.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return new Uri(albumMid);
            if (albumMid != null && albumMid.StartsWith("//", StringComparison.Ordinal)) return new Uri("https:" + albumMid);
            return new Uri("https://y.gtimg.cn/music/photo_new/T002R300x300M000" + albumMid + ".jpg");
        }

        static void SetMusicCover(Image target, string albumMid)
        {
            if (target == null) return;
            if (string.IsNullOrEmpty(albumMid))
            {
                target.Source = null;
                return;
            }
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = CoverUri(albumMid);
                bitmap.EndInit();
                bitmap.DownloadFailed += delegate
                {
                    try { target.Source = null; } catch (Exception) { }
                };
                target.Source = bitmap;
            }
            catch (Exception)
            {
                target.Source = null;
            }
        }

        static ImageSource LoadMusicCover(string albumMid)
        {
            if (string.IsNullOrEmpty(albumMid)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = CoverUri(albumMid);
                bitmap.EndInit();
                return bitmap;
            }
            catch (Exception) { return null; }
        }

        void UpdateMusicPlaylistHighlight()
        {
            foreach (KeyValuePair<string, Border> pair in musicPlaylistBgs)
            {
                bool selected = string.Equals(pair.Key, musicSelectedPlaylistId, StringComparison.Ordinal);
                pair.Value.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(18, 52, 211, 153))
                    : Brushes.Transparent;
            }
        }

        void UpdateMusicPlaylistCount(MusicPlaylistInfo info)
        {
            TextBlock text;
            if (info == null || !musicPlaylistCounts.TryGetValue(info.Id, out text) || text == null) return;
            text.Text = info.Count.ToString();
        }

        void RenderMusicSongs()
        {
            if (musicSongList == null) return;
            musicSongList.Children.Clear();
            for (int i = 0; i < musicSongs.Count; i++)
            {
                musicSongList.Children.Add(BuildMusicSongItem(i + 1, musicSongs[i]));
            }
        }

        FrameworkElement BuildMusicSongItem(int index, MusicSongInfo song)
        {
            var grid = new Grid { Height = 34, Margin = new Thickness(0, 0, 4, 2), Cursor = Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var bg = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            Grid.SetColumnSpan(bg, 3);
            grid.Children.Add(bg);

            var number = new TextBlock
            {
                Text = index.ToString(),
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(110, 148, 156, 172)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(number, 0);
            grid.Children.Add(number);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = song.Title,
                FontFamily = AiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MusicTextColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(song.Artists) ? " " : song.Artists,
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(140, 148, 156, 172)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            var duration = new TextBlock
            {
                Text = song.Duration,
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 156, 172)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(duration, 2);
            grid.Children.Add(duration);

            grid.MouseEnter += delegate { bg.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            grid.MouseLeave += delegate { bg.Background = Brushes.Transparent; };
            grid.MouseLeftButtonUp += delegate { PlayMusicSong(song); };
            // 无版权/下架的曲子：变暗，且不接受点击（点也播不了）
            if (!song.Playable)
            {
                grid.Opacity = 0.45;
                grid.Cursor = Cursors.Arrow;
                grid.ToolTip = "这首没有版权 / 已下架，播放不了";
            }
            return grid;
        }

        void RebuildMusicSongPlaceholders()
        {
            if (musicSongList == null) return;
            musicSongList.Children.Clear();
            double[] titles = new double[] { 210, 168, 236, 184, 152 };
            double[] artists = new double[] { 88, 72, 96, 64, 80 };
            for (int i = 0; i < titles.Length; i++)
            {
                musicSongList.Children.Add(MusicSongRow(i + 1, titles[i], artists[i]));
            }
        }

        // 打开面板时列表逐行淡入 + 轻微上移，比整块"啪"一下出现舒服
        void AnimateMusicRows()
        {
            var rows = new List<FrameworkElement>();
            if (musicPlaylistList != null)
            {
                foreach (object child in musicPlaylistList.Children)
                {
                    FrameworkElement element = child as FrameworkElement;
                    if (element != null) rows.Add(element);
                }
            }
            if (musicSongList != null)
            {
                foreach (object child in musicSongList.Children)
                {
                    FrameworkElement element = child as FrameworkElement;
                    if (element != null) rows.Add(element);
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                FrameworkElement row = rows[i];
                if (row.RenderTransform as TranslateTransform == null)
                {
                    row.RenderTransform = new TranslateTransform(0, 6);
                }
                TranslateTransform move = (TranslateTransform)row.RenderTransform;
                row.Opacity = 0;
                int delay = 40 + i * 22;
                Motion.AnimateDouble(row, UIElement.OpacityProperty, 0, 1, 220, Motion.Enter, delay, null);
                Motion.AnimateDouble(move, TranslateTransform.YProperty, 6, 0, 260, Motion.Enter, delay, null);
            }
        }

        // 队列面板（图三那种）：上面海报，下面播放队列
        // 队列面板：底部抽屉（带抓手 + 播放模式 + 关闭），比原来的右侧竖条更像这个软件的东西
        FrameworkElement BuildMusicQueueSheet()
        {
            var grid = new Grid { Visibility = Visibility.Collapsed };
            Panel.SetZIndex(grid, 40);

            var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(130, 6, 8, 12)) };
            scrim.MouseLeftButtonUp += delegate { CloseMusicQueueSheet(); };
            grid.Children.Add(scrim);

            var panel = new Border
            {
                Width = 540,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 16),
                CornerRadius = new CornerRadius(18),
                Background = new LinearGradientBrush(
                    Color.FromArgb(250, 36, 42, 50), Color.FromArgb(250, 25, 30, 38),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 10),
                Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Opacity = 0.5 }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 抓手
            layout.Children.Add(new Border
            {
                Width = 36,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8),
                Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
            });

            var head = new Grid { Height = 28, Margin = new Thickness(2, 0, 2, 6) };
            musicQueueTitle = new TextBlock
            {
                Text = "播放队列",
                FontFamily = AiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            head.Children.Add(musicQueueTitle);

            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            musicQueueModeButton = BuildMusicPlayModeChip();
            musicQueueModeButton.Margin = new Thickness(0, 0, 8, 0);
            rightButtons.Children.Add(musicQueueModeButton);
            var close = AiGhostButton("×");
            close.Click += delegate { CloseMusicQueueSheet(); };
            rightButtons.Children.Add(close);
            head.Children.Add(rightButtons);
            Grid.SetRow(head, 1);
            layout.Children.Add(head);

            musicQueueList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = musicQueueList,
                Height = 196,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent
            };
            Grid.SetRow(scroll, 2);
            layout.Children.Add(scroll);

            panel.Child = layout;
            grid.Children.Add(panel);
            musicQueueSheet = grid;
            return grid;
        }

        // 播放模式小胶囊：随机 → 单曲循环 → 顺序 → 随机
        Border BuildMusicPlayModeChip()
        {
            var chip = new Border
            {
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 0, 9, 0),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            chip.MouseEnter += delegate { chip.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); };
            chip.MouseLeave += delegate { chip.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); };
            chip.MouseLeftButtonUp += delegate { CycleMusicPlayMode(); };
            return chip;
        }

        void CycleMusicPlayMode()
        {
            if (musicPlayMode == MusicPlayMode.Shuffle) musicPlayMode = MusicPlayMode.RepeatOne;
            else if (musicPlayMode == MusicPlayMode.RepeatOne) musicPlayMode = MusicPlayMode.Sequential;
            else musicPlayMode = MusicPlayMode.Shuffle;
            UpdateMusicPlayModeVisual();
            ShowToolMessage("音乐", "播放模式：" + MusicPlayModeLabel());
        }

        string MusicPlayModeLabel()
        {
            if (musicPlayMode == MusicPlayMode.RepeatOne) return "单曲循环";
            if (musicPlayMode == MusicPlayMode.Sequential) return "顺序播放";
            return "随机播放";
        }

        void UpdateMusicPlayModeVisual()
        {
            if (musicQueueModeButton == null) return;
            string glyph = "shuffle";
            string label = "随机";
            if (musicPlayMode == MusicPlayMode.RepeatOne) { glyph = "repeat"; label = "单曲"; }
            else if (musicPlayMode == MusicPlayMode.Sequential) { glyph = "sequential"; label = "顺序"; }

            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(MusicGlyph(glyph, Color.FromArgb(230, 228, 234, 244), 11));
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(225, 228, 234, 244)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            });
            musicQueueModeButton.Child = content;
            musicQueueModeButton.ToolTip = "播放模式：" + MusicPlayModeLabel() + "（点击切换）";
        }

        void OpenMusicQueueSheet()
        {
            if (musicQueueSheet == null) return;
            RenderMusicQueue();
            musicQueueSheet.Visibility = Visibility.Visible;
            musicQueueSheet.Opacity = 0;
            Motion.Fade(musicQueueSheet, 1, 140, Motion.Enter, 0, null);
        }

        void CloseMusicQueueSheet()
        {
            CloseMusicQueueSheetCore();
        }

        void CloseMusicQueueSheetCore()
        {
            if (musicQueueSheet == null) return;
            musicQueueSheet.Visibility = Visibility.Collapsed;
        }

        void RenderMusicQueue()
        {
            UpdateMusicPlayModeVisual();
            if (musicQueueList == null) return;
            musicQueueList.Children.Clear();
            musicQueueTitle.Text = "播放队列 (" + musicSongs.Count + ")";
            for (int i = 0; i < musicSongs.Count; i++)
            {
                musicQueueList.Children.Add(BuildMusicQueueRow(i + 1, musicSongs[i]));
            }
        }

        FrameworkElement BuildMusicQueueRow(int index, MusicSongInfo song)
        {
            var row = new Grid { Height = 38, Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand };
            bool current = musicCurrentSong != null && string.Equals(musicCurrentSong.Mid, song.Mid, StringComparison.Ordinal);
            var bg = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = current ? new SolidColorBrush(Color.FromArgb(26, 52, 211, 153)) : Brushes.Transparent,
                Margin = new Thickness(-4, 0, -4, 0)
            };
            row.Children.Add(bg);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            text.Children.Add(new TextBlock
            {
                Text = (index < 10 ? " " : "") + index + "  " + song.Title,
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(current ? Color.FromRgb(126, 226, 194) : MusicTextColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(song.Artists) ? " " : song.Artists,
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(130, 148, 156, 172)),
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(text);

            row.MouseEnter += delegate { if (!current) bg.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            row.MouseLeave += delegate { if (!current) bg.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += delegate { PlayMusicSong(song); RenderMusicQueue(); };
            return row;
        }

        // 左栏底部：设置入口（齿轮 + 文字），点开是一张本机设置卡
        FrameworkElement BuildMusicFooter()
        {
            var row = new Grid { Height = 28, Margin = new Thickness(0, 6, 0, 0), Cursor = Cursors.Hand };
            var bg = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Margin = new Thickness(-6, 0, -4, 0) };
            row.Children.Add(bg);

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            content.Children.Add(MusicGlyph("gear", Color.FromArgb(140, 148, 156, 172), 13));
            content.Children.Add(new TextBlock
            {
                Text = "设置",
                FontFamily = AiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 148, 156, 172)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0)
            });
            row.Children.Add(content);

            var refreshContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            refreshContent.Children.Add(MusicGlyph("refresh", Color.FromArgb(200, 150, 158, 172), 11));
            refreshContent.Children.Add(new TextBlock
            {
                Text = "刷新",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 150, 158, 172)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            });
            var refresh = new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "实时重新拉取歌单",
                Child = refreshContent
            };
            refresh.MouseEnter += delegate { refresh.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)); };
            refresh.MouseLeave += delegate { refresh.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); };
            refresh.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; RefreshMusicNow(); };
            row.Children.Add(refresh);

            row.MouseEnter += delegate { bg.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)); };
            row.MouseLeave += delegate { bg.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += delegate { OpenMusicSettings(); };
            return row;
        }

        Border BuildMusicSettingsSheet()
        {
            var card = new Border
            {
                Width = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(34, 39, 47)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 16, 18, 14),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 10, Opacity = 0.5 }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "音乐设置",
                FontFamily = AiFont,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MusicTextColor)
            });

            musicSettingsAccount = new TextBlock
            {
                FontFamily = AiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(MusicMutedColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 14)
            };
            stack.Children.Add(musicSettingsAccount);

            // 免责提示：接口为独立重写，只用于播放自己账号的内容（详见 THIRD-PARTY-NOTICES.txt）
            var disclaimer = new TextBlock
            {
                Text = "接口为独立重写，仅用于播放自己账号的内容；不绕过付费/加密，与各平台无关联。",
                FontFamily = AiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(120, MusicMutedColor.R, MusicMutedColor.G, MusicMutedColor.B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Insert(stack.Children.IndexOf(musicSettingsAccount) + 1, disclaimer);

            var actions = new StackPanel();
            var relogin = ToolButton("换平台 / 重新登录", 12);
            relogin.HorizontalAlignment = HorizontalAlignment.Stretch;
            relogin.Margin = new Thickness(0, 0, 0, 8);
            relogin.Click += delegate
            {
                CloseMusicSettings();
                ShowMusicLoginPicker();
            };
            actions.Children.Add(relogin);

            var folder = ToolButton("打开数据目录", 12);
            folder.HorizontalAlignment = HorizontalAlignment.Stretch;
            folder.Margin = new Thickness(0, 0, 0, 8);
            folder.Click += delegate { OpenMusicDataFolder(); };
            actions.Children.Add(folder);

            var logout = ToolButton("退出登录（下次重新扫码）", 12);
            logout.HorizontalAlignment = HorizontalAlignment.Stretch;
            logout.Margin = new Thickness(0, 0, 0, 8);
            logout.Click += delegate { MusicLogout(); };
            actions.Children.Add(logout);
            stack.Children.Add(actions);

            var foot = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            var hint = new TextBlock
            {
                Text = "歌单和播放还在接，登录态存在本机 data\\music 下。",
                FontFamily = AiFont,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 156, 172)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 90, 0)
            };
            foot.Children.Add(hint);
            var close = ToolButton("关闭", 12);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += delegate { CloseMusicSettings(); };
            foot.Children.Add(close);
            stack.Children.Add(foot);

            card.Child = stack;

            var sheet = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 12, 14, 18)),
                CornerRadius = new CornerRadius(0, 0, 29, 29),
                Visibility = Visibility.Collapsed,
                Child = card
            };
            // 点遮罩关掉（点卡片本身不关）
            sheet.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == sheet) CloseMusicSettings();
            };
            return sheet;
        }

        void OpenMusicSettings()
        {
            if (musicSettingsSheet == null) return;
            ApplyMusicSettingsText();
            musicSettingsSheet.Visibility = Visibility.Visible;
            musicSettingsSheet.Opacity = 0;
            Motion.Fade(musicSettingsSheet, 1, 140, Motion.Enter, 0, null);
        }

        void CloseMusicSettings()
        {
            if (musicSettingsSheet == null) return;
            musicSettingsSheet.Visibility = Visibility.Collapsed;
        }

        void ApplyMusicSettingsText()
        {
            if (musicSettingsAccount == null) return;
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            musicSettingsAccount.Text = platform == null
                ? "当前未登录。选一个平台登录后，这里会显示账号信息。"
                : "当前账号：" + platform.Name + "（已登录）";
        }

        void OpenMusicDataFolder()
        {
            try
            {
                string folder = System.IO.Path.Combine(AiConfigStore.DataDir, "music");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ShowToolMessage("音乐", "打开目录失败：" + exception.Message);
            }
        }

        void MusicLogout()
        {
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            CloseMusicSettings();
            StopMusicQr();
            CloseMusicLoginWindow();
            musicPlatformId = "";
            MusicSession.Clear();
            KugouApi.ResetSession();
            // 顺带把这个平台的网页登录态（WebView2 配置目录）也删掉，
            // 不然下次选同一个平台会直接被判"已登录"，看不到二维码
            if (platform != null) ClearMusicWebProfile(platform.Id);
            ApplyMusicLoginVisual();
            RebuildMusicPlaceholders();
            ShowToolMessage("音乐", "已退出登录。下次登录这个平台会重新扫码。");
        }

        // 删除某个平台的 WebView2 配置目录（cookie 都在里面）。被占用时静默失败。
        static void ClearMusicWebProfile(string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) return;
            try
            {
                string folder = System.IO.Path.Combine(AiConfigStore.DataDir, "music", "webview2-" + platformId);
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
            catch (Exception) { }
        }

        void ApplyMusicLoginVisual()
        {
            MusicPlatform platform = MusicPlatforms.Find(musicPlatformId);
            bool logged = platform != null;

            if (musicAccountChip != null)
            {
                musicChipText.Text = logged ? platform.Name : "未登录";
                musicChipDot.Fill = new SolidColorBrush(
                    logged ? platform.ColorFrom : Color.FromRgb(120, 128, 145));
            }

            if (musicAccountName != null)
            {
                musicAccountName.Text = logged ? platform.Name : "未登录";
                musicAccountSub.Text = logged ? "已登录 · 换平台" : "点这里登录";
                musicAccountBadge.Text = logged ? platform.Badge : "?";
                musicAccountAvatar.Background = logged
                    ? (Brush)new LinearGradientBrush(platform.ColorFrom, platform.ColorTo, new Point(0, 0), new Point(1, 1))
                    : new SolidColorBrush(Color.FromRgb(48, 55, 66));
            }

            if (musicNowArtist != null)
            {
                musicNowArtist.Text = logged ? "从左边选一个歌单" : "登录后从左边挑一个歌单";
            }
            if (musicStateTag != null)
            {
                musicStateTag.Text = logged ? "已登录 · 歌单已接入" : "未登录";
            }
            if (musicStateHint != null)
            {
                musicStateHint.Text = logged
                    ? "点左边歌单读歌曲；播放下一步接，控制键现在按不动。"
                    : "选一个平台登录。登录窗口里用官方网页扫码 / 输账号，我们只接收登录态。";
            }
            if (musicLoginOverlay != null)
            {
                musicLoginOverlay.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
            }
            if (musicLoginTitle != null)
            {
                musicLoginTitle.Text = logged ? "换一个音乐账号" : "先登录一个音乐账号";
            }
            if (musicLoginCancel != null)
            {
                musicLoginCancel.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;
            }
            ApplyMusicSettingsText();
        }

        void ShowMusicLoginPicker()
        {
            if (musicLoginOverlay == null) return;
            // 每次打开都从平台列表开始，别停在上次的扫码页上
            StopMusicQr();
            musicQrPlatform = null;
            if (musicLoginCard != null) musicLoginCard.Width = 640;
            if (musicQrStage != null) musicQrStage.Visibility = Visibility.Collapsed;
            if (musicPlatformStage != null) musicPlatformStage.Visibility = Visibility.Visible;

            musicLoginOverlay.Visibility = Visibility.Visible;
            musicLoginOverlay.Opacity = 0;
            Motion.Fade(musicLoginOverlay, 1, 140, Motion.Enter, 0, null);
        }

        void HideMusicLoginPicker()
        {
            StopMusicQr();
            if (musicLoginOverlay == null) return;
            musicLoginOverlay.Visibility = Visibility.Collapsed;
        }

        // ================================================================ 登录

        void OpenMusicLogin(MusicPlatform platform)
        {
            if (platform == null) return;
            CloseMusicLoginWindow();

            MusicLoginWindow window = new MusicLoginWindow(platform);
            musicLoginWindow = window;
            window.LoggedIn += delegate(MusicPlatform done)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    musicPlatformId = done.Id;
                    MusicSession.Save(done.Id);
                    ApplyMusicLoginVisual();
                    StartMusicImport();
                    ShowToolMessage("音乐", "已登录 " + done.Name + "。正在读取歌单。");
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            window.Dismissed += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    musicLoginWindow = null;
                }), System.Windows.Threading.DispatcherPriority.Background);
            };

            // 贴着面板正上方往下排，用户视线不用来回找
            Rect host = ScreenRectOf(musicPanel);
            if (host.IsEmpty)
            {
                Rect work = SystemParameters.WorkArea;
                window.Left = work.Left + (work.Width - window.Width) / 2;
                window.Top = work.Top + 40;
            }
            else
            {
                window.Left = host.Left + (host.Width - window.Width) / 2;
                window.Top = Math.Max(SystemParameters.WorkArea.Top + 8, host.Top + 4);
            }
            window.Show();
            window.Activate();
        }

        void CloseMusicLoginWindow()
        {
            MusicLoginWindow window = musicLoginWindow;
            musicLoginWindow = null;
            if (window == null) return;
            try { window.Close(); }
            catch (Exception) { }
        }

        // ================================================================ 显隐

        void OpenMusicPanel()
        {
            if (musicPanel == null) return;
            if (musicVisible && expanded && musicPanel.Visibility == Visibility.Visible) return;

            bool fromImagePanel = expanded;
            musicVisible = true;
            musicLastSurface = true;
            aiLastSurface = false;

            EnsureMusicShadow();
            if (panel != null) panel.Visibility = Visibility.Collapsed;
            if (aiPanel != null && aiVisible)
            {
                aiVisible = false;
                aiPanel.Visibility = Visibility.Collapsed;
                aiPanel.Effect = null;
            }
            ApplyMusicPanelHeight();
            ApplyMusicLoginVisual();
            if (!musicRowsIntroduced)
            {
                musicRowsIntroduced = true;
                AnimateMusicRows();
            }
            if (MusicPlatforms.Find(musicPlatformId) != null) StartMusicImport();

            if (fromImagePanel)
            {
                musicPanel.Visibility = Visibility.Visible;
                musicPanel.IsHitTestVisible = true;
                musicPanel.Opacity = 0;
                UIElement body = musicPanel.Child as UIElement;
                if (body != null) Motion.AnimateDouble(body, UIElement.OpacityProperty, 0, 1, 220, Motion.Enter, 40, null);
                Motion.PopIn(musicPanel, new Point(0.5, 0.15), 0.96, 10, 200, 0, Motion.Drop);
            }
            else
            {
                SetExpanded(true);
            }
        }

        // 返回图片面板：窗口保持展开态，只把内容换回图片面板
        void BackToImagePanelFromMusic()
        {
            if (!musicVisible) return;
            CloseMusicLoginWindow();
            musicVisible = false;
            musicLastSurface = false;
            HideMusicLoginPicker();

            if (musicPanel != null)
            {
                // 直接收掉，不做淡出：淡出动画被打断时音乐面板会留在上面（表现为一片黑底）
                musicPanel.Visibility = Visibility.Collapsed;
                musicPanel.Opacity = 1;
                musicPanel.Effect = null;
                musicPanel.IsHitTestVisible = true;
            }

            if (panel == null) return;
            panel.Visibility = Visibility.Visible;
            panel.IsHitTestVisible = true;
            Motion.Prepare(panel, PanelMorphOrigin);
            ScaleTransform scale = Motion.ScaleOf(panel, PanelMorphOrigin);
            TranslateTransform move = Motion.MoveOf(panel);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            move.Y = 0;
            panel.Opacity = 1;
            ReloadCardImages();
            Opacity = 1;
            panel.InvalidateVisual();
            // 400ms / 1s 各检查一次：哪段动画没跑完都能纠正回来
            Motion.Delay(400, delegate { EnsureImageSurfaceHealthy(); });
            Motion.Delay(1000, delegate { EnsureImageSurfaceHealthy(); });
        }

        // × / Esc：走正常的收起动画，下次点胶囊直接回到音乐面板
        void CloseMusicPanel()
        {
            if (!musicVisible) return;
            CloseMusicLoginWindow();
            HideMusicLoginPicker();
            if (!expanded)
            {
                musicVisible = false;
                HideMusicSurface();
                return;
            }
            SetExpanded(false);
        }

        void EnsureMusicShadow()
        {
            if (musicPanel == null) return;
            if (musicPanel.Effect == null)
                musicPanel.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Opacity = 0.4 };
        }

        void HideMusicSurface()
        {
            if (musicPanel == null) return;
            musicPanel.Visibility = Visibility.Collapsed;
            musicPanel.Effect = null;
            musicPanel.IsHitTestVisible = true;
        }

        void ApplyMusicPanelHeight()
        {
            // 和 AI 面板一样：窗口高度在构造时定死，运行时只调面板自身高度，绝不改窗口 Height
            if (musicPanel == null) return;
            double avail = MaxWindowHeight() - MusicPanelTop - 12;
            musicPanel.Height = Math.Min(MusicPanelHeight, Math.Max(300, avail));
        }
    }
}
