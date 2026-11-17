using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IslandShot
{
    // 「一键放歌」用哪个歌单：列出当前平台的歌单让用户挑一个
    internal sealed class QuickPlaylistWindow : Window
    {
        readonly MusicPlatform platform;
        readonly Action<MusicPlaylistInfo> picked;
        readonly StackPanel list;
        readonly TextBlock status;
        bool includeDefault = true;

        public QuickPlaylistWindow(MusicPlatform platform, Action<MusicPlaylistInfo> onPicked)
        {
            this.platform = platform;
            picked = onPicked;

            Width = 380;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            panel.Children.Add(new TextBlock
            {
                Text = "一键放歌 · 选择歌单",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "按快捷键时从这个歌单里随机挑一首开始放。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 150)),
                Margin = new Thickness(0, 4, 0, 10),
                TextWrapping = TextWrapping.Wrap
            });

            status = new TextBlock
            {
                Text = "正在读取歌单…",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 150))
            };
            panel.Children.Add(status);

            list = new StackPanel();
            panel.Children.Add(list);

            var cancel = new Button
            {
                Content = "取消",
                Height = 30,
                Margin = new Thickness(0, 10, 0, 0),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 115, 133))
            };
            cancel.Click += delegate { Close(); };
            panel.Children.Add(cancel);

            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 237)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.28 },
                Child = panel
            };

            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            Loaded += delegate { Activate(); LoadPlaylists(); };
        }

        void LoadPlaylists()
        {
            if (platform == null)
            {
                status.Text = "先在音乐面板选一个平台并登录，再回来选歌单。";
                return;
            }

            // ① 先用本地缓存把列表显示出来（打开过音乐面板就有缓存）
            List<MusicPlaylistInfo> cached = MusicCache.LoadPlaylists(platform.Id);
            if (cached != null && cached.Count > 0)
            {
                Render(cached);
                status.Text = "本地缓存（正在刷新…）";
            }
            else
            {
                status.Text = "正在读取歌单…";
            }

            // ② 后台刷新：成功就覆盖列表；失败但有缓存就保留缓存
            MusicImport.LoadPlaylists(platform, delegate(List<MusicPlaylistInfo> lists)
            {
                if (lists == null || lists.Count == 0)
                {
                    status.Text = cached == null ? "没读到歌单：接口返回空（登录态可能失效，回音乐面板重新登录一次）" : "刷新失败，已显示本地缓存的歌单";
                    return;
                }
                MusicCache.SavePlaylists(platform.Id, lists);
                Render(lists);
                status.Text = "点一个歌单即可设为默认：";
            }, delegate(string message)
            {
                status.Text = cached == null
                    ? "读取歌单失败：" + message
                    : "刷新失败（已用本地缓存）：" + message;
            });
        }

        void Render(List<MusicPlaylistInfo> lists)
        {
            list.Children.Clear();
            list.Children.Add(BuildRow(null, "默认（第一个歌单）"));
            for (int i = 0; i < lists.Count; i++)
            {
                list.Children.Add(BuildRow(lists[i], lists[i].Name + "  ·  " + lists[i].Count + " 首"));
            }
        }

        Button BuildRow(MusicPlaylistInfo info, string text)
        {
            var button = new Button
            {
                Content = text,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 3, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(244, 247, 251)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 237)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67)),
                FontSize = 12
            };
            button.Click += delegate
            {
                if (picked != null) picked(info);
                Close();
            };
            return button;
        }
    }
}