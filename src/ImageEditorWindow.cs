using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

namespace IslandShot
{
    // 图片编辑器：常用标注 + 裁剪 + 保存/另存为。
    // 设计参考 ShareX 编辑器的功能划分，但实现完全自写（不依赖它的代码）。
    internal sealed class ImageEditorWindow : Window
    {
        static readonly Color PanelColor = Color.FromRgb(20, 22, 30);
        static readonly Color BarColor = Color.FromRgb(26, 29, 38);
        static readonly Color LineColor = Color.FromRgb(44, 48, 60);
        static readonly Color TextColor = Color.FromRgb(226, 232, 244);
        static readonly Color MutedColor = Color.FromRgb(140, 150, 170);

        string sourcePath;
        readonly Action<string> savedCallback;
        readonly EditorSurface surface;
        readonly TextBox textInput;
        readonly StackPanel toolRow;
        readonly StackPanel propertyRow;
        readonly TextBlock statusText;
        readonly List<Button> toolButtons = new List<Button>();

        EditorTool currentTool = EditorTool.Select;
        bool textEditingNew;
        Rect textBoxRect;
        Color strokeColor = Color.FromRgb(255, 69, 58);
        Color fillColor = Color.FromArgb(0, 0, 0, 0);
        double thickness = 3;
        double opacity = 1;
        double fontSize = 22;
        int blurRadius = 6;
        int pixelBlock = 10;
        int stepStart = 1;
        Color highlightColor = Color.FromRgb(255, 226, 66);
        int highlightAlpha = 90;

        void ApplyHighlightColor()
        {
            Color fill = Color.FromArgb((byte)Math.Max(0, Math.Min(255, highlightAlpha)), highlightColor.R, highlightColor.G, highlightColor.B);
            surface.HighlightFill = fill;
            HighlightItem selected = surface.Selected as HighlightItem;
            if (selected != null) selected.Fill = fill;
            surface.InvalidateVisual();
        }

        public ImageEditorWindow(string path, Action<string> saved)
        {
            sourcePath = path;
            savedCallback = saved;

            Title = "图片编辑";
            Width = 1120;
            Height = 760;
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(PanelColor);
            Foreground = new SolidColorBrush(TextColor);
            WindowStyle = WindowStyle.SingleBorderWindow;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            toolRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            propertyRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            statusText = new TextBlock { Foreground = new SolidColorBrush(MutedColor), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

            surface = new EditorSurface();
            surface.Changed += UpdateStatus;
            surface.TextRequested += OnTextRequested;

            textInput = new TextBox
            {
                Visibility = Visibility.Collapsed,
                MinWidth = 160,
                FontSize = 18,
                Background = new SolidColorBrush(Color.FromRgb(34, 38, 48)),
                Foreground = new SolidColorBrush(TextColor),
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 170, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                AcceptsReturn = false
            };
            textInput.KeyDown += TextInputKeyDown;
            textInput.LostFocus += delegate { CommitTextInput(); };

            var canvasHost = new Grid();
            canvasHost.Children.Add(surface);
            var inputLayer = new Canvas { IsHitTestVisible = true };
            inputLayer.Children.Add(textInput);
            canvasHost.Children.Add(inputLayer);

            BuildToolbar();
            BuildPropertyBar();

            root.Children.Add(BuildHeader());
            root.Children.Add(BuildPropertyStrip());
            Grid.SetRow(canvasHost, 2);
            root.Children.Add(canvasHost);
            Grid.SetRow(canvasHost, 2);

            var status = new Border
            {
                Background = new SolidColorBrush(BarColor),
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Child = statusText
            };
            Grid.SetRow(status, 3);
            root.Children.Add(status);

            Content = root;

            surface.MouseWheel += OnSurfaceWheel;
            PreviewKeyDown += OnWindowKeyDown;

            LoadImage(path);
            Loaded += delegate { surface.ZoomToFit(); UpdateStatus(); };
        }

        // ------------------------------------------------------------ 外观
        UIElement BuildHeader()
        {
            var grid = new Grid { Height = 56, Background = new SolidColorBrush(BarColor) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logo = new TextBlock
            {
                Text = "图片编辑",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextColor),
                Margin = new Thickness(14, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(logo);

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = toolRow,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(BarButton("撤销", delegate { surface.Undo(); UpdateStatus(); }));
            actions.Children.Add(BarButton("重做", delegate { surface.Redo(); UpdateStatus(); }));
            actions.Children.Add(BarButton("复制", delegate { CopyToClipboard(); }));
            actions.Children.Add(BarButton("保存", delegate { Save(false); }));
            actions.Children.Add(BarButton("另存为", delegate { Save(true); }));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            var border = new Border
            {
                Background = new SolidColorBrush(BarColor),
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
            return border;
        }

        UIElement BuildPropertyStrip()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 26, 34)),
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8),
                Child = propertyRow
            };
            Grid.SetRow(border, 1);
            return border;
        }

