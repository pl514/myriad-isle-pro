using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IslandShot
{
    /// <summary>图绳风格的快捷键设置面板。</summary>
    internal sealed class HotkeySettingsWindow : Window
    {
        readonly HotkeyBinding[] working;
        readonly Func<HotkeyBinding[], string> apply;
        readonly List<Button> hotkeyButtons = new List<Button>();
        readonly TextBlock status;
        int recording = -1;

        public HotkeySettingsWindow(HotkeyBinding[] current, Func<HotkeyBinding[], string> apply)
        {
            this.apply = apply;
            working = new HotkeyBinding[current.Length];
            for (int i = 0; i < current.Length; i++) working[i] = current[i].Clone();

            Width = 440;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            panel.Children.Add(new TextBlock
            {
                Text = "快捷键设置",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "点击快捷键后按下组合键；Backspace 清除，Esc 取消。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 150)),
                Margin = new Thickness(0, 4, 0, 12)
            });

            for (int i = 0; i < working.Length; i++)
            {
                int index = i;
                var row = new Grid { Height = 46 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                var name = new TextBlock
                {
                    Text = working[i].Name,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(name);
                var keyButton = new Button
                {
                    Content = working[i].Display,
                    Height = 30,
                    FontSize = 12,
                    Background = new SolidColorBrush(Color.FromRgb(244, 247, 251)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                keyButton.Template = ButtonTemplate();
                Grid.SetColumn(keyButton, 1);
                keyButton.Click += delegate
                {
                    recording = index;
                    keyButton.Content = "按下快捷键…";
                    keyButton.Focus();
                };
                row.Children.Add(keyButton);
                hotkeyButtons.Add(keyButton);
                var clear = new Button
                {
                    Content = "清除",
                    Height = 28,
                    FontSize = 11,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 115, 133)),
                    Cursor = Cursors.Hand
                };
                Grid.SetColumn(clear, 2);
                clear.Click += delegate
                {
                    working[index].Modifiers = 0;
                    working[index].Key = 0;
                    hotkeyButtons[index].Content = "未设置";
                };
                row.Children.Add(clear);
                panel.Children.Add(row);
                panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(236, 240, 246)) });
            }

            // 一键放歌的歌单选择（和第 2 条自定义项）
            var playlistRow = new Grid { Height = 46 };
            playlistRow.ColumnDefinitions.Add(new ColumnDefinition());
            playlistRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(198) });
            playlistRow.Children.Add(new TextBlock
            {
                Text = "一键放歌歌单",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var playlistButton = new Button
            {
                Content = QuickPlayStore.DisplayName(),
                Height = 30,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(244, 247, 251)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            playlistButton.Template = ButtonTemplate();
            Grid.SetColumn(playlistButton, 1);
            playlistButton.Click += delegate
            {
                string platformId = MusicSession.Load();
                MusicPlatform platform = MusicPlatforms.Find(platformId);
                if (platform == null)
                {
                    status.Text = "先在音乐面板里选好平台并登录，再回来选歌单。";
                    return;
                }
                var picker = new QuickPlaylistWindow(platform, delegate(MusicPlaylistInfo info)
                {
                    if (info == null) QuickPlayStore.Save("", "");
                    else QuickPlayStore.Save(info.Id, info.Name);
                    playlistButton.Content = QuickPlayStore.DisplayName();
                    status.Text = "";
                });
                picker.ShowDialog();
            };
            playlistRow.Children.Add(playlistButton);
            panel.Children.Add(playlistRow);
            panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(236, 240, 246)) });

            status = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 70, 70)),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 76, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Template = ButtonTemplate();
            cancel.Click += delegate { Close(); };
            var save = new Button
            {
                Content = "保存",
                Width = 76,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(36, 99, 174)),
                Foreground = Brushes.White
            };
            save.Template = ButtonTemplate(true);
            save.Click += delegate
            {
                string error = apply(working);
                if (!string.IsNullOrEmpty(error)) { status.Text = error; return; }
                Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 237)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.28 },
                Child = panel
            };

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (recording < 0) return;
                int index = recording;
                if (e.Key == Key.Escape)
                {
                    recording = -1;
                    hotkeyButtons[index].Content = working[index].Display;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Back || e.Key == Key.Delete)
                {
                    working[index].Modifiers = 0;
                    working[index].Key = 0;
                    hotkeyButtons[index].Content = "未设置";
                    recording = -1;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftAlt || e.Key == Key.RightAlt
                    || e.Key == Key.LeftShift || e.Key == Key.RightShift || e.Key == Key.LWin || e.Key == Key.RWin) return;
                uint key = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
                if (key == 0) return;
                uint modifiers = 0;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= HotkeyStore.ModControl;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= HotkeyStore.ModAlt;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= HotkeyStore.ModShift;
                if (modifiers == 0)
                {
                    status.Text = "快捷键至少需要 Ctrl、Alt 或 Shift 中的一个。";
                    return;
                }
                working[index].Modifiers = modifiers;
                working[index].Key = key;
                hotkeyButtons[index].Content = working[index].Display;
                status.Text = "";
                recording = -1;
                e.Handled = true;
            };
            Loaded += delegate { Activate(); Focus(); };
        }

        static ControlTemplate ButtonTemplate(bool primary = false)
        {
            string hover = primary ? "#3A71B0" : "#F0F4F9";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='7' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='" + hover + "'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
        }
    }
}
