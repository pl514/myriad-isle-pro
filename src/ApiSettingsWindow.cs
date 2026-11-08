using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Threading.Tasks;

namespace IslandShot
{
    // 普通版没有 AI 聊天面板，所以单独提供一个"填 API"的小窗口：
    // 名称 / 接口地址 / 模型 ID / Key，保存后就能在翻译页的"引擎"里选到它。
    internal sealed class ApiSettingsWindow : Window
    {
        readonly TextBox labelBox;
        readonly TextBox urlBox;
        readonly TextBox modelBox;
        readonly TextBox keyBox;
        readonly TextBlock hint;

        public ApiSettingsWindow()
        {
            Title = "翻译 API 设置";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(24, 22, 32));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };

            root.Children.Add(Header("给翻译填一个 OpenAI 兼容接口（BaseUrl + 模型 + Key）"));

            labelBox = Field(root, "名称（随便写，会显示在引擎菜单里）", "我的 DeepSeek");
            urlBox = Field(root, "接口地址 BaseUrl", "https://api.deepseek.com");
            modelBox = Field(root, "模型 ID", "deepseek-chat");
            keyBox = Field(root, "API Key（保存在本机 data\\keys.txt，DPAPI 加密）", "");

            hint = new TextBlock
            {
                Text = "常见写法：DeepSeek → https://api.deepseek.com + deepseek-chat；智谱 → https://open.bigmodel.cn/api/paas/v4 + glm-4-flash",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 182)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 12)
            };
            root.Children.Add(hint);

            var buttons = new DockPanel { LastChildFill = false };
            var save = Button("保存并使用");
            save.Click += delegate { Save(true); };
            var test = Button("检测");
            test.Margin = new Thickness(8, 0, 0, 0);
            test.Click += delegate { Save(false); };
            var close = Button("关闭");
            close.Margin = new Thickness(8, 0, 0, 0);
            close.Click += delegate { Close(); };
            DockPanel.SetDock(save, Dock.Right);
            DockPanel.SetDock(test, Dock.Right);
            DockPanel.SetDock(close, Dock.Right);
            buttons.Children.Add(save);
            buttons.Children.Add(test);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;

            // 预填已有的第一个模型
            try
            {
                foreach (AiModelConfig item in AiTranslateService.AvailableModels())
                {
                    labelBox.Text = item.DisplayName();
                    urlBox.Text = item.BaseUrl;
                    modelBox.Text = item.Model;
                    break;
                }
            }
            catch (Exception) { }
        }

        static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(214, 220, 236)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        static TextBox Field(StackPanel host, string label, string placeholder)
        {
            host.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 174)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var box = new TextBox
            {
                Text = "",
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(34, 31, 44)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(58, 54, 74)),
                ToolTip = placeholder,
                CaretBrush = Brushes.White
            };
            host.Children.Add(box);
            return box;
        }

        static Button Button(string text)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(52, 46, 74)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
        }

        void Save(bool useIt)
        {
            string url = urlBox.Text.Trim();
            string model = modelBox.Text.Trim();
            if (url.Length == 0 || model.Length == 0)
            {
                hint.Text = "接口地址和模型 ID 必须填。";
                return;
            }

            string label = labelBox.Text.Trim();
            if (label.Length == 0) label = model;

            string id = "user_" + Math.Abs(url.GetHashCode()).ToString("X");
            var config = new AiModelConfig
            {
                Id = id,
                Label = label,
                BaseUrl = url,
                Model = model,
                KeyRef = id,
                Vision = false,
                Reasoning = ""
            };
            try
            {
                AiConfigStore.SaveModel(config);
                string key = keyBox.Text.Trim();
                if (key.Length > 0) AiConfigStore.SaveKey(id, key);
                hint.Text = "已保存：" + label;
            }
            catch (Exception ex)
            {
                hint.Text = "保存失败：" + ex.Message;
                return;
            }

            if (useIt)
            {
                TranslateSettings.Engine = id;
                Close();
                return;
            }

            hint.Text = "正在检测…";
            Task.Run(delegate
            {
                AiTranslateService.SelectModel(id);
                string result = AiTranslateService.Translate("Good morning.", "en|zh-CN");
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (string.IsNullOrEmpty(result))
                        hint.Text = "检测失败：" + (AiTranslateService.LastError.Length > 0 ? AiTranslateService.LastError : "无返回");
                    else
                        hint.Text = "检测成功：" + result;
                }));
            });
        }
    }
}
