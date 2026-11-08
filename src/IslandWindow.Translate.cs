using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using System.Windows.Threading;

namespace IslandShot
{
    // 翻译页签：左边输入，中间显示译文，右边滑出历史抽屉。
    // 引擎见 TranslateService（MyMemory 免费接口）。
    internal sealed partial class IslandWindow
    {
        static readonly Color TrPanelBg = Color.FromRgb(19, 16, 26);
        static readonly Color TrCardBg = Color.FromRgb(28, 24, 38);
        static readonly Color TrLine = Color.FromRgb(48, 41, 66);
        static readonly Color TrText = Color.FromRgb(232, 238, 248);
        static readonly Color TrMuted = Color.FromRgb(150, 156, 178);
        static readonly Color TrAccentA = Color.FromRgb(124, 108, 242);
        static readonly Color TrAccentB = Color.FromRgb(96, 148, 246);
        const double TrDrawerWidth = 258;
        const double TrStripWidth = 20;

        Grid translateView;
        TextBox translateInput;
        TextBlock translatePlaceholder;
        StackPanel translateHistoryList;
        ScrollViewer translateHistoryScroll;
        ScrollViewer translateResultScroll;
        TextBlock translateResultText;
        TextBlock translateResultSource;
        TextBlock translateResultPlaceholder;
        Button translateButton;
        Button translateCopyButton;
        TextBlock translateStatus;
        Button translateDirButton;
        Border translateHistoryDrawer;
        TranslateTransform translateHistoryShift;
        TextBlock translateDrawerGlyph;
        bool translateDrawerOpen;
        Grid translateImageLayer;
        Image translatePreview;
        TextBlock translateImageStatus;
        string translateImageOutput;
        bool translateBusy;
        bool translateForceZhEn;

