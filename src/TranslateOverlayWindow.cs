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
        readonly ScaleTransform ringScale;
        readonly Border statusPill;
        readonly TextBlock statusLabel;
        readonly TextBlock hintText;
        readonly Border topBar;
        readonly TextBlock topStatus;

        string translatedPath;
        Canvas rippleLayer;
        System.Windows.Shapes.Ellipse rippleBloom;
        System.Windows.Shapes.Rectangle veil;
        Canvas selectLayer;
        System.Windows.Shapes.Rectangle selectRect;
        Canvas resultLayer;
        Point dragStart;
        bool dragging;
        ScaleTransform rippleScale;
        System.Windows.Shapes.Rectangle fog;
        System.Windows.Media.Effects.BlurEffect pictureBlur;
        EllipseGeometry revealClip;
        double imageWidth = 1;
        double imageHeight = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        const int GwlExStyle = -20;
        const int WsExTransparent = 0x00000020;
        const int WsExNoActivate = 0x08000000;

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

            picture = new Image { Stretch = Stretch.Fill, Source = LoadImage(sourcePath), Opacity = 0 };
            imageWidth = picture.Source != null ? ((BitmapSource)picture.Source).PixelWidth : 1;
            imageHeight = picture.Source != null ? ((BitmapSource)picture.Source).PixelHeight : 1;
            // 常驻薄雾：低透明度彩虹，从扩散结束一直盖到翻译完成
            veil = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Opacity = 0,
                Fill = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(64, 200, 172, 255), 0),
                        new GradientStop(Color.FromArgb(58, 180, 162, 255), 0.26),
                        new GradientStop(Color.FromArgb(56, 160, 188, 255), 0.52),
                        new GradientStop(Color.FromArgb(60, 176, 156, 255), 0.78),
                        new GradientStop(Color.FromArgb(64, 214, 174, 255), 1)
                    },
                    new Point(0, 0), new Point(1, 1))
            };            root.Children.Add(veil);

            rippleLayer = new Canvas { IsHitTestVisible = false };
            rippleScale = new ScaleTransform(0.1, 0.1);
            rippleBloom = new System.Windows.Shapes.Ellipse
            {
                Opacity = 0.85,
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(120, 196, 170, 255), 0.34),
                        new GradientStop(Color.FromArgb(130, 174, 160, 255), 0.52),
                        new GradientStop(Color.FromArgb(120, 160, 190, 255), 0.74),
                        new GradientStop(Color.FromArgb(0, 210, 170, 255), 1)
                    })
            };            rippleBloom.RenderTransform = rippleScale;
            rippleBloom.RenderTransformOrigin = new Point(0.5, 0.5);
            rippleLayer.Children.Add(rippleBloom);
            root.Children.Add(rippleLayer);

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

        // 唤醒动效：屏幕中心向外扩散的彩虹光环，边缘柔化发光，扩散中淡出。
        // 动画期间窗口点透（不挡底层界面），光效结束后再开始翻译。
        void StartReveal()
        {
            SetClickThrough(true);

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            double diagonal = Math.Sqrt(width * width + height * height);
            double diameter = diagonal * 0.42;

            double centerX = width / 2;
            double centerY = height / 2;

            rippleLayer.Width = width;
            rippleLayer.Height = height;

            foreach (var ring in new[] { rippleBloom })
            {
                ring.Width = diameter;
                ring.Height = diameter;
                Canvas.SetLeft(ring, centerX - diameter / 2);
                Canvas.SetTop(ring, centerY - diameter / 2);
                ring.Opacity = 1;
            }

            // 光圈由小放大
            Motion.AnimateDouble(rippleScale, ScaleTransform.ScaleXProperty, 0.06, 3.30, 1200, Motion.Enter, 0, null);
            Motion.AnimateDouble(rippleScale, ScaleTransform.ScaleYProperty, 0.06, 3.30, 1200, Motion.Enter, 0, null);

            // 220ms 后画面淡入（透过光晕出现）
            var fadeIn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            fadeIn.Tick += delegate
            {
                fadeIn.Stop();
                Motion.AnimateDouble(picture, UIElement.OpacityProperty, 0, 1, 520, Motion.Enter, 0, null);
            };
            fadeIn.Start();

            // 650ms 后光晕开始淡出 + 顶部条出现
            var fadeOut = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            fadeOut.Tick += delegate
            {
                fadeOut.Stop();
                Motion.AnimateDouble(topBar, UIElement.OpacityProperty, 0, 1, 260, Motion.Enter, 0, null);
            };
            fadeOut.Start();

            // 1300ms 后收尾：移除光效、恢复点中、开始翻译
            var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1360) };
            finish.Tick += delegate
            {
                finish.Stop();
                // 确保扩散层彻底清掉，只留常驻薄雾
                if (rippleBloom != null) rippleBloom.Opacity = 0;
                if (rippleLayer != null)
                {
                    rippleLayer.Visibility = Visibility.Collapsed;
                    rippleLayer.Children.Clear();
                }
                Motion.AnimateDouble(veil, UIElement.OpacityProperty, 0, 1, 420, Motion.Enter, 0, null);
                SetClickThrough(false);
                BeginSelection();
            };
            finish.Start();
        }

        void SetClickThrough(bool enabled)
        {
            try
            {
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                int style = GetWindowLong(handle, GwlExStyle);
                if (enabled) style |= WsExTransparent | WsExNoActivate;
                else style &= ~WsExTransparent;
                SetWindowLong(handle, GwlExStyle, style);
            }
            catch (Exception) { }
        }
        void SetStatus(string text)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (statusLabel != null) statusLabel.Text = text;
            }), DispatcherPriority.Background);
        }

        static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screen-translate");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "overlay.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        // ---------- 框选区域翻译 ----------
        void BeginSelection()
        {
            Log("BeginSelection");
            resultLayer = new Canvas();
            root.Children.Add(resultLayer);

            selectLayer = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.Cross };
            selectRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(140, 190, 255)),
                StrokeThickness = 1.6,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 140, 190, 255)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            selectLayer.Children.Add(selectRect);
            root.Children.Add(selectLayer);

            selectLayer.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                dragging = true;
                dragStart = e.GetPosition(selectLayer);
                Canvas.SetLeft(selectRect, dragStart.X);
                Canvas.SetTop(selectRect, dragStart.Y);
                selectRect.Width = 0;
                selectRect.Height = 0;
                selectRect.Visibility = Visibility.Visible;
            };
            selectLayer.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!dragging) return;
                Point now = e.GetPosition(selectLayer);
                Canvas.SetLeft(selectRect, Math.Min(dragStart.X, now.X));
                Canvas.SetTop(selectRect, Math.Min(dragStart.Y, now.Y));
                selectRect.Width = Math.Abs(now.X - dragStart.X);
                selectRect.Height = Math.Abs(now.Y - dragStart.Y);
            };
            selectLayer.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false;
                var rect = new Rect(Canvas.GetLeft(selectRect), Canvas.GetTop(selectRect), selectRect.Width, selectRect.Height);
                selectRect.Visibility = Visibility.Collapsed;
                if (rect.Width < 24 || rect.Height < 18) return;
                TranslateRegion(rect);
            };

            topStatus.Text = "框选要翻译的区域";
            hintText.Text = "按住左键拖一个框 · Esc 关闭";
        }

        void TranslateRegion(Rect rect)
        {
            Log("TranslateRegion " + rect);
            // 屏幕坐标（DIP）→ 原图像素
            double scaleX = imageWidth / Math.Max(1, ActualWidth);
            double scaleY = imageHeight / Math.Max(1, ActualHeight);
            int sx = (int)Math.Max(0, rect.X * scaleX);
            int sy = (int)Math.Max(0, rect.Y * scaleY);
            int sw = (int)Math.Min(imageWidth - sx, rect.Width * scaleX);
            int sh = (int)Math.Min(imageHeight - sy, rect.Height * scaleY);
            if (sw < 8 || sh < 8) return;

            string cropPath = "";
            try
            {
                using (var full = new System.Drawing.Bitmap(sourcePath))
                using (var crop = full.Clone(new System.Drawing.Rectangle(sx, sy, sw, sh), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screen-translate");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    cropPath = Path.Combine(folder, "crop-" + DateTime.Now.ToString("HHmmss-fff") + ".png");
                    crop.Save(cropPath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                topStatus.Text = "截取失败：" + ex.Message;
                return;
            }

            topStatus.Text = "正在识别该区域…";
            var piece = new Image { Stretch = Stretch.Fill, Width = rect.Width, Height = rect.Height, Source = LoadImage(cropPath) };
            Canvas.SetLeft(piece, rect.X);
            Canvas.SetTop(piece, rect.Y);
            resultLayer.Children.Add(piece);

            string path = cropPath;
            Task.Run(delegate
            {
                try
                {
                    ImageTranslationResult result = ImageTranslator.Translate(path,
                        delegate(string message) { SetStatus("局部翻译 · " + message); },
                        null);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath))
                        {
                            piece.Source = LoadImage(result.OutputPath);
                        }
                        topStatus.Text = "该区域已翻译 " + result.TranslatedCount + " 行 · 引擎：" + TranslateService.EngineLabel()
                                         + (result.UnchangedCount > 0 ? " · " + result.UnchangedCount + " 行未变" : "");
                        hintText.Text = "可以继续框选其他区域 · Esc 关闭";
                        Motion.AnimateDouble(veil, UIElement.OpacityProperty, veil.Opacity, 0, 360, Motion.Exit, 0, null);
                    }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        piece.Visibility = Visibility.Collapsed;
                        topStatus.Text = "该区域翻译失败：" + message;
                    }), DispatcherPriority.Background);
                }
            });
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
                statusLabel.Text = "屏幕翻译 · 正在翻译 " + progress.Done + " / " + progress.Total + " 行";
                topStatus.Text = "正在翻译 " + progress.Done + " / " + progress.Total + " 行…";
            }
            catch (Exception) { }
        }

        void ShowResult(ImageTranslationResult result)
        {
            translatedPath = result.OutputPath;
            Motion.AnimateDouble(veil, UIElement.OpacityProperty, veil.Opacity, 0, 460, Motion.Exit, 0, null);
            if (!string.IsNullOrEmpty(translatedPath) && File.Exists(translatedPath))
            {
                picture.Source = LoadImage(translatedPath);
            }


            int unchanged = result.UnchangedCount;
            if (unchanged > 0 && unchanged * 3 >= result.TranslatedCount * 2)
            {
                // 大部分行没替换：基本是免费接口限流，明确提示而不是假装成功
                topStatus.Text = "翻译服务限流：" + unchanged + " / " + result.TranslatedCount + " 行未能替换，稍后重试";
            }
            else
            {
                topStatus.Text = "已翻译 " + result.TranslatedCount + " 行 · 引擎：" + TranslateService.EngineLabel() + (unchanged > 0 ? " · " + unchanged + " 行未变" : "");
            }
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
