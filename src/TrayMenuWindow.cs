using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace IslandShot
{
    /// <summary>图绳风格的托盘菜单。</summary>
    internal sealed class TrayMenuWindow : Window
    {
        public bool ChildOpen;
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        public TrayMenuWindow(Action toggle, Action more, Action startup, bool startupOn, Action restart, Action exit)
        {
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var panel = new StackPanel { Margin = new Thickness(6) };
            var header = new Grid { Height = 42, Margin = new Thickness(8, 0, 12, 4) };
            var logo = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(Color.FromRgb(52, 210, 238), Color.FromRgb(34, 124, 196), new Point(0, 0), new Point(1, 1)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "岛",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            header.Children.Add(logo);
            header.Children.Add(new TextBlock
            {
                Text = "万象岛 Pro",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(38, 49, 63)),
                Margin = new Thickness(39, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = "v1.0",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(119, 131, 148)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(header);

            var show = Row("显示 / 收起", "▣", "Ctrl+Alt+Shift+Space", true, delegate { Close(); toggle(); });
            panel.Children.Add(show);
            panel.Children.Add(Row("更多操作", "⋯", "", false, delegate { more(); }));
            panel.Children.Add(Row(startupOn ? "开机自启动：开" : "开机自启动：关", startupOn ? "◉" : "○", "", false, delegate { Close(); startup(); }));
            panel.Children.Add(Separator());
            panel.Children.Add(Row("以普通权限重新启动", "↻", "", false, delegate { Close(); restart(); }));
            panel.Children.Add(Separator());
            panel.Children.Add(Row("退出 万象岛 Pro", "⏻", "", false, delegate { Close(); exit(); }));

            // 权限状态：普通用户就已经够用，这里是给用户一个确定的答案。
            panel.Children.Add(new TextBlock
            {
                Text = "当前权限：" + RuntimeAccess.ModeName +
                       (RuntimeAccess.IsLow ? "（托盘/拖拽受限，请从桌面双击启动）" : "（拖到微信、托盘图标可用）"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 151, 166)),
                Margin = new Thickness(16, 2, 12, 8),
                TextWrapping = TextWrapping.Wrap
            });

            var surface = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 252, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(222, 229, 239)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(2),
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 7, Opacity = 0.24 },
                Child = panel
            };
            Content = surface;
            Motion.WindowIn(this, surface);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
            bool ready = false;
            bool closing = false;
            Loaded += delegate
            {
                ready = true;
                try { SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle); } catch (Exception) { }
                Activate();
                Focus();
            };
            Closing += delegate { closing = true; };
            Deactivated += delegate
            {
                // 失焦就收起；窗口已经在关闭时再 Close() 会抛 InvalidOperationException。
                if (!ready || closing || ChildOpen) return;
                try { Close(); }
                catch (InvalidOperationException) { }
            };
        }

        public void ShowAtCursor()
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            Left = cursor.X + 12;
            Top = cursor.Y - 12;
            Show();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                UpdateLayout();
                double height = ActualHeight > 1 ? ActualHeight : 360;
                double width = ActualWidth > 1 ? ActualWidth : Width;
                double left = cursor.X - width + 16;
                double top = cursor.Y - height - 12;
                Left = Math.Max(SystemParameters.WorkArea.Left + 4, Math.Min(SystemParameters.WorkArea.Right - width - 4, left));
                Top = Math.Max(SystemParameters.WorkArea.Top + 4, Math.Min(SystemParameters.WorkArea.Bottom - height - 4, top));
                DragNative.Log("tray menu at " + Left + "," + Top + " size " + width + "x" + height);
                Activate();
            }), DispatcherPriority.ApplicationIdle);
        }

        static TextBlock Section(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 150)),
                Margin = new Thickness(14, 7, 0, 3)
            };
        }

        static Border Separator()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(232, 237, 244)),
                Margin = new Thickness(8, 5, 8, 5)
            };
        }

        static Button Row(string title, string icon, string shortcut, bool primary, Action action)
        {
            var content = new Grid { Margin = new Thickness(10, 0, 8, 0) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var iconText = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 16,
                Foreground = primary ? new SolidColorBrush(Color.FromRgb(36, 99, 174)) : new SolidColorBrush(Color.FromRgb(83, 103, 125)),
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(iconText);
            var label = new TextBlock
            {
                Text = title,
                FontSize = 13,
                Foreground = primary ? new SolidColorBrush(Color.FromRgb(36, 99, 174)) : new SolidColorBrush(Color.FromRgb(40, 51, 67)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            if (!string.IsNullOrEmpty(shortcut))
            {
                var key = new TextBlock
                {
                    Text = shortcut,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(133, 146, 162)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(key, 2);
                content.Children.Add(key);
            }
            var button = new Button
            {
                Height = 40,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = primary ? new SolidColorBrush(Color.FromRgb(237, 244, 255)) : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = content
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='8' BorderBrush='Transparent' BorderThickness='1'>" +
                "<ContentPresenter/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#F0F4F9'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='bg' Property='Background' Value='#E6EDF6'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>");
            button.Click += delegate { action(); };
            return button;
        }
    }
}