        static string TranslateStorePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "translate.txt"); }
        }

        // 和 AI 面板一致的细圆角滚动条（轨道透明，滑块悬停变亮）
        static Style BuildTranslateScrollBarStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='8'/>" +
                "<Setter Property='MinWidth' Value='8'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>" +
                "<Track.Thumb><Thumb Width='6' HorizontalAlignment='Center'>" +
                "<Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border x:Name='bar' CornerRadius='3' Background='#2EFFFFFF'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bar' Property='Background' Value='#66FFFFFF'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='bar' Property='Background' Value='#A0FFFFFF'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template>" +
                "</Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        static Button TrButton(string text, bool primary, Action click)
        {
            var button = new Button
            {
                Content = text,
                FontSize = primary ? 12.5 : 11.5,
                Cursor = Cursors.Hand,
                Foreground = primary ? Brushes.White : new SolidColorBrush(TrMuted),
                Background = primary
                    ? (Brush)new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(TrAccentA, 0),
                            new GradientStop(TrAccentB, 1)
                        },
                        new Point(0, 0), new Point(1, 1))
                    : new SolidColorBrush(Color.FromRgb(38, 33, 52)),
                BorderThickness = new Thickness(0),
                Focusable = false
            };

            string margin = primary ? "14,7" : "12,5";
            button.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='9' BorderThickness='1' BorderBrush='#2A2338'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='" + margin + "'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='0.82'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='bg' Property='Opacity' Value='0.55'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");

            if (click != null) button.Click += delegate { click(); };
            return button;
        }

        Grid BuildTranslateView()
        {
            var root = new Grid { Background = new SolidColorBrush(TrPanelBg), Visibility = Visibility.Collapsed };
            // 在视图根上统一挂滚动条样式：输入框内部的 ScrollViewer 也能继承到
            root.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid left = BuildTranslateInputColumn();
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            var divider = new Border
            {
                Width = 1,
                Background = new SolidColorBrush(TrLine),
                Margin = new Thickness(0, 10, 0, 10)
            };
            Grid.SetColumn(divider, 1);
            root.Children.Add(divider);

            var right = new Grid { ClipToBounds = true };
            right.Children.Add(BuildTranslateResultArea());

            translateHistoryDrawer = BuildTranslateHistoryDrawer();
            right.Children.Add(translateHistoryDrawer);

            var strip = BuildTranslateDrawerStrip();
            right.Children.Add(strip);

            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            translateView = root;
            LoadTranslateHistory();
            return root;
        }

        static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null) return typed;
                T deeper = FindVisualChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // ---------------- 屏幕翻译（热键触发） ----------------
        void StartScreenTranslate()
        {
            try
            {
                System.Drawing.Rectangle bounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screen-translate");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string shot = Path.Combine(folder, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                using (System.Drawing.Bitmap bitmap = ScreenCapture.CaptureBitmap(bounds))
                {
                    if (bitmap == null) return;
                    bitmap.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                }

                // 扩张动画的起点：胶囊中心（屏幕坐标，DIP）
                double cx = Left + Width / 2;
                double cy = Top + capsule.ActualHeight;
                Rect capsuleRect = ScreenRectOf(capsule);
                if (!capsuleRect.IsEmpty) { cx = capsuleRect.Left + capsuleRect.Width / 2; cy = capsuleRect.Top + capsuleRect.Height / 2; }

                var overlay = new TranslateOverlayWindow(shot, bounds, new Point(cx, cy));
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception) { }
        }

        // ---------------- 左：输入 ----------------
        Grid BuildTranslateInputColumn()
        {
            var left = new Grid { Margin = new Thickness(16, 12, 14, 12) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            topRow.Children.Add(new TextBlock
            {
                Text = "原文",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            translateDirButton = TrButton("自动识别", false, delegate { ToggleTranslateDirection(); });
            translateDirButton.FontSize = 10.5;
            translateDirButton.ToolTip = "点一下切换翻译方向";
            topRow.Children.Add(translateDirButton);
            Grid.SetRow(topRow, 0);
            left.Children.Add(topRow);

            var inputBorder = new Border
            {
                Margin = new Thickness(0, 8, 0, 10),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(25, 21, 34)),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1)
            };

            var inputHost = new Grid();
            translateInput = new TextBox
            {
                Margin = new Thickness(10, 8, 10, 8),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TrText),
                CaretBrush = new SolidColorBrush(TrText),
                FontSize = 13.5,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            translateInput.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            translateInput.Loaded += delegate
            {
                // TextBox 的内部滚动条是模板生成出来的，加载后再补一次样式
                ScrollViewer inner = FindVisualChild<ScrollViewer>(translateInput);
                if (inner != null) inner.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            };
            translateInput.TextChanged += delegate { UpdateTranslatePlaceholder(); };
            translateInput.PreviewKeyDown += TranslateInputKeyDown;
            translateInput.PreviewMouseWheel += TranslateWheel;
            inputHost.Children.Add(translateInput);

            translatePlaceholder = new TextBlock
            {
                Text = "粘贴或输入要翻译的内容，Ctrl+Enter 直接翻译",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                Margin = new Thickness(12, 10, 12, 0),
                IsHitTestVisible = false,
                TextWrapping = TextWrapping.Wrap
            };
            inputHost.Children.Add(translatePlaceholder);
            inputBorder.Child = inputHost;

            Grid.SetRow(inputBorder, 1);
            left.Children.Add(inputBorder);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            translateButton = TrButton("翻译", true, null);
            translateButton.Click += async delegate { await DoTranslate(); };
            buttonRow.Children.Add(translateButton);

            var clearInput = TrButton("清空", false, delegate
            {
                translateInput.Text = "";
                translateStatus.Text = "";
            });
            clearInput.Margin = new Thickness(10, 0, 0, 0);
            buttonRow.Children.Add(clearInput);
            Grid.SetRow(buttonRow, 2);
            left.Children.Add(buttonRow);

            translateStatus = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                Margin = new Thickness(0, 8, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(translateStatus, 3);
            left.Children.Add(translateStatus);

            // ---- 图片翻译层：盖住整个左栏 ----
            var imageLayer = new Grid
            {
                Background = new SolidColorBrush(TrPanelBg),
                Visibility = Visibility.Collapsed
            };
            imageLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            imageLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            imageLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imageHead = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            imageHead.Children.Add(new TextBlock
            {
                Text = "图片翻译",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            var backToText = TrButton("返回文字", false, delegate { ShowTranslateImageLayer(false); });
            backToText.FontSize = 10.5;
            imageHead.Children.Add(backToText);
            Grid.SetRow(imageHead, 0);
            imageLayer.Children.Add(imageHead);

            translatePreview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(6) };
            var previewHost = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(25, 21, 34)),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "单击放大预览 · 右键可保存",
                Child = translatePreview
            };
            previewHost.MouseLeftButtonUp += delegate
            {
                if (string.IsNullOrEmpty(translateImageOutput)) return;
                if (!File.Exists(translateImageOutput)) return;
                ShowFullImage(translateImageOutput);
            };
            var imageMenu = new ContextMenu();
            var saveItem = new MenuItem { Header = "保存图片" };
            saveItem.Click += delegate { SaveTranslatedImage(); };
            var copyItem = new MenuItem { Header = "复制图片到剪贴板" };
            copyItem.Click += delegate { CopyTranslatedImage(); };
            var openItem = new MenuItem { Header = "打开预览" };
            openItem.Click += delegate
            {
                if (!string.IsNullOrEmpty(translateImageOutput) && File.Exists(translateImageOutput))
                    ShowFullImage(translateImageOutput);
            };
            imageMenu.Items.Add(openItem);
            imageMenu.Items.Add(saveItem);
            imageMenu.Items.Add(copyItem);
            previewHost.ContextMenu = imageMenu;
            Grid.SetRow(previewHost, 1);
            imageLayer.Children.Add(previewHost);

            translateImageStatus = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(translateImageStatus, 2);
            imageLayer.Children.Add(translateImageStatus);

            Grid.SetRowSpan(imageLayer, 4);
            left.Children.Add(imageLayer);
            translateImageLayer = imageLayer;

            return left;
        }

        void ShowTranslateImageLayer(bool show)
        {
            if (translateImageLayer == null) return;
            translateImageLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        void TranslateImageAsync(string path)
        {
            SetTabTranslate();
            ShowTranslateImageLayer(true);
            translateImageOutput = null;
            if (translatePreview != null)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    translatePreview.Source = image;
                }
                catch (Exception) { }
            }
            if (translateImageStatus != null) translateImageStatus.Text = "正在翻译…（首次可能稍慢）";

            string sourcePath = path;
            Task.Run(delegate
            {
                try
                {
                    ImageTranslationResult result = ImageTranslator.Translate(sourcePath, delegate(string message)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (translateImageStatus != null) translateImageStatus.Text = message;
                        }), DispatcherPriority.Background);
                    });

                    Dispatcher.BeginInvoke(new Action(delegate { ShowImageTranslationResult(result); }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (translateImageStatus != null) translateImageStatus.Text = "翻译失败：" + message;
                    }), DispatcherPriority.Background);
                }
            });
        }

        void ShowImageTranslationResult(ImageTranslationResult result)
        {
            translateImageOutput = result.OutputPath;
            if (translatePreview != null && !string.IsNullOrEmpty(result.OutputPath))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(result.OutputPath, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    translatePreview.Source = image;
                }
                catch (Exception) { }
            }

            if (translateImageStatus != null)
            {
                translateImageStatus.Text = "已翻译 " + result.TranslatedCount + " 行 · 结果已保存到 data\\translate-img";
            }

            // 图片翻译只展示图片：右侧放一句说明，不铺文字
            SetTranslateResult("", "");
            if (translateResultPlaceholder != null)
            {
                translateResultPlaceholder.Text = "图片已翻译\n单击图片可放大 · 右键可保存";
                translateResultPlaceholder.Visibility = Visibility.Visible;
            }
        }

        void SaveTranslatedImage()
        {
            if (string.IsNullOrEmpty(translateImageOutput) || !File.Exists(translateImageOutput))
            {
                if (translateImageStatus != null) translateImageStatus.Text = "还没有可保存的图片";
                return;
            }
            using (var dialog = new Forms.SaveFileDialog())
            {
                dialog.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg";
                dialog.FileName = Path.GetFileName(translateImageOutput);
                try { dialog.InitialDirectory = Path.GetDirectoryName(translateImageOutput); }
                catch (Exception) { }
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    File.Copy(translateImageOutput, dialog.FileName, true);
                    if (translateImageStatus != null) translateImageStatus.Text = "已保存到 " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    if (translateImageStatus != null) translateImageStatus.Text = "保存失败：" + ex.Message;
                }
            }
        }

        void CopyTranslatedImage()
        {
            if (string.IsNullOrEmpty(translateImageOutput) || !File.Exists(translateImageOutput)) return;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(translateImageOutput, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                Clipboard.SetImage(image);
                if (translateImageStatus != null) translateImageStatus.Text = "图片已复制到剪贴板";
            }
            catch (Exception) { }
        }

        // ---------------- 右：译文 ----------------
        Grid BuildTranslateResultArea()
        {
            var area = new Grid { Margin = new Thickness(16, 12, 20, 12) };
            area.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            area.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock
            {
                Text = "译文",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center
            });
            translateCopyButton = TrButton("复制", false, delegate { CopyTranslateResult(); });
            translateCopyButton.HorizontalAlignment = HorizontalAlignment.Right;
            translateCopyButton.FontSize = 10.5;
            translateCopyButton.Visibility = Visibility.Collapsed;
            header.Children.Add(translateCopyButton);
            Grid.SetRow(header, 0);
            area.Children.Add(header);

            var body = new Grid();
            translateResultSource = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 172)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            translateResultText = new TextBlock
            {
                Text = "",
                FontSize = 15,
                LineHeight = 24,
                Foreground = new SolidColorBrush(TrText),
                TextWrapping = TextWrapping.Wrap
            };
            var resultStack = new StackPanel();
            resultStack.Children.Add(translateResultSource);
            resultStack.Children.Add(translateResultText);

            translateResultScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = resultStack,
                Focusable = false
            };
            translateResultScroll.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            translateResultScroll.PreviewMouseWheel += TranslateWheel;
            body.Children.Add(translateResultScroll);

            translateResultPlaceholder = new TextBlock
            {
                Text = "译文会显示在这里\n左边输入内容后点「翻译」",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(104, 108, 130)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };
            body.Children.Add(translateResultPlaceholder);

            Grid.SetRow(body, 1);
            area.Children.Add(body);
            return area;
        }

        // ---------------- 右：历史抽屉 ----------------
        Border BuildTranslateHistoryDrawer()
        {
            var drawer = new Border
            {
                Width = TrDrawerWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 10, TrStripWidth, 10),
                CornerRadius = new CornerRadius(12, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(24, 20, 33)),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.45,
                    Color = Color.FromRgb(0, 0, 0)
                }
            };

            var grid = new Grid { Margin = new Thickness(12, 10, 10, 10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock
            {
                Text = "历史记录",
                FontSize = 11,
                Foreground = new SolidColorBrush(TrMuted),
                VerticalAlignment = VerticalAlignment.Center
            });
            var clearHistory = TrButton("清空", false, delegate { ClearTranslateHistory(); });
            clearHistory.HorizontalAlignment = HorizontalAlignment.Right;
            clearHistory.FontSize = 10;
            head.Children.Add(clearHistory);
            Grid.SetRow(head, 0);
            grid.Children.Add(head);

            translateHistoryList = new StackPanel();
            translateHistoryScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = translateHistoryList,
                Focusable = false
            };
            translateHistoryScroll.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            translateHistoryScroll.PreviewMouseWheel += TranslateWheel;
            Grid.SetRow(translateHistoryScroll, 1);
            grid.Children.Add(translateHistoryScroll);

            drawer.Child = grid;

            translateHistoryShift = new TranslateTransform(TrDrawerWidth + TrStripWidth, 0);
            drawer.RenderTransform = translateHistoryShift;
            return drawer;
        }

        Border BuildTranslateDrawerStrip()
        {
            var strip = new Border
            {
                Width = TrStripWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 14, 0, 14),
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                Background = new SolidColorBrush(Color.FromRgb(30, 26, 42)),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "展开/收起翻译历史"
            };
            Panel.SetZIndex(strip, 5);

            translateDrawerGlyph = new TextBlock
            {
                Text = "‹",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(168, 160, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            strip.Child = translateDrawerGlyph;
            strip.MouseLeftButtonUp += delegate { ToggleTranslateDrawer(); };
            return strip;
        }

        void ToggleTranslateDrawer()
        {
            translateDrawerOpen = !translateDrawerOpen;
            if (translateHistoryDrawer != null)
            {
                translateHistoryDrawer.IsHitTestVisible = translateDrawerOpen;
                double to = translateDrawerOpen ? 0 : TrDrawerWidth + TrStripWidth;
                if (translateHistoryShift != null)
                {
                    Motion.AnimateDouble(translateHistoryShift, TranslateTransform.XProperty,
                        translateHistoryShift.X, to, 220, Motion.Drop, 0, null);
                }
            }
            if (translateDrawerGlyph != null) translateDrawerGlyph.Text = translateDrawerOpen ? "›" : "‹";
            if (translateDrawerOpen) UpdateTranslatePlaceholder();
        }

        // ---------------- 滚轮 / 输入 ----------------
        void TranslateWheel(object sender, MouseWheelEventArgs e)
        {
            var scroll = sender as ScrollViewer;
            if (scroll == null) return;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 2.0);
            e.Handled = true;
        }

        void TranslateInputKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (e.Key == Key.Enter && control)
            {
                e.Handled = true;
                var ignored = DoTranslate();
            }
        }

        void UpdateTranslatePlaceholder()
        {
            if (translatePlaceholder == null) return;
            translatePlaceholder.Visibility = string.IsNullOrEmpty(translateInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        void ToggleTranslateDirection()
        {
            translateForceZhEn = !translateForceZhEn;
            translateDirButton.Content = translateForceZhEn ? "中 → 英" : "自动识别";
        }

        void SetTranslateResult(string source, string target)
        {
            if (translateResultSource != null)
            {
                translateResultSource.Text = source;
                translateResultSource.Visibility = string.IsNullOrEmpty(source) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (translateResultText != null) translateResultText.Text = target ?? "";
            if (translateResultPlaceholder != null)
            {
                translateResultPlaceholder.Visibility = string.IsNullOrEmpty(target) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (translateCopyButton != null)
            {
                translateCopyButton.Visibility = string.IsNullOrEmpty(target) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (translateResultScroll != null) translateResultScroll.ScrollToTop();
        }

        void CopyTranslateResult()
        {
            try
            {
                Clipboard.SetText(translateResultText.Text ?? "");
                translateStatus.Text = "译文已复制";
            }
            catch (Exception) { }
        }

        async Task DoTranslate()
        {
            if (translateBusy) return;
            string text = (translateInput.Text ?? "").Trim();
            if (text.Length == 0)
            {
                translateStatus.Text = "先输入内容";
                return;
            }
            if (text.Length > 1500)
            {
                translateStatus.Text = "一次最多翻译 1500 个字符";
                return;
            }

            string pair = TranslateService.PairFor(text, translateForceZhEn);
            translateBusy = true;
            translateButton.IsEnabled = false;
            translateButton.Content = "翻译中…";
            translateStatus.Text = "正在翻译…";

            try
            {
                string result = await TranslateService.TranslateAsync(text, pair);
                result = (result ?? "").Trim();
                if (result.Length == 0) throw new Exception("翻译结果为空");

                SetTranslateResult(text, result);
                translateStatus.Text = "已翻译为" + TranslateService.TargetLabel(pair);
                AddTranslateCard(text, result, pair);
                SaveTranslateHistory();
            }
            catch (Exception ex)
            {
                translateStatus.Text = "失败：" + ex.Message;
            }
            finally
            {
                translateBusy = false;
                translateButton.IsEnabled = true;
                translateButton.Content = "翻译";
            }
        }

        // ---------------- 历史 ----------------
        void AddTranslateCard(string source, string target, string pair)
        {
            if (translateHistoryList == null) return;

            var card = new Border
            {
                Background = new SolidColorBrush(TrCardBg),
                BorderBrush = new SolidColorBrush(TrLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 6),
                Margin = new Thickness(0, 0, 0, 7),
                Cursor = Cursors.Hand,
                ToolTip = "点一下把它显示到右边"
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = source,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 172)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = target,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(TrText),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 4),
                MaxHeight = 52,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm") + " · " + TranslateService.TargetLabel(pair),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 138))
            });

            card.Child = stack;
            card.MouseLeftButtonUp += delegate { SetTranslateResult(source, target); };
            translateHistoryList.Children.Insert(0, card);

            while (translateHistoryList.Children.Count > 30)
            {
                translateHistoryList.Children.RemoveAt(translateHistoryList.Children.Count - 1);
            }
            if (translateHistoryScroll != null) translateHistoryScroll.ScrollToTop();
        }

        void ClearTranslateHistory()
        {
            if (translateHistoryList != null) translateHistoryList.Children.Clear();
            try { if (File.Exists(TranslateStorePath)) File.Delete(TranslateStorePath); }
            catch (Exception) { }
            translateStatus.Text = "记录已清空";
        }

        void SaveTranslateHistory()
        {
            try
            {
                string folder = Path.GetDirectoryName(TranslateStorePath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var lines = new List<string>();
                if (translateHistoryList != null)
                {
                    foreach (object child in translateHistoryList.Children)
                    {
                        var card = child as Border;
                        if (card == null) continue;
                        var stack = card.Child as StackPanel;
                        if (stack == null || stack.Children.Count < 2) continue;
                        var sourceBlock = stack.Children[0] as TextBlock;
                        var targetBlock = stack.Children[1] as TextBlock;
                        if (sourceBlock == null || targetBlock == null) continue;

                        lines.Add(DateTime.Now.ToString("yyyyMMddHHmm") + "\t" +
                                  Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceBlock.Text)) + "\t" +
                                  Convert.ToBase64String(Encoding.UTF8.GetBytes(targetBlock.Text)));
                    }
                }
                File.WriteAllLines(TranslateStorePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        void LoadTranslateHistory()
        {
            try
            {
                if (!File.Exists(TranslateStorePath)) return;
                string[] lines = File.ReadAllLines(TranslateStorePath, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 30);
                for (int i = start; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length < 3) continue;
                    string source, target;
                    try
                    {
                        source = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                        target = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                    }
                    catch (Exception) { continue; }
                    AddTranslateCard(source, target, "zh-CN|en");
                }
            }
            catch (Exception) { }
        }
    }
}
