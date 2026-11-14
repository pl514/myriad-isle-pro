using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace IslandShot
{
    internal sealed class MoreMenuWindow : Window
    {
        public MoreMenuWindow(Action settings, Action add, Action folder, Action clear, Action toggleEyes, bool eyesOn, Action phone)
        {
            Width = 280;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var panel = new StackPanel { Margin = new Thickness(6) };
            panel.Children.Add(new TextBlock
            {
                Text = "更多操作",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(83, 103, 125)),
                Margin = new Thickness(12, 6, 0, 5)
            });
            panel.Children.Add(Row("快捷键设置", "⌨", delegate { Close(); settings(); }));
            panel.Children.Add(Row("添加图片", "＋", delegate { Close(); add(); }));
            panel.Children.Add(Row("手机传图（扫码）", "📱", delegate { Close(); phone(); }));
            panel.Children.Add(Row("打开收藏文件夹", "▤", delegate { Close(); folder(); }));
            panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(232, 237, 244)), Margin = new Thickness(8, 5, 8, 5) });
            panel.Children.Add(Row("全部取下（保留文件）", "↙", delegate { Close(); clear(); }));
            panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(232, 237, 244)), Margin = new Thickness(8, 5, 8, 5) });
            panel.Children.Add(Row(eyesOn ? "卡通眼睛：开" : "卡通眼睛：关", "◡", delegate { Close(); toggleEyes(); }));

            var surface = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(2),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.24 },
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
            Loaded += delegate { ready = true; Activate(); Focus(); };
            Closing += delegate { closing = true; };
            Deactivated += delegate
            {
                // 失焦就收起；但窗口正在关闭时再 Close() 会抛 InvalidOperationException
                // （WPF：在 Window 关闭时无法调用 Close）。加个标志位挡掉。
                if (!ready || closing) return;
                try { Close(); }
                catch (InvalidOperationException) { }
            };
        }

        public void ShowLeftOf(Window parent)
        {
            Show();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                UpdateLayout();
                double width = ActualWidth > 1 ? ActualWidth : Width;
                Left = parent == null ? Left : Math.Max(SystemParameters.WorkArea.Left + 4, parent.Left - width - 8);
                Top = parent == null ? Top : Math.Max(SystemParameters.WorkArea.Top + 4, parent.Top);
                Activate();
            }), DispatcherPriority.ApplicationIdle);
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
                double height = ActualHeight > 1 ? ActualHeight : 240;
                double width = ActualWidth > 1 ? ActualWidth : Width;
                Left = Math.Max(SystemParameters.WorkArea.Left + 4, Math.Min(SystemParameters.WorkArea.Right - width - 4, cursor.X - width + 16));
                Top = Math.Max(SystemParameters.WorkArea.Top + 4, Math.Min(SystemParameters.WorkArea.Bottom - height - 4, cursor.Y - height - 12));
                Activate();
            }), DispatcherPriority.ApplicationIdle);
        }

        static Button Row(string title, string icon, Action action)
        {
            var content = new Grid { Margin = new Thickness(10, 0, 8, 0) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            var iconText = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(83, 103, 125)),
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(iconText);
            var label = new TextBlock
            {
                Text = title,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 51, 67)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var button = new Button
            {
                Height = 38,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = content
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='8'>" +
                "<ContentPresenter/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#F0F4F9'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            button.Click += delegate { action(); };
            return button;
        }
    }
}
