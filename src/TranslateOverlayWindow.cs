using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace IslandShot
{
    // 屏幕翻译浮层：
    //   · 从触发点圆形扩张铺满屏幕
    //   · 翻译过程中逐行"冲刷"：已经翻好的行先变成译文，正在处理的位置有一个光斑扫过
    //   · 完成后停留，Esc / ✕ 关闭，右键可保存
    internal sealed class TranslateOverlayWindow : Window
    {
        readonly string sourcePath;
        readonly System.Drawing.Rectangle screenBounds;
        readonly Point originDip;
        readonly Image picture;
        readonly Grid root;
        readonly Canvas ringLayer;
        readonly System.Windows.Shapes.Ellipse glowRing;
        readonly ScaleTransform ringScale;
        readonly Border statusPill;
        readonly TextBlock statusLabel;
        readonly TextBlock hintText;
        readonly Border topBar;
        readonly TextBlock topStatus;

        string translatedPath;
        EllipseGeometry revealClip;
        double imageWidth = 1;
        double imageHeight = 1;
        DispatcherTimer ringTimer;

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
            ShowActivated = true;

            double dpi = DpiScale();
            Left = physicalBounds.Left / dpi;
            Top = physicalBounds.Top / dpi;
            Width = physicalBounds.Width / dpi;
            Height = physicalBounds.Height / dpi;

            root = new Grid();

            picture = new Image { Stretch = Stretch.Fill, Source = LoadImage(sourcePath) };
            imageWidth = picture.Source != null ? ((BitmapSource)picture.Source).PixelWidth : 1;
            imageHeight = picture.Source != null ? ((BitmapSource)picture.Source).PixelHeight : 1;
            root.Children.Add(picture);

            // 扫描光斑：一个发光圆环，跟着"正在翻译的那一行"移动
            ringLayer = new Canvas { IsHitTestVisible = false };
            ringScale = new ScaleTransform(1, 1);
            glowRing = new System.Windows.Shapes.Ellipse
            {
                Width = 150,
                Height = 150,
                Opacity = 0,
                IsHitTestVisible = false,
                StrokeThickness = 2,
                Stroke = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(230, 150, 200, 255), 0),
                        new GradientStop(Color.FromArgb(120, 220, 180, 255), 1)
                    },
                    new Point(0, 0), new Point(1, 1)),
                Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(26, 190, 220, 255), 0.62),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                    })
            };
            glowRing.RenderTransform = ringScale;
            glowRing.RenderTransformOrigin = new Point(0.5, 0.5);
            ringLayer.Children.Add(glowRing);
            root.Children.Add(ringLayer);

            // 顶部条
            topStatus = new TextBlock
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
            barRow.Children.Add(topStatus);
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
                Padding = new Thickness(18, 10, 10, 10),
                Background = new SolidColorBrush(Color.FromArgb(214, 18, 20, 28)),
                Child = barGrid,
                Opacity = 0
            };
            root.Children.Add(topBar);

            // 底部进度胶囊
            statusLabel = new TextBlock
            {
                Text = "屏幕翻译 · 正在准备",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 244))
            };
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
                Child = statusLabel
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
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
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
            StartRingPulse();
        }

        // 光斑缓慢呼吸
        void StartRingPulse()
        {
            ringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            ringTimer.Tick += delegate
            {
                Motion.AnimateDouble(ringScale, ScaleTransform.ScaleXProperty, 0.86, 1.06, 480, Motion.Enter, 0, null);
                Motion.AnimateDouble(ringScale, ScaleTransform.ScaleYProperty, 0.86, 1.06, 480, Motion.Enter, 0, null);
            };
            ringTimer.Start();
        }

        void SetStatus(string text)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (statusLabel != null) statusLabel.Text = text;
            }), DispatcherPriority.Background);
        }

        void MoveRing(ImageTranslationProgress progress)
        {
            if (progress == null || progress.Total <= 0) return;
            double scaleX = ActualWidth > 0 ? ActualWidth / Math.Max(1, imageWidth) : 1;
            double scaleY = ActualHeight > 0 ? ActualHeight / Math.Max(1, imageHeight) : 1;

            double centerX = (progress.X + progress.Width / 2) * scaleX;
            double centerY = (progress.Y + progress.Height / 2) * scaleY;

            Canvas.SetLeft(glowRing, centerX - glowRing.Width / 2);
            Canvas.SetTop(glowRing, centerY - glowRing.Height / 2);
            if (glowRing.Opacity < 0.5) Motion.AnimateDouble(glowRing, UIElement.OpacityProperty, glowRing.Opacity, 1, 200, Motion.Enter, 0, null);
        }

        void BeginTranslate()
        {
            string path = sourcePath;
            Task.Run(delegate
            {
                try
                {
                    ImageTranslationResult result = ImageTranslator.Translate(path,
                        delegate(string message) { SetStatus("屏幕翻译 · " + message); },
                        delegate(ImageTranslationProgress progress)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { ApplyProgress(progress); }), DispatcherPriority.Background);
                        });

                    Dispatcher.BeginInvoke(new Action(delegate { ShowResult(result); }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (ringTimer != null) ringTimer.Stop();
                        glowRing.Opacity = 0;
                        topStatus.Text = "翻译失败：" + message;
                        hintText.Text = "按 Esc 关闭";
                    }), DispatcherPriority.Background);
                }
            });
        }

        // 每翻好一批：换成"已经部分翻译"的图，并把光斑移到当前行
        void ApplyProgress(ImageTranslationProgress progress)
        {
            if (progress == null) return;
            try
            {
                if (!string.IsNullOrEmpty(progress.ImagePath) && File.Exists(progress.ImagePath))
                {
                    picture.Source = LoadImage(progress.ImagePath);
                }
                MoveRing(progress);
                statusLabel.Text = "屏幕翻译 · 正在翻译 " + progress.Done + " / " + progress.Total + " 行";
                topStatus.Text = "正在翻译 " + progress.Done + " / " + progress.Total + " 行…";
            }
            catch (Exception) { }
        }

        void ShowResult(ImageTranslationResult result)
        {
            translatedPath = result.OutputPath;
            if (!string.IsNullOrEmpty(translatedPath) && File.Exists(translatedPath))
            {
                picture.Source = LoadImage(translatedPath);
            }

            if (ringTimer != null) ringTimer.Stop();
            Motion.AnimateDouble(glowRing, UIElement.OpacityProperty, glowRing.Opacity, 0, 320, Motion.Exit, 0, null);

            topStatus.Text = "已翻译 " + result.TranslatedCount + " 行";
            hintText.Text = "Esc 或右上角 ✕ 关闭 · 右键可保存图片";
            Motion.AnimateDouble(statusPill, UIElement.OpacityProperty, 1, 0, 260, Motion.Exit, 1400, null);
        }

        void SaveTranslated()
        {
            if (string.IsNullOrEmpty(translatedPath) || !File.Exists(translatedPath))
            {
                topStatus.Text = "还没有翻译结果";
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
                    topStatus.Text = "已保存：" + dialog.FileName;
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
                topStatus.Text = "翻译图片已复制";
            }
            catch (Exception) { }
        }
    }
}
