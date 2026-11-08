using System;

using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace IslandShot
{
    // 屏幕翻译浮层：把整屏"定格"成一张图铺满屏幕，然后原位翻译。
    // 动画：从一个点扩张到全屏（repeating 视频里的效果）；翻译中整屏轻微模糊，完成后恢复清晰。
    internal sealed class TranslateOverlayWindow : Window
    {
        readonly string sourcePath;
        readonly System.Drawing.Rectangle screenBounds;
        readonly Point originDip;
        readonly Image picture;
        readonly Grid root;
        readonly DropShadowEffect shadow;
        readonly Border statusPill;
        readonly TextBlock statusText;
        readonly TextBlock hintText;
        readonly Border topBar;
        readonly BlurEffect blur;

        string translatedPath;
        EllipseGeometry revealClip;
        bool translating;

        static double DpiScale()
        {
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                return Math.Max(1.0, graphics.DpiX / 96.0);
            }
        }

        public TranslateOverlayWindow(string capturedImage, System.Drawing.Rectangle physicalBounds, Point origin)
        {
            sourcePath = capturedImage;
            screenBounds = physicalBounds;
            originDip = origin;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = true;

            double dpi = DpiScale();
            Left = physicalBounds.Left / dpi;
            Top = physicalBounds.Top / dpi;
            Width = physicalBounds.Width / dpi;
            Height = physicalBounds.Height / dpi;

            root = new Grid();

            blur = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian };
            picture = new Image
            {
                Stretch = Stretch.Fill,
                Source = LoadImage(sourcePath),
                Effect = blur
            };
            root.Children.Add(picture);

            // 顶部提示条
            statusText = new TextBlock
            {
                Text = "正在识别文字…",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 240, 250)),
                VerticalAlignment = VerticalAlignment.Center
            };
            hintText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 198, 214)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };

            var barRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            barRow.Children.Add(statusText);
            barRow.Children.Add(hintText);

            var close = new Button
            {
                Content = "✕",
                Width = 30,
                Height = 30,
                FontSize = 13,
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 240, 250)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                ToolTip = "关闭（Esc）"
            };
            close.Click += delegate { Close(); };

            var barGrid = new Grid();
            barGrid.Children.Add(barRow);
            barGrid.Children.Add(close);

            topBar = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(18, 10, 10, 10),
                Background = new SolidColorBrush(Color.FromArgb(214, 18, 20, 28)),
                Child = barGrid,
                Opacity = 0
            };
            root.Children.Add(topBar);

            // 底部状态胶囊
            statusPill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 40),
                Padding = new Thickness(16, 8, 16, 8),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(220, 22, 24, 33)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "屏幕翻译 · 正在准备",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 244))
                }
            };
            root.Children.Add(statusPill);

            var menu = new ContextMenu();
            var saveItem = new MenuItem { Header = "保存翻译图片" };
            saveItem.Click += delegate { SaveTranslated(); };
            var copyItem = new MenuItem { Header = "复制翻译图片" };
            copyItem.Click += delegate { CopyTranslated(); };
            var exitItem = new MenuItem { Header = "关闭" };
            exitItem.Click += delegate { Close(); };
            menu.Items.Add(saveItem);
            menu.Items.Add(copyItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);
            root.ContextMenu = menu;

            Content = root;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
            Loaded += delegate { StartReveal(); };
        }

        static BitmapImage LoadImage(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }

        // 从一个点扩张到全屏
        void StartReveal()
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;

            double cx = Math.Max(0, Math.Min(width, originDip.X - Left));
            double cy = Math.Max(0, Math.Min(height, originDip.Y - Top));
            double maxRadius = Math.Sqrt(width * width + height * height);

            revealClip = new EllipseGeometry(new Point(cx, cy), 0, 0);
            root.Clip = revealClip;

            Motion.AnimateDouble(revealClip, EllipseGeometry.RadiusXProperty, 0, maxRadius, 340, Motion.Enter, 0, delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    root.Clip = null;
                    BeginTranslate();
                }), DispatcherPriority.Background);
            });
            Motion.AnimateDouble(revealClip, EllipseGeometry.RadiusYProperty, 0, maxRadius, 340, Motion.Enter, 0, null);
            Motion.AnimateDouble(topBar, UIElement.OpacityProperty, 0, 1, 260, Motion.Enter, 160, null);
        }

        void SetStatus(string text)
        {
            TranslateOverlayWindow self = this;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Border pill = self.statusPill;
                if (pill == null) return;
                var label = pill.Child as TextBlock;
                if (label != null) label.Text = text;
            }), DispatcherPriority.Background);
        }

        void BeginTranslate()
        {
            translating = true;
            Motion.AnimateDouble(blur, BlurEffect.RadiusProperty, 0, 9, 420, Motion.Enter, 0, null);

            string path = sourcePath;
            Task.Run(delegate
            {
                try
                {
                    ImageTranslationResult result = ImageTranslator.Translate(path, delegate(string message)
                    {
                        SetStatus("屏幕翻译 · " + message);
                    });

                    Dispatcher.BeginInvoke(new Action(delegate { ShowResult(result); }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        translating = false;
                        Motion.AnimateDouble(blur, BlurEffect.RadiusProperty, blur.Radius, 0, 300, Motion.Exit, 0, null);
                        statusText.Text = "翻译失败：" + message;
                        hintText.Text = "按 Esc 关闭";
                    }), DispatcherPriority.Background);
                }
            });
        }

        void ShowResult(ImageTranslationResult result)
        {
            translating = false;
            translatedPath = result.OutputPath;

            if (!string.IsNullOrEmpty(translatedPath) && File.Exists(translatedPath))
            {
                picture.Source = LoadImage(translatedPath);
            }
            // 模糊退掉，恢复清晰
            Motion.AnimateDouble(blur, BlurEffect.RadiusProperty, blur.Radius, 0, 320, Motion.Exit, 0, null);

            statusText.Text = "已翻译 " + result.TranslatedCount + " 行";
            hintText.Text = "Esc 或右上角 ✕ 关闭 · 右键可保存图片";

            Motion.AnimateDouble(statusPill, UIElement.OpacityProperty, 1, 0, 240, Motion.Exit, 1800, null);
        }

        void SaveTranslated()
        {
            if (string.IsNullOrEmpty(translatedPath) || !File.Exists(translatedPath))
            {
                statusText.Text = "还没有翻译结果";
                return;
            }
            using (var dialog = new Forms.SaveFileDialog())
            {
                dialog.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg";
                dialog.FileName = Path.GetFileName(translatedPath);
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    File.Copy(translatedPath, dialog.FileName, true);
                    statusText.Text = "已保存：" + dialog.FileName;
                }
                catch (Exception) { }
            }
        }

        void CopyTranslated()
        {
            if (string.IsNullOrEmpty(translatedPath) || !File.Exists(translatedPath)) return;
            try
            {
                Clipboard.SetImage(LoadImage(translatedPath));
                statusText.Text = "翻译图片已复制";
            }
            catch (Exception) { }
        }
    }
}
