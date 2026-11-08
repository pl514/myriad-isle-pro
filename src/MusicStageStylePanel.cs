using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IslandShot
{
    // 投射窗里的「歌词样式」设置面板（图一那套的本地版）。
    //
    // 左边是 18 个样式的实时预览 / 样式卡；右边是歌词动画、背景、字体、
    // 字幕、全局、默认六个页签。设置直接写回 MusicStageSettings，
    // 由宿主窗口决定哪些参数需要重排、哪些可以逐帧生效。
    internal sealed class MusicStageStylePanel
    {
        public readonly Border Root;
        readonly MusicStageSettings settings;
        readonly Action changed;
        readonly Action close;
        readonly StackPanel content;
        readonly UniformGrid styleGrid;
        readonly List<Border> styleCards = new List<Border>();
        readonly TextBlock previewTitle;
        readonly TextBlock previewSub;
        readonly Border preview;
        readonly TextBlock themeName;
        int tabIndex;

        static readonly string[] TabNames = new string[]
        {
            "歌词动画", "背景", "字体", "字幕", "全局", "默认"
        };

        public MusicStageStylePanel(MusicStageSettings settings, Action changed, Action close)
        {
            this.settings = settings;
            this.changed = changed;
            this.close = close;

            Root = new Border
            {
                Width = 1040,
                Height = 730,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 16,
                    BlurRadius = 46,
                    Opacity = 0.7,
                    RenderingBias = RenderingBias.Performance
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Root.Child = grid;

            // ---------------------------------------------------------- 标题栏
            var header = new Grid { Margin = new Thickness(22, 0, 18, 0) };
            var title = new TextBlock
            {
                Text = "歌词样式",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 245, 247)),
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(title);
            var closeButton = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = SimpleIcon("close", Color.FromArgb(220, 255, 255, 255), 13)
            };
            closeButton.MouseLeftButtonUp += delegate { if (close != null) close(); };
            header.Children.Add(closeButton);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var body = new Grid { Margin = new Thickness(18, 0, 18, 18) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(596) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            // ---------------------------------------------------------- 左：预览 + 样式格
            var left = new StackPanel();
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            preview = new Border
            {
                Height = 300,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 12)),
                ClipToBounds = true
            };
            left.Children.Add(preview);
            var previewCanvas = new Canvas();
            preview.Child = previewCanvas;
            previewTitle = new TextBlock
            {
                FontSize = 44,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.White,
                IsHitTestVisible = false
            };
            previewCanvas.Children.Add(previewTitle);
            previewSub = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                IsHitTestVisible = false
            };
            previewCanvas.Children.Add(previewSub);

            var gridTitle = new TextBlock
            {
                Text = "样式 18",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 210, 212, 220)),
                Margin = new Thickness(2, 14, 0, 8)
            };
            left.Children.Add(gridTitle);

            styleGrid = new UniformGrid { Columns = 6, Rows = 3 };
            var gridHost = new Border
            {
                Height = 300,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(19, 19, 22))
            };
            gridHost.Child = styleGrid;
            left.Children.Add(gridHost);

            // ---------------------------------------------------------- 右：页签 + 设置内容
            var right = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(19, 19, 22)),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(14, 0, 0, 0)
            };
            Grid.SetColumn(right, 1);
            body.Children.Add(right);

            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 8, 0) };
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = new Border
                {
                    Height = 44,
                    Padding = new Thickness(10, 0, 10, 0),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = TabNames[i],
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(Color.FromArgb(150, 210, 212, 220)),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                tab.MouseLeftButtonUp += delegate { SelectTab(index); };
                tabBar.Children.Add(tab);
            }
            Grid.SetRow(tabBar, 0);
            rightGrid.Children.Add(tabBar);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(6, 0, 6, 6)
            };
            content = new StackPanel { Margin = new Thickness(10, 6, 10, 10) };
            scroll.Content = content;
            Grid.SetRow(scroll, 1);
            rightGrid.Children.Add(scroll);
            right.Child = rightGrid;

            themeName = new TextBlock();
            BuildStyleCards();
            SelectTab(0);
            Refresh();
        }

        // ================================================================ 样式卡 / 预览

        void BuildStyleCards()
        {
            styleGrid.Children.Clear();
            styleCards.Clear();
            var all = MusicStageStyles.All;
            for (int i = 0; i < all.Count; i++)
            {
                MusicStageStyle style = all[i];
                string id = style.Id;
                var card = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = Brushes.Transparent,
                    Background = new LinearGradientBrush(style.BgTop, style.BgBottom, 90),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true,
                    ToolTip = style.Name + " · " + style.Description
                };
                var cardGrid = new Grid();
                var big = new TextBlock
                {
                    Text = style.Name.Substring(0, 1),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(MusicStageUtil.Alpha(style.Ink, 220)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 14),
                    IsHitTestVisible = false
                };
                cardGrid.Children.Add(big);
                var name = new TextBlock
                {
                    Text = style.Name,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(MusicStageUtil.Alpha(style.Ink, 235)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 7),
                    IsHitTestVisible = false
                };
                cardGrid.Children.Add(name);
                card.Child = cardGrid;
                card.MouseLeftButtonUp += delegate
                {
                    settings.StyleId = id;
                    settings.ThemeName = style.Name;
                    if (changed != null) changed();
                    Refresh();
                };
                styleGrid.Children.Add(card);
                styleCards.Add(card);
            }
        }

        void Refresh()
        {
            MusicStageStyle style = MusicStageStyles.Find(settings.StyleId);
            for (int i = 0; i < styleCards.Count; i++)
            {
                styleCards[i].BorderBrush = i == IndexOf(settings.StyleId)
                    ? new SolidColorBrush(MusicStageUtil.Lift(style.Accent, 0.25))
                    : Brushes.Transparent;
            }

            var canvas = preview.Child as Canvas;
            if (canvas != null)
            {
                canvas.Children.Clear();
                var bg = new Rectangle
                {
                    Width = preview.ActualWidth > 1 ? preview.ActualWidth : 560,
                    Height = preview.ActualHeight > 1 ? preview.ActualHeight : 300,
                    Fill = new LinearGradientBrush(style.BgTop, style.BgBottom, 90)
                };
                canvas.Children.Add(bg);
                // 预览里的装饰色块，让 18 个样式一眼有区别
                for (int i = 0; i < 5; i++)
                {
                    var block = new Rectangle
                    {
                        Width = 70 + i * 24,
                        Height = 14 + i * 7,
                        Fill = new SolidColorBrush(MusicStageUtil.Alpha(
                            i % 2 == 0 ? style.Accent : style.Secondary, (byte)(26 + i * 8))),
                        RenderTransform = new RotateTransform(-14 + i * 9, 0, 0)
                    };
                    Canvas.SetLeft(block, 40 + i * 88);
                    Canvas.SetTop(block, 150 + (i % 3) * 36);
                    canvas.Children.Add(block);
                }
                var title = new TextBlock
                {
                    Text = style.Name,
                    FontSize = 52,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(style.Ink),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(title, 34);
                Canvas.SetTop(title, 84);
                canvas.Children.Add(title);
                var desc = new TextBlock
                {
                    Text = style.Description,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(MusicStageUtil.Alpha(style.Dim, 235)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(desc, 38);
                Canvas.SetTop(desc, 156);
                canvas.Children.Add(desc);
                var sample = new TextBlock
                {
                    Text = "把这一句歌词放进画面",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(MusicStageUtil.Alpha(style.Ink, 200)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(sample, 38);
                Canvas.SetTop(sample, 230);
                canvas.Children.Add(sample);
            }
            BuildTab();
        }

        int IndexOf(string id)
        {
            var all = MusicStageStyles.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.Ordinal)) return i;
            }
            return 0;
        }

        // ================================================================ 页签

        void SelectTab(int index)
        {
            tabIndex = index;
            for (int i = 0; i < styleCards.Count; i++) { }
            var bar = FindTabBar();
            if (bar != null)
            {
                for (int i = 0; i < bar.Children.Count; i++)
                {
                    var tab = bar.Children[i] as Border;
                    if (tab == null) continue;
                    var text = tab.Child as TextBlock;
                    if (text == null) continue;
                    bool active = i == index;
                    tab.Background = active
                        ? new SolidColorBrush(Color.FromArgb(22, 255, 255, 255))
                        : Brushes.Transparent;
                    text.Foreground = new SolidColorBrush(active
                        ? Colors.White
                        : Color.FromArgb(150, 210, 212, 220));
                }
            }
            BuildTab();
        }

        StackPanel FindTabBar()
        {
            // Root -> body Grid -> right Border -> Grid -> tabBar StackPanel
            var grid = Root.Child as Grid;
            if (grid == null) return null;
            var body = grid.Children.Count > 1 ? grid.Children[1] as Grid : null;
            if (body == null) return null;
            if (body.Children.Count < 2) return null;
            var right = body.Children[1] as Border;
            if (right == null) return null;
            var rightGrid = right.Child as Grid;
            if (rightGrid == null) return null;
            return rightGrid.Children.Count > 0 ? rightGrid.Children[0] as StackPanel : null;
        }

        void BuildTab()
        {
            content.Children.Clear();
            MusicStageStyle style = MusicStageStyles.Find(settings.StyleId);
            if (tabIndex == 0) BuildAnimationTab(style);
            else if (tabIndex == 1) BuildBackgroundTab(style);
            else if (tabIndex == 2) BuildFontTab(style);
            else if (tabIndex == 3) BuildSubtitleTab(style);
            else if (tabIndex == 4) BuildGlobalTab(style);
            else BuildDefaultTab(style);
        }

        void BuildAnimationTab(MusicStageStyle style)
        {
            AddInfo("在「全局」页打开智能动效模式后，这些效果才会生效。");
            AddSection("动效效果");
            AddToggle("随拍开花", "每拍在夜空里开一朵小烟花；段落第一拍更大。", () => settings.BeatBloom, v => settings.BeatBloom = v);
            AddToggle("随拍推镜", "每拍整个画面推近一下，段落第一拍更深。", () => settings.BeatPush, v => settings.BeatPush = v);

            AddSection("烟花");
            AddSlider("火花数量", () => settings.SparkCount, v => settings.SparkCount = v, 0, 2, "0.00");
            AddSlider("烟花辉光", () => settings.SparkGlow, v => settings.SparkGlow = v, 0, 2, "0.00");
            AddSlider("副歌辉光", () => settings.ChorusGlow, v => settings.ChorusGlow = v, 0, 2, "0.00");
            AddSlider("整体辉光", () => settings.OverallGlow, v => settings.OverallGlow = v, 0, 2, "0.00");
            AddToggle("升空尾迹", "烟花升空时画一条淡出的尾迹。", () => settings.Trail, v => settings.Trail = v);

            AddSection("画面");
            AddSlider("镜头强度", () => settings.CameraIntensity, v => settings.CameraIntensity = v, 0, 2, "0.00");
            AddSlider("文字动效", () => settings.MotionIntensity, v => settings.MotionIntensity = v, 0, 2, "0.00");
            AddToggle("换段过渡", "段落切换时淡出、模糊、轻微缩放。", () => settings.Transitions, v => settings.Transitions = v);
            AddNote("当前样式：" + style.Name + "。烟花主要用在「花火」，其它样式会把它当作通用光点。");
        }

        void BuildBackgroundTab(MusicStageStyle style)
        {
            AddInfo("背景跟着样式走；也可以让封面主色参与。");
            AddToggle("跟随封面取色", "从 QQ 封面提取主色，应用到光晕、进度和高亮。", () => settings.CoverTint, v => settings.CoverTint = v);
            AddSlider("背景压暗", () => settings.BackgroundDim, v => settings.BackgroundDim = v, 0, 0.7, "0.00");
            AddSlider("装饰密度", () => settings.DecorDensity, v => settings.DecorDensity = v, 0.2, 1.6, "0.00");
            AddNote("样式背景：" + style.BackgroundKind + "；主色 " + ColorText(style.Accent) + "。");
        }

        void BuildFontTab(MusicStageStyle style)
        {
            AddInfo("字体由样式决定：商籁/心象/云阶偏衬线，绘光/町景/花火偏无衬线，时计用等宽。");
            AddSlider("字号缩放", () => settings.FontScale, v => settings.FontScale = v, 0.7, 1.6, "0.00");
            AddNote("当前字体：" + FontName(style.FontKind) + "；改字号会重新排版当前段。");
        }

        void BuildSubtitleTab(MusicStageStyle style)
        {
            AddInfo("QQ 歌词接口目前只返回原文，没有翻译。这里先用当前句作为副字幕。");
            AddToggle("显示副字幕", "在画面底部显示当前唱到的一句。", () => settings.ShowTranslation, v => settings.ShowTranslation = v);
            AddNote("副字幕是投射窗自己的叠加层，不改变胶囊面板里的歌词。");
        }

        void BuildGlobalTab(MusicStageStyle style)
        {
            AddInfo("全局开关影响所有歌词样式。");
            AddToggle("减少动画", "关掉镜头呼吸、粒子和烟花。", () => settings.ReduceMotion, v => settings.ReduceMotion = v);
            AddToggle("换段过渡", "段落切换时淡出、模糊、轻微缩放。", () => settings.Transitions, v => settings.Transitions = v);
            AddToggle("每首歌随机样式", "切歌时从 18 个样式里按歌名稳定地挑一个。", () => settings.RandomPerSong, v => settings.RandomPerSong = v);
            AddSlider("动效强度", () => settings.MotionIntensity, v => settings.MotionIntensity = v, 0, 2, "0.00");
            AddNote("主题：MIDNIGHT DEFAULT / " + style.Name);
        }

        void BuildDefaultTab(MusicStageStyle style)
        {
            AddInfo("恢复所有歌词动画参数到初始值。样式会回到「商籁」。");
            var reset = new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "恢复默认",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 244)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(0, 8, 0, 0)
            };
            reset.MouseLeftButtonUp += delegate
            {
                settings.CopyFrom(new MusicStageSettings());
                if (changed != null) changed();
                Refresh();
            };
            content.Children.Add(reset);
            AddNote("当前：" + style.Name + " / " + settings.ThemeName);
        }

        // ================================================================ 控件

        void AddSection(string text)
        {
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(236, 237, 242)),
                Margin = new Thickness(0, 16, 0, 6)
            });
        }

        void AddInfo(string text)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8)
            };
            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 214, 217, 225)),
                TextWrapping = TextWrapping.Wrap
            };
            content.Children.Add(border);
        }

        void AddNote(string text)
        {
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(130, 190, 194, 204)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 4)
            });
        }

        void AddToggle(string label, string hint, Func<bool> get, Action<bool> set)
        {
            var row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 234, 240))
            });
            if (!string.IsNullOrEmpty(hint))
            {
                text.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 184, 194)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 260
                });
            }
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            var switchBorder = new Border
            {
                Width = 44,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(get()
                    ? MusicStageUtil.Alpha(MusicStageStyles.Find(settings.StyleId).Accent, 200)
                    : Color.FromArgb(30, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            var knob = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new SolidColorBrush(Colors.White),
                HorizontalAlignment = get() ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(3, 0, 3, 0)
            };
            switchBorder.Child = knob;
            switchBorder.MouseLeftButtonUp += delegate
            {
                bool value = !get();
                set(value);
                switchBorder.Background = new SolidColorBrush(value
                    ? MusicStageUtil.Alpha(MusicStageStyles.Find(settings.StyleId).Accent, 200)
                    : Color.FromArgb(30, 255, 255, 255));
                knob.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                if (changed != null) changed();
            };
            Grid.SetColumn(switchBorder, 1);
            row.Children.Add(switchBorder);
            content.Children.Add(row);
        }

        void AddSlider(string label, Func<double> get, Action<double> set, double min, double max, string format)
        {
            var host = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 232, 234, 240))
            };
            var valueText = new TextBlock
            {
                Text = get().ToString(format, CultureInfo.InvariantCulture),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(190, 214, 217, 225))
            };
            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(valueText, 1);
            top.Children.Add(labelText);
            top.Children.Add(valueText);
            host.Children.Add(top);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = MusicStageUtil.Clamp(get(), min, max),
                IsMoveToPointEnabled = true,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(MusicStageStyles.Find(settings.StyleId).Accent)
            };
            slider.ValueChanged += delegate
            {
                set(slider.Value);
                valueText.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);
                if (changed != null) changed();
            };
            host.Children.Add(slider);
            content.Children.Add(host);
        }

        static string FontName(string kind)
        {
            if (kind == "sans") return "无衬线";
            if (kind == "display") return "粗展示体";
            if (kind == "hand") return "手写";
            if (kind == "mono") return "等宽";
            return "衬线";
        }

        static string ColorText(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        static FrameworkElement SimpleIcon(string kind, Color color, double size)
        {
            var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
            var brush = new SolidColorBrush(color);
            if (kind == "close")
            {
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
                    Stroke = brush,
                    StrokeThickness = 1.7,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas };
        }
    }
}
