using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandShot
{
    // 翻译页签：左边输入，右边历史记录。
    // 引擎见 TranslateService（MyMemory 免费接口）。
    internal sealed partial class IslandWindow
    {
        static readonly Color TrPanelBg = Color.FromRgb(19, 16, 26);
        static readonly Color TrCardBg = Color.FromRgb(28, 24, 38);
        static readonly Color TrLine = Color.FromRgb(48, 41, 66);
        static readonly Color TrText = Color.FromRgb(232, 238, 248);
        static readonly Color TrMuted = Color.FromRgb(150, 156, 178);
        static readonly Color TrAccentA = Color.FromRgb(124, 108, 242);
        static readonly Color TrAccentB = Color.FromRgb(96, 148, 246);

        Grid translateView;
        TextBox translateInput;
        TextBlock translatePlaceholder;
        StackPanel translateHistoryList;
        ScrollViewer translateHistoryScroll;
        Button translateButton;
        TextBlock translateStatus;
        Button translateDirButton;
        bool translateBusy;
        bool translateForceZhEn;

        static string TranslateStorePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "translate.txt"); }
        }

        // 圆角按钮：WPF 默认按钮是直角，用模板套一层圆角边框
        static Button TrButton(string text, bool primary, Action click)
        {
            var button = new Button
            {
                Content = text,
                FontSize = primary ? 12.5 : 11.5,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Foreground = primary ? Brushes.White : new SolidColorBrush(TrMuted),
                Background = primary
                    ? (Brush)new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(TrAccentA, 0),
                            new GradientStop(TrAccentB, 1)
                        },
                        new Point(0, 0), new Point(1, 1))
                    : new SolidColorBrush(Color.FromRgb(38, 33, 52)),
                BorderThickness = new Thickness(0),
                Focusable = false
            };

            string margin = primary ? "14,7" : "12,5";
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='9' BorderThickness='1' BorderBrush='#2A2338'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='" + margin + "'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='0.82'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='bg' Property='Opacity' Value='0.55'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");

            if (click != null) button.Click += delegate { click(); };
            return button;
        }

        Grid BuildTranslateView()
        {
            var root = new Grid { Background = new SolidColorBrush(TrPanelBg), Visibility = Visibility.Collapsed };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.46, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.54, GridUnitType.Star) });

            // ---------------- 左：输入 ----------------
            var left = new Grid { Margin = new Thickness(16, 12, 14, 12) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            topRow.Children.Add(new TextBlock
            {
                Text = "原文",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            translateDirButton = new Button
            {
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(34, 29, 48)),
                Foreground = new SolidColorBrush(Color.FromRgb(206, 200, 240)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 55, 92)),
                BorderThickness = new Thickness(1),
                Content = "自动识别",
                ToolTip = "点一下切换翻译方向"
            };
            translateDirButton.Click += delegate { ToggleTranslateDirection(); };
            topRow.Children.Add(translateDirButton);

            Grid.SetRow(topRow, 0);
            left.Children.Add(topRow);

            var inputBorder = new Border
            {
                Margin = new Thickness(0, 8, 0, 10),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(25, 21, 34)),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1)
            };

            var inputHost = new Grid();
            translateInput = new TextBox
            {
                Margin = new Thickness(10, 8, 10, 8),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TrText),
                CaretBrush = new SolidColorBrush(TrText),
                FontSize = 13.5,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            translateInput.TextChanged += delegate { UpdateTranslatePlaceholder(); };
            translateInput.PreviewKeyDown += TranslateInputKeyDown;
            inputHost.Children.Add(translateInput);

            translatePlaceholder = new TextBlock
            {
                Text = "粘贴或输入要翻译的内容，Ctrl+Enter 直接翻译",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                Margin = new Thickness(12, 10, 12, 0),
                IsHitTestVisible = false,
                TextWrapping = TextWrapping.Wrap
            };
            inputHost.Children.Add(translatePlaceholder);
            inputBorder.Child = inputHost;

            Grid.SetRow(inputBorder, 1);
            left.Children.Add(inputBorder);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            translateButton = TrButton("翻译", true, null);
            translateButton.Click += async delegate { await DoTranslate(); };
            buttonRow.Children.Add(translateButton);

            var clearInput = TrButton("清空", false, delegate
            {
                translateInput.Text = "";
                translateStatus.Text = "";
            });
            clearInput.Margin = new Thickness(10, 0, 0, 0);
            buttonRow.Children.Add(clearInput);

            Grid.SetRow(buttonRow, 2);
            left.Children.Add(buttonRow);

            translateStatus = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                Margin = new Thickness(0, 8, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(translateStatus, 3);
            left.Children.Add(translateStatus);

            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // 分隔线
            var divider = new Border { Width = 1, Background = new SolidColorBrush(TrLine), Margin = new Thickness(0, 10, 0, 10) };
            Grid.SetColumn(divider, 1);
            root.Children.Add(divider);

            // ---------------- 右：历史 ----------------
            var right = new Grid { Margin = new Thickness(14, 12, 16, 12) };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var historyHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            historyHeader.Children.Add(new TextBlock
            {
                Text = "历史记录",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center
            });
            var clearHistory = TrButton("清空记录", false, delegate { ClearTranslateHistory(); });
            clearHistory.HorizontalAlignment = HorizontalAlignment.Right;
            clearHistory.FontSize = 10.5;
            historyHeader.Children.Add(clearHistory);
            Grid.SetRow(historyHeader, 0);
            right.Children.Add(historyHeader);

            translateHistoryList = new StackPanel();
            translateHistoryScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = translateHistoryList,
                Focusable = false
            };
            Grid.SetRow(translateHistoryScroll, 1);
            right.Children.Add(translateHistoryScroll);

            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            translateView = root;
            LoadTranslateHistory();
            return root;
        }

        void TranslateInputKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (e.Key == Key.Enter && control)
            {
                e.Handled = true;
                var ignored = DoTranslate();
            }
        }

        void UpdateTranslatePlaceholder()
        {
            if (translatePlaceholder == null) return;
            translatePlaceholder.Visibility = string.IsNullOrEmpty(translateInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        void ToggleTranslateDirection()
        {
            translateForceZhEn = !translateForceZhEn;
            translateDirButton.Content = translateForceZhEn ? "中 → 英" : "自动识别";
        }

        async Task DoTranslate()
        {
            if (translateBusy) return;
            string text = (translateInput.Text ?? "").Trim();
            if (text.Length == 0)
            {
                translateStatus.Text = "先输入内容";
                return;
            }
            if (text.Length > 1500)
            {
                translateStatus.Text = "一次最多翻译 1500 个字符";
                return;
            }

            string pair = TranslateService.PairFor(text, translateForceZhEn);
            translateBusy = true;
            translateButton.IsEnabled = false;
            translateButton.Content = "翻译中…";
            translateStatus.Text = "正在翻译…";

            try
            {
                string result = await TranslateService.TranslateAsync(text, pair);
                result = (result ?? "").Trim();
                if (result.Length == 0) throw new Exception("翻译结果为空");

                translateStatus.Text = "已翻译为" + TranslateService.TargetLabel(pair);
                AddTranslateCard(text, result, pair, true);
                SaveTranslateHistory();
            }
            catch (Exception ex)
            {
                translateStatus.Text = "失败：" + ex.Message;
            }
            finally
            {
                translateBusy = false;
                translateButton.IsEnabled = true;
                translateButton.Content = "翻译";
            }
        }

        void AddTranslateCard(string source, string target, string pair, bool save)
        {
            if (translateHistoryList == null) return;

            var card = new Border
            {
                Background = new SolidColorBrush(TrCardBg),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = source,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 172)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 54,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            stack.Children.Add(new TextBlock
            {
                Text = target,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(TrText),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 6),
                LineHeight = 20
            });

            var footer = new Grid();
            footer.Children.Add(new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm") + " · 译成" + TranslateService.TargetLabel(pair),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 114, 138)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var copy = TrButton("复制", false, delegate
            {
                try { Clipboard.SetText(target); translateStatus.Text = "译文已复制"; }
                catch (Exception) { }
            });
            copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.FontSize = 10.5;
            footer.Children.Add(copy);
            stack.Children.Add(footer);

            card.Child = stack;
            translateHistoryList.Children.Insert(0, card);

            // 只保留最近 30 条，避免无限膨胀
            while (translateHistoryList.Children.Count > 30)
            {
                translateHistoryList.Children.RemoveAt(translateHistoryList.Children.Count - 1);
            }
            if (translateHistoryScroll != null) translateHistoryScroll.ScrollToTop();
        }

        void ClearTranslateHistory()
        {
            if (translateHistoryList != null) translateHistoryList.Children.Clear();
            try { if (File.Exists(TranslateStorePath)) File.Delete(TranslateStorePath); }
            catch (Exception) { }
            translateStatus.Text = "记录已清空";
        }

        void SaveTranslateHistory()
        {
            try
            {
                string folder = Path.GetDirectoryName(TranslateStorePath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var lines = new List<string>();
                if (translateHistoryList != null)
                {
                    foreach (object child in translateHistoryList.Children)
                    {
                        var card = child as Border;
                        if (card == null) continue;
                        var stack = card.Child as StackPanel;
                        if (stack == null || stack.Children.Count < 2) continue;
                        var sourceBlock = stack.Children[0] as TextBlock;
                        var targetBlock = stack.Children[1] as TextBlock;
                        if (sourceBlock == null || targetBlock == null) continue;

                        lines.Add(DateTime.Now.ToString("yyyyMMddHHmm") + "\t" +
                                  Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceBlock.Text)) + "\t" +
                                  Convert.ToBase64String(Encoding.UTF8.GetBytes(targetBlock.Text)));
                    }
                }
                File.WriteAllLines(TranslateStorePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        void LoadTranslateHistory()
        {
            try
            {
                if (!File.Exists(TranslateStorePath)) return;
                string[] lines = File.ReadAllLines(TranslateStorePath, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 30);
                for (int i = start; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length < 3) continue;
                    string source, target;
                    try
                    {
                        source = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                        target = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                    }
                    catch (Exception) { continue; }
                    AddTranslateCard(source, target, "zh-CN|en", false);
                }
            }
            catch (Exception) { }
        }
    }
}
