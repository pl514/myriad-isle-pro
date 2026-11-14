using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IslandShot
{
    // 新建会话前先选模型：选定后这个会话锁定该模型，中途不能换。
    internal sealed class NewChatModelWindow : Window
    {
        readonly Action<AiModelConfig> picked;

        public NewChatModelWindow(List<AiModelConfig> models, Action<AiModelConfig> onPicked)
        {
            picked = onPicked;

            Title = "新建会话 · 选择模型";
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

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

            panel.Children.Add(new TextBlock
            {
                Text = "选择这个会话使用的模型",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 240, 248))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "选好后这个会话就锁定它，中途不能切换；要换模型请新建会话。",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 162, 184)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 12)
            });

            if (models == null || models.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "还没有配置任何模型。先去「设置 → AI 模型」加一个。",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 150, 150)),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                foreach (AiModelConfig model in models)
                {
                    AiModelConfig captured = model;
                    var button = new Button
                    {
                        Content = model.DisplayName(),
                        Height = 36,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 0, 12, 0),
                        Background = new SolidColorBrush(Color.FromRgb(21, 24, 39)),
                        Foreground = new SolidColorBrush(Color.FromRgb(226, 230, 242)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(42, 47, 66)),
                        BorderThickness = new Thickness(1),
                        FontSize = 12.5
                    };
                    button.Click += delegate
                    {
                        try { Close(); } catch (Exception) { }
                        if (picked != null) picked(captured);
                    };
                    panel.Children.Add(button);
                }
            }

            var cancel = new Button
            {
                Content = "取消",
                Height = 32,
                Margin = new Thickness(0, 8, 0, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 162, 184)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 47, 66)),
                BorderThickness = new Thickness(1),
                FontSize = 12
            };
            cancel.Click += delegate { try { Close(); } catch (Exception) { } };
            panel.Children.Add(cancel);

            var surface = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(9, 11, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 76)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel
            };
            Content = surface;

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
            Loaded += delegate { Activate(); Focus(); };
        }
    }
}