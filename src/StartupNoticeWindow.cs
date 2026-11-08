using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IslandShot
{
    /// <summary>
    /// 低权限（Low）启动时的提示窗。
    ///
    /// 低权限下托盘图标加不进去（Shell_NotifyIcon 返回 FALSE），所以不能用气泡提示；
    /// 这个窗口不依赖托盘，直接把原因和解决办法摆在屏幕上。
    /// </summary>
    internal sealed class StartupNoticeWindow : Window
    {
        readonly CheckBox neverShow;

        public StartupNoticeWindow()
        {
            Title = "万象岛 Pro";
            Width = 442;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 14) };

            panel.Children.Add(new TextBlock
            {
                Text = "万象岛 Pro 被系统以低权限启动了",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 242, 248))
            });

            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(186, 195, 208)),
                Margin = new Thickness(0, 10, 0, 0),
                LineHeight = 20,
                Text =
                    "这种低权限进程会被 Windows 静默限制：" + Environment.NewLine +
                    "  · 托盘图标加不进去（Shell_NotifyIcon 直接被拒绝）" + Environment.NewLine +
                    "  · 图片拖不进微信、资源管理器等普通程序（松手后 effect=0）" + Environment.NewLine + Environment.NewLine +
                    "不需要管理员，只要用普通用户身份启动就行：" + Environment.NewLine +
                    "  · 双击项目里的「启动 万象岛 Pro.cmd」" + Environment.NewLine +
                    "  · 或者直接双击 build\\MyriadIslePro.exe" + Environment.NewLine + Environment.NewLine +
                    "如果是从终端或自动化工具里启动的，那个环境本身就是低权限，" + Environment.NewLine +
                    "请改成在桌面上双击启动（这样就是普通用户权限）。"
            });

            panel.Children.Add(new TextBlock
            {
                Text = "当前权限：" + RuntimeAccess.ModeName + "（" + RuntimeAccess.IntegrityName + "）",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 151, 166)),
                Margin = new Thickness(0, 8, 0, 0)
            });

            neverShow = new CheckBox
            {
                Content = "下次不再提示",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 171, 187)),
                Margin = new Thickness(0, 10, 0, 0),
                Focusable = false
            };
            panel.Children.Add(neverShow);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var openFolder = ActionButton("打开所在文件夹", false);
            openFolder.Click += delegate { OpenFolder(); };
            var dismiss = ActionButton("知道了", true);
            dismiss.Click += delegate { Close(); };
            row.Children.Add(openFolder);
            row.Children.Add(dismiss);
            panel.Children.Add(row);

            var surface = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(248, 36, 42, 50),
                    Color.FromArgb(246, 26, 31, 38),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.42 },
                Child = panel
            };
            Content = surface;

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
            Closed += delegate
            {
                if (neverShow.IsChecked == true) AppSettings.LowPrivilegeNotice = false;
            };
        }

        void OpenFolder()
        {
            try
            {
                string exe = RuntimeAccess.ExePath;
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + exe + "\"");
            }
            catch (Exception exception)
            {
                DragNative.Log("open folder failed: " + exception.Message);
            }
        }

        static Button ActionButton(string text, bool primary)
        {
            var button = new Button
            {
                Content = text,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(206, 214, 226)),
                Background = primary ? new SolidColorBrush(Color.FromRgb(52, 132, 214)) : new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='8'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            return button;
        }
    }
}