        Button BarButton(string text, Action action)
        {
            var button = new Button
            {
                Content = text,
                FontSize = 12,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(38, 42, 54)),
                Foreground = new SolidColorBrush(TextColor),
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(1),
                FocusVisualStyle = null,
                Cursor = Cursors.Hand
            };
            button.Click += delegate { action(); };
            return button;
        }

        void BuildToolbar()
        {
            toolRow.Children.Clear();
            toolButtons.Clear();

            AddTool(EditorTool.Pan, "移动画布 (1)", "M8,2 V14 M2,8 H14 M5,5 L8,2 L11,5 M5,11 L8,14 L11,11 M5,5 L2,8 L5,11 M11,5 L14,8 L11,11", false);
            AddTool(EditorTool.Select, "选择 (V)", "M2,2 L2,15 L6,11 L9,16 L11,15 L8,10 L14,10 Z", true);
            AddTool(EditorTool.Pen, "画笔 (2)", "M3,13 C5,8 8,11 10,6 C11,4 12,3 13,2", false);
            AddTool(EditorTool.Rect, "矩形 (3)", "M2,4 H14 V12 H2 Z", true);
            AddTool(EditorTool.Ellipse, "椭圆 (4)", "M2,8 A6,4 0 1 0 14,8 A6,4 0 1 0 2,8", true);
            AddTool(EditorTool.Line, "直线 (5)", "M3,13 L13,3", false);
            AddTool(EditorTool.Arrow, "箭头 (6)", "M3,13 L13,3 M8,3 H13 V8", false);
            AddTool(EditorTool.Highlight, "高亮 (7)", "M4,10 H12 V13 H4 Z M6,3 H13 V9 H6 Z", true);
            AddTool(EditorTool.Text, "文字 (8)", "M4,4 H12 M8,4 V13", false);
            AddTool(EditorTool.Bubble, "气泡 (9)", "M3,4 H13 V10 H7 L5,13 V10 H3 Z", true);
            AddTool(EditorTool.Step, "步骤 (0)", "M8,2 A6,6 0 1 1 7.9,2", true);
            AddTool(EditorTool.Blur, "模糊 (-)", "M3,8 C5,4 9,4 11,8 C9,12 5,12 3,8", true);
            AddTool(EditorTool.Pixelate, "马赛克 (=)", "M3,3 H7 V7 H3 Z M9,3 H13 V7 H9 Z M3,9 H7 V13 H3 Z M9,9 H13 V13 H9 Z", true);
            AddTool(EditorTool.Crop, "裁剪 (C)", "M5,2 V13 H16 M2,5 H13 V16", false);
            AddTool(EditorTool.Pan, "移动画布", "M8,2 V14 M2,8 H14 M5,5 L8,2 L11,5 M5,11 L8,14 L11,11 M5,5 L2,8 L5,11 M11,5 L14,8 L11,11", false);

            toolRow.Children.Add(Separator());

            var fit = BarButton("适合窗口", delegate { surface.ZoomToFit(); UpdateStatus(); });
            toolRow.Children.Add(fit);
            var zoomIn = BarButton("放大", delegate { Zoom(1.2); });
            toolRow.Children.Add(zoomIn);
            var zoomOut = BarButton("缩小", delegate { Zoom(1 / 1.2); });
            toolRow.Children.Add(zoomOut);

            SelectTool(EditorTool.Select);
        }

        FrameworkElement Separator()
        {
            return new Border
            {
                Width = 1,
                Height = 22,
                Margin = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(LineColor),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        void AddTool(EditorTool tool, string tip, string geometry, bool filled)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry),
                Stroke = new SolidColorBrush(Color.FromRgb(214, 222, 236)),
                StrokeThickness = 1.4,
                Fill = filled ? new SolidColorBrush(Color.FromArgb(70, 214, 222, 236)) : null,
                Stretch = Stretch.Uniform,
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                ToolTip = tip,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Content = icon,
                Tag = tool
            };
            button.Click += delegate
            {
                SelectTool(tool);
            };
            toolButtons.Add(button);
            toolRow.Children.Add(button);
        }

        void SelectTool(EditorTool tool)
        {
            currentTool = tool;
            surface.Tool = tool;
            if (tool != EditorTool.Crop) surface.CancelCrop();
            surface.Cursor = tool == EditorTool.Pan ? Cursors.Hand : (tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross);

            foreach (Button button in toolButtons)
            {
                bool active = button.Tag is EditorTool && (EditorTool)button.Tag == tool;
                button.Background = active
                    ? new SolidColorBrush(Color.FromRgb(58, 96, 160))
                    : Brushes.Transparent;
                button.BorderBrush = active
                    ? new SolidColorBrush(Color.FromRgb(90, 170, 255))
                    : Brushes.Transparent;
            }
            BuildPropertyBar();
            UpdateStatus();
        }

        void BuildPropertyBar()
        {
            propertyRow.Children.Clear();

            if (currentTool == EditorTool.Highlight)
            {
                // 高亮是半透明色块，单独给一个颜色 + 浓度，避免和描边色混淆
                propertyRow.Children.Add(Label("高亮颜色"));
                AddColorSwatches(highlightColor, delegate(Color color)
                {
                    highlightColor = color;
                    ApplyHighlightColor();
                    BuildPropertyBar();
                });

                propertyRow.Children.Add(Label("浓度", 10));
                AddSlider(20, 180, highlightAlpha, delegate(double value)
                {
                    highlightAlpha = (int)Math.Round(value);
                    ApplyHighlightColor();
                });
                propertyRow.Children.Add(Label("（拖出一块区域即可高亮）", 10));
                return;
            }

            propertyRow.Children.Add(Label("颜色"));
            AddColorSwatches(strokeColor, delegate(Color color)
            {
                strokeColor = color;
                surface.DefaultStroke = color;      // 关键：新画的对象也用这个颜色
                ApplyToSelected(delegate(EditorItem item) { item.Stroke = color; });
                BuildPropertyBar();
            });

            propertyRow.Children.Add(Label("填充", 10));
            AddColorSwatches(fillColor, delegate(Color color)
            {
                fillColor = color;
                surface.DefaultFill = color;
                ApplyToSelected(delegate(EditorItem item) { item.Fill = color; });
                BuildPropertyBar();
            });
            var transparent = BarButton("无", delegate
            {
                fillColor = Color.FromArgb(0, 0, 0, 0);
                surface.DefaultFill = fillColor;
                ApplyToSelected(delegate(EditorItem item) { item.Fill = Color.FromArgb(0, 0, 0, 0); });
            });
            propertyRow.Children.Add(transparent);

            propertyRow.Children.Add(Label("粗细", 10));
            AddSlider(1, 24, thickness, delegate(double value)
            {
                thickness = value;
                surface.DefaultThickness = value;   // 关键：新画的对象也用这个粗细
                ApplyToSelected(delegate(EditorItem item) { item.Thickness = value; });
            });

            propertyRow.Children.Add(Label("透明度", 10));
            AddSlider(5, 100, opacity * 100, delegate(double value)
            {
                opacity = value / 100.0;
                surface.DefaultOpacity = opacity;   // 关键：新画的对象也用这个透明度
                ApplyToSelected(delegate(EditorItem item) { item.Opacity = opacity; });
            });

            bool textLike = currentTool == EditorTool.Text || surface.Selected is TextItem;
            if (textLike)
            {
                propertyRow.Children.Add(Label("字号", 10));
                AddSlider(8, 72, fontSize, delegate(double value)
                {
                    fontSize = value;
                    surface.DefaultFontSize = value;
                    TextItem item = surface.Selected as TextItem;
                    if (item != null) { item.FontSize = value; item.Layout(); surface.InvalidateVisual(); }
                });
            }

            if (currentTool == EditorTool.Blur)
            {
                propertyRow.Children.Add(Label("模糊程度", 10));
                AddSlider(1, 20, blurRadius, delegate(double value) { blurRadius = (int)Math.Round(value); surface.BlurRadius = blurRadius; });
            }

            if (currentTool == EditorTool.Pixelate)
            {
                propertyRow.Children.Add(Label("马赛克块", 10));
                AddSlider(2, 40, pixelBlock, delegate(double value) { pixelBlock = (int)Math.Round(value); surface.PixelBlock = pixelBlock; });
            }

            if (currentTool == EditorTool.Step)
            {
                propertyRow.Children.Add(Label("起始编号", 10));
                AddSlider(1, 30, stepStart, delegate(double value) { stepStart = (int)Math.Round(value); surface.NextStepNumber = stepStart; });
            }

            if (currentTool == EditorTool.Crop)
            {
                propertyRow.Children.Add(BarButton("应用裁剪", delegate { surface.ApplyCrop(); UpdateStatus(); }));
                propertyRow.Children.Add(BarButton("取消裁剪", delegate { surface.CancelCrop(); UpdateStatus(); }));
            }

            if (surface.Selected != null)
            {
                propertyRow.Children.Add(Label("图层", 10));
                propertyRow.Children.Add(BarButton("上移", delegate { surface.MoveLayer(1); }));
                propertyRow.Children.Add(BarButton("下移", delegate { surface.MoveLayer(-1); }));
                propertyRow.Children.Add(BarButton("置顶", delegate { surface.MoveLayer(1000); }));
                propertyRow.Children.Add(BarButton("置底", delegate { surface.MoveLayer(-1000); }));
                propertyRow.Children.Add(BarButton("删除", delegate { surface.DeleteSelected(); UpdateStatus(); }));
            }

            propertyRow.Children.Add(Label("图片", 10));
            propertyRow.Children.Add(BarButton("旋转 90°", delegate { TransformImage(delegate(BitmapSource image) { return ImageEffects.Rotate90(image, true); }); }));
            propertyRow.Children.Add(BarButton("水平翻转", delegate { TransformImage(delegate(BitmapSource image) { return ImageEffects.Flip(image, true); }); }));
            propertyRow.Children.Add(BarButton("灰度", delegate { TransformImage(ImageEffects.Grayscale); }));
            propertyRow.Children.Add(BarButton("反相", delegate { TransformImage(ImageEffects.Invert); }));
            propertyRow.Children.Add(BarButton("棕褐", delegate { TransformImage(ImageEffects.Sepia); }));
            propertyRow.Children.Add(BarButton("锐化", delegate { TransformImage(delegate(BitmapSource image) { return ImageEffects.Sharpen(image, 45); }); }));
            propertyRow.Children.Add(BarButton("暗角", delegate { TransformImage(delegate(BitmapSource image) { return ImageEffects.Vignette(image, 45); }); }));
            propertyRow.Children.Add(BarButton("加留白", delegate { AddPadding(); }));
            propertyRow.Children.Add(BarButton("调整大小", delegate { ResizeImage(); }));
        }

        TextBlock Label(string text, double leftMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(leftMargin, 0, 6, 0)
            };
        }

        void AddColorSwatches(Color current, Action<Color> pick)
        {
            Color[] palette = new Color[]
            {
                Color.FromRgb(255, 69, 58), Color.FromRgb(255, 179, 64), Color.FromRgb(255, 226, 66),
                Color.FromRgb(88, 232, 156), Color.FromRgb(64, 200, 255), Color.FromRgb(90, 170, 255),
                Color.FromRgb(179, 157, 255), Color.FromRgb(255, 255, 255), Color.FromRgb(150, 158, 175),
                Color.FromRgb(24, 26, 32)
            };

            foreach (Color color in palette)
            {
                bool active = color == current;
                var swatch = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(active ? Color.FromRgb(90, 170, 255) : LineColor),
                    BorderThickness = new Thickness(active ? 2 : 1),
                    Cursor = Cursors.Hand
                };
                Color captured = color;
                swatch.MouseLeftButtonUp += delegate { pick(captured); };
                propertyRow.Children.Add(swatch);
            }

            var custom = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(38, 42, 54)),
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "自定义颜色"
            };
            custom.MouseLeftButtonUp += delegate
            {
                using (var dialog = new Forms.ColorDialog())
                {
                    dialog.FullOpen = true;
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        pick(Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B));
                    }
                }
            };
            propertyRow.Children.Add(custom);
        }

        void AddSlider(double min, double max, double value, Action<double> changed)
        {
            var slider = new Slider
            {
                Width = 92,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e) { changed(e.NewValue); };
            propertyRow.Children.Add(slider);
        }

        void ApplyToSelected(Action<EditorItem> action)
        {
            if (surface.Selected == null) return;
            action(surface.Selected);
            surface.InvalidateVisual();
        }

        // ------------------------------------------------------------ 图片
        void LoadImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                surface.BaseImage = bitmap;
                surface.ZoomToFit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开图片：" + ex.Message, "图片编辑");
            }
        }

        void TransformImage(Func<BitmapSource, BitmapSource> transform)
        {
            if (surface.BaseImage == null) return;
            surface.PushUndo();
            try
            {
                surface.BaseImage = transform(surface.BaseImage);
                surface.ZoomToFit();
                surface.InvalidateVisual();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "处理失败：" + ex.Message, "图片编辑");
            }
        }

        void AddPadding()
        {
            if (surface.BaseImage == null) return;
            surface.PushUndo();
            surface.BaseImage = ImageEffects.AddBackground(surface.BaseImage, Color.FromRgb(255, 255, 255), 24, 10);
            surface.ZoomToFit();
            surface.InvalidateVisual();
            UpdateStatus();
        }

        void ResizeImage()
        {
            if (surface.BaseImage == null) return;
            var dialog = new ResizeDialog(surface.BaseImage.PixelWidth, surface.BaseImage.PixelHeight) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            surface.PushUndo();
            surface.BaseImage = ImageEffects.Resize(surface.BaseImage, dialog.TargetWidth, dialog.TargetHeight);
            surface.ZoomToFit();
            surface.InvalidateVisual();
            UpdateStatus();
        }

        void Zoom(double factor)
        {
            Point center = new Point(surface.ActualWidth / 2, surface.ActualHeight / 2);
            Point before = surface.ToImage(center);
            surface.Scale = Math.Max(0.05, Math.Min(8, surface.Scale * factor));
            // 让缩放中心下的图片点保持不动：center = before * Scale + Offset
            surface.Offset = new Point(center.X - before.X * surface.Scale, center.Y - before.Y * surface.Scale);
            surface.InvalidateVisual();
            UpdateStatus();
        }

        void OnSurfaceWheel(object sender, MouseWheelEventArgs e)
        {
            Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12);
            e.Handled = true;
        }

        // ------------------------------------------------------------ 文字输入
        void OnTextRequested(Rect box)
        {
            textBoxRect = box;
            TextItem existing = surface.Selected as TextItem;
            textEditingNew = existing == null || surface.PendingText != null;
            textInput.Text = existing != null ? existing.Text : "";

            double width = Math.Max(160, box.Width * surface.Scale);
            textInput.Width = Math.Min(Math.Max(160, surface.ActualWidth - 40), width);
            textInput.Visibility = Visibility.Visible;

            Canvas.SetLeft(textInput, box.X * surface.Scale + surface.Offset.X);
            Canvas.SetTop(textInput, box.Y * surface.Scale + surface.Offset.Y);
            textInput.Focus();
            textInput.SelectAll();
        }

        void TextInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitTextInput(); e.Handled = true; }
            else if (e.Key == Key.Escape) { textInput.Visibility = Visibility.Collapsed; e.Handled = true; }
        }

        void CommitTextInput()
        {
            if (textInput.Visibility != Visibility.Visible) return;
            textInput.Visibility = Visibility.Collapsed;
            surface.CommitText(textInput.Text, textBoxRect, textEditingNew);

            // 输完字切到选择工具：这样马上能看到文本框边框，也能拖动/改宽度
            SelectTool(EditorTool.Select);
        }

        // ------------------------------------------------------------ 保存
        void CopyToClipboard()
        {
            BitmapSource composed = surface.Compose();
            if (composed == null) return;
            try { Clipboard.SetImage(composed); }
            catch (Exception ex) { MessageBox.Show(this, "复制失败：" + ex.Message, "图片编辑"); }
        }

        void Save(bool saveAs)
        {
            BitmapSource composed = surface.Compose();
            if (composed == null) return;

            string target = sourcePath;
            if (saveAs || string.IsNullOrEmpty(target))
            {
                using (var dialog = new Forms.SaveFileDialog())
                {
                    dialog.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg";
                    dialog.FileName = string.IsNullOrEmpty(target)
                        ? "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
                        : Path.GetFileNameWithoutExtension(target) + "_编辑" + Path.GetExtension(target);
                    if (!string.IsNullOrEmpty(target))
                    {
                        try { dialog.InitialDirectory = Path.GetDirectoryName(target); }
                        catch (Exception) { }
                    }
                    if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                    target = dialog.FileName;
                }
            }

            try
            {
                WriteBitmap(composed, target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "图片编辑");
                return;
            }

            if (savedCallback != null) savedCallback(target);
            if (saveAs) sourcePath = target;
            Close();
        }

        static void WriteBitmap(BitmapSource bitmap, string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder encoder;
            BitmapSource source = bitmap;

            if (extension == ".jpg" || extension == ".jpeg")
            {
                // JPEG 不支持透明：先铺白底
                var visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                    dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                }
                var flattened = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                flattened.Render(visual);
                flattened.Freeze();
                source = flattened;
                encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            }
            else
            {
                encoder = new PngBitmapEncoder();
            }

            encoder.Frames.Add(BitmapFrame.Create(source));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        // ------------------------------------------------------------ 杂项
        EditorItem lastSelectedForBar;

        void UpdateStatus()
        {
            if (surface.BaseImage != null)
            {
                statusText.Text = string.Format("{0} × {1} px    缩放 {2}%    对象 {3} 个    提示：Ctrl+Z 撤销 · Ctrl+S 保存 · Delete 删除 · 双击文字可改",
                    surface.BaseImage.PixelWidth, surface.BaseImage.PixelHeight,
                    Math.Round(surface.Scale * 100), surface.Items.Count);
            }

            // 只有选中对象换了才重建属性栏（拖动时不动，避免滑块被重置）
            if (!ReferenceEquals(lastSelectedForBar, surface.Selected))
            {
                lastSelectedForBar = surface.Selected;
                BuildPropertyBar();
            }
        }

        void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // 正在输入文字时，所有快捷键让位给输入框
            if (textInput.IsKeyboardFocusWithin) return;

            if (!control)
            {
                switch (e.Key)
                {
                    case Key.D1: case Key.NumPad1: SelectTool(EditorTool.Pan); e.Handled = true; return;
                    case Key.D2: case Key.NumPad2: SelectTool(EditorTool.Pen); e.Handled = true; return;
                    case Key.D3: case Key.NumPad3: SelectTool(EditorTool.Rect); e.Handled = true; return;
                    case Key.D4: case Key.NumPad4: SelectTool(EditorTool.Ellipse); e.Handled = true; return;
                    case Key.D5: case Key.NumPad5: SelectTool(EditorTool.Line); e.Handled = true; return;
                    case Key.D6: case Key.NumPad6: SelectTool(EditorTool.Arrow); e.Handled = true; return;
                    case Key.D7: case Key.NumPad7: SelectTool(EditorTool.Highlight); e.Handled = true; return;
                    case Key.D8: case Key.NumPad8: SelectTool(EditorTool.Text); e.Handled = true; return;
                    case Key.D9: case Key.NumPad9: SelectTool(EditorTool.Bubble); e.Handled = true; return;
                    case Key.D0: case Key.NumPad0: SelectTool(EditorTool.Step); e.Handled = true; return;
                    case Key.OemMinus: SelectTool(EditorTool.Blur); e.Handled = true; return;
                    case Key.OemPlus: SelectTool(EditorTool.Pixelate); e.Handled = true; return;
                    case Key.V: SelectTool(EditorTool.Select); e.Handled = true; return;
                    case Key.C: SelectTool(EditorTool.Crop); e.Handled = true; return;
                }
            }

            if (control && e.Key == Key.Z) { surface.Undo(); e.Handled = true; return; }
            if (control && e.Key == Key.Y) { surface.Redo(); e.Handled = true; return; }
            if (control && e.Key == Key.S) { Save(false); e.Handled = true; return; }
            if (control && e.Key == Key.C) { CopyToClipboard(); e.Handled = true; return; }
            if (control && e.Key == Key.D) { surface.DuplicateSelected(); e.Handled = true; return; }
            if (e.Key == Key.Delete) { surface.DeleteSelected(); e.Handled = true; return; }
            if (e.Key == Key.Escape)
            {
                if (surface.CropActive) { surface.CancelCrop(); e.Handled = true; return; }
                Close();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && surface.CropActive) { surface.ApplyCrop(); e.Handled = true; }
        }
    }

    // 调整图片大小的小对话框
    internal sealed class ResizeDialog : Window
    {
        readonly TextBox widthBox;
        readonly TextBox heightBox;

        public int TargetWidth { get; private set; }
        public int TargetHeight { get; private set; }

        public ResizeDialog(int width, int height)
        {
            TargetWidth = width;
            TargetHeight = height;

            Title = "调整图片大小";
            Width = 300;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 32));

            var panel = new StackPanel { Margin = new Thickness(18) };
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row1.Children.Add(new TextBlock { Text = "宽度", Width = 50, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            widthBox = new TextBox { Text = width.ToString(), Width = 150 };
            row1.Children.Add(widthBox);

            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            row2.Children.Add(new TextBlock { Text = "高度", Width = 50, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            heightBox = new TextBox { Text = height.ToString(), Width = 150 };
            row2.Children.Add(heightBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "确定", Width = 72, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += delegate
            {
                int w, h;
                if (!int.TryParse(widthBox.Text, out w) || w < 1) { MessageBox.Show(this, "宽度无效"); return; }
                if (!int.TryParse(heightBox.Text, out h) || h < 1) { MessageBox.Show(this, "高度无效"); return; }
                if (w > 20000 || h > 20000) { MessageBox.Show(this, "尺寸过大"); return; }
                TargetWidth = w;
                TargetHeight = h;
                DialogResult = true;
            };
            var cancel = new Button { Content = "取消", Width = 72, IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            panel.Children.Add(row1);
            panel.Children.Add(row2);
            panel.Children.Add(buttons);
            Content = panel;
        }
    }
}
