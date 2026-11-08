using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 顶栏工具坞：页签右边一个小圆点，点开散出三个圆球（抖音 / AI / 音乐）。
    //
    // 放在顶栏里而不是面板外侧，是因为窗口只有 1000 宽、面板 960 宽，
    // 左右各剩 20px，圆点放外面会被窗口裁掉一半。
    //
    // 交互约定：
    //   · 点圆点 = 展开 / 收回三个球；
    //   · 点球 = 启动对应功能，球群自动收回；
    //   · Esc / 收起面板时球群强制收回，不留残影。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        sealed class CapsuleTool
        {
            public string Id;
            public string Name;
            public Color ColorFrom;
            public Color ColorTo;
            public Action Launch;
            public Border Orb;
            public Ellipse RunningDot;
            public bool Running;
        }

        readonly List<CapsuleTool> orbTools = new List<CapsuleTool>();
        Border orbTrigger;
        Grid orbDotsIcon;
        Grid orbCloseIcon;
        Grid orbSlot;
        bool orbOpen;
        bool orbAnimating;

        // ------------------------------------------------------------ 构建

        FrameworkElement BuildOrbHub()
        {
            RegisterTools();
            AttachCapsuleNotice();

            // 触发器：26px 小圆，里面三个小点；展开后变成 ×
            orbTrigger = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "更多功能",
                Background = new LinearGradientBrush(
                    Color.FromArgb(38, 255, 255, 255),
                    Color.FromArgb(14, 255, 255, 255),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.35, Color = Colors.Black }
            };

            orbDotsIcon = new Grid { IsHitTestVisible = false };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 3; i++)
            {
                dots.Children.Add(new Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                    Fill = new SolidColorBrush(Color.FromRgb(206, 200, 224))
                });
            }
            orbDotsIcon.Children.Add(dots);

            orbCloseIcon = new Grid { IsHitTestVisible = false, Opacity = 0 };
            var barA = new Rectangle { Width = 9, Height = 1.6, RadiusX = 0.8, RadiusY = 0.8, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var barB = new Rectangle { Width = 9, Height = 1.6, RadiusX = 0.8, RadiusY = 0.8, Fill = new SolidColorBrush(Color.FromRgb(230, 226, 240)), RenderTransform = new RotateTransform(-45), RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            orbCloseIcon.Children.Add(barA);
            orbCloseIcon.Children.Add(barB);

            var triggerHost = new Grid();
            triggerHost.Children.Add(orbDotsIcon);
            triggerHost.Children.Add(orbCloseIcon);
            orbTrigger.Child = triggerHost;
            orbTrigger.MouseLeftButtonUp += delegate { ToggleOrbHub(); };
            orbTrigger.MouseEnter += delegate { AnimateTriggerHover(true); };
            orbTrigger.MouseLeave += delegate { AnimateTriggerHover(false); };

            // 球的容器：宽度固定，避免展开/收回时右边按钮位移
            orbSlot = new Grid
            {
                Width = 3 * 28 + 3 * 8,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var orbRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (CapsuleTool tool in orbTools)
            {
                orbRow.Children.Add(BuildOrb(tool));
            }
            orbSlot.Children.Add(orbRow);

            // 触发球 + 槽位叠在同一格：消息胶囊贴在球右边，展开功能球时被球盖住
            var hub = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            hub.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hub.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(orbTrigger, 0);
            hub.Children.Add(orbTrigger);

            var noticeChip = BuildPanelNoticeChip();
            Grid.SetColumn(noticeChip, 1);
            noticeChip.HorizontalAlignment = HorizontalAlignment.Left;
            noticeChip.Margin = new Thickness(4, 0, 0, 0);
            hub.Children.Add(noticeChip);

            Grid.SetColumn(orbSlot, 1);
            orbSlot.Margin = new Thickness(0);
            hub.Children.Add(orbSlot);   // 加在后面 → 展开时盖住胶囊
            return hub;
        }

        void RegisterTools()
        {
            orbTools.Clear();
            orbTools.Add(new CapsuleTool
            {
                Id = "douyin",
                Name = "抖音",
                ColorFrom = Color.FromRgb(254, 44, 85),
                ColorTo = Color.FromRgb(37, 244, 238),
                Launch = LaunchDouyin
            });
            orbTools.Add(new CapsuleTool
            {
                Id = "ai",
                Name = "AI 问答",
                ColorFrom = Color.FromRgb(124, 108, 242),
                ColorTo = Color.FromRgb(179, 157, 255),
                Launch = LaunchAiChat
            });
            orbTools.Add(new CapsuleTool
            {
                Id = "music",
                Name = "音乐",
                ColorFrom = Color.FromRgb(52, 211, 153),
                ColorTo = Color.FromRgb(14, 165, 233),
                Launch = LaunchMusic
            });
        }

        Border BuildOrb(CapsuleTool tool)
        {
            var orb = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = tool.Name,
                Opacity = 0,
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(tool.ColorFrom, tool.ColorTo, new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                    Color = tool.ColorFrom
                }
            };

            var group = new TransformGroup();
            var scale = new ScaleTransform(0.2, 0.2);
            var move = new TranslateTransform(-8, 0);
            group.Children.Add(scale);
            group.Children.Add(move);
            orb.RenderTransform = group;
            orb.RenderTransformOrigin = new Point(0.5, 0.5);

            var iconHost = new Viewbox { Width = 15, Height = 15, IsHitTestVisible = false };
            iconHost.Child = BuildToolIcon(tool.Id);
            var layered = new Grid();
            layered.Children.Add(iconHost);

            // 运行态：右下角一颗小绿点，默认隐藏
            var runningDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(88, 232, 156)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
                Visibility = Visibility.Collapsed,
                Stroke = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
                StrokeThickness = 1
            };
            layered.Children.Add(runningDot);
            orb.Child = layered;
            tool.RunningDot = runningDot;

            orb.MouseEnter += delegate { AnimateOrbHover(orb, true); };
            orb.MouseLeave += delegate { AnimateOrbHover(orb, false); };
            orb.MouseLeftButtonUp += delegate { RunTool(tool); };
            tool.Orb = orb;
            return orb;
        }

        // 图标用几何图形画，不依赖任何字体（换机器不会变成豆腐块）
        FrameworkElement BuildToolIcon(string id)
        {
            var canvas = new Canvas { Width = 16, Height = 16 };
            if (id == "music")
            {
                double[] x = new double[] { 1.6, 6.4, 11.2 };
                double[] y = new double[] { 5.4, 1.8, 4.2 };
                double[] h = new double[] { 9.0, 12.6, 10.2 };
                for (int i = 0; i < 3; i++)
                {
                    var bar = new Rectangle
                    {
                        Width = 2.6,
                        Height = h[i],
                        RadiusX = 1.3,
                        RadiusY = 1.3,
                        Fill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255))
                    };
                    Canvas.SetLeft(bar, x[i]);
                    Canvas.SetTop(bar, y[i] + (12.6 - h[i]) * 0.35);
                    canvas.Children.Add(bar);
                }
                return canvas;
            }

            if (id == "wechat")
            {
                // 两个叠在一起的对话气泡
                var big = new Border
                {
                    Width = 10.5,
                    Height = 8.5,
                    CornerRadius = new CornerRadius(4.2),
                    Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
                };
                Canvas.SetLeft(big, 1.2);
                Canvas.SetTop(big, 2.4);
                canvas.Children.Add(big);

                var small = new Border
                {
                    Width = 8.5,
                    Height = 7,
                    CornerRadius = new CornerRadius(3.5),
                    Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 20, 120, 70)),
                    BorderThickness = new Thickness(0.8)
                };
                Canvas.SetLeft(small, 5.6);
                Canvas.SetTop(small, 6.6);
                canvas.Children.Add(small);
                return canvas;
            }

            if (id == "qq")
            {
                // 简化的企鹅：头 + 身体
                var body = new System.Windows.Shapes.Ellipse
                {
                    Width = 11,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
                };
                Canvas.SetLeft(body, 2.5);
                Canvas.SetTop(body, 3.0);
                canvas.Children.Add(body);

                var head = new System.Windows.Shapes.Ellipse
                {
                    Width = 7.6,
                    Height = 7.6,
                    Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
                };
                Canvas.SetLeft(head, 4.2);
                Canvas.SetTop(head, 1.2);
                canvas.Children.Add(head);

                var eyeLeft = new System.Windows.Shapes.Ellipse
                {
                    Width = 1.2,
                    Height = 1.2,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 20, 40, 70))
                };
                Canvas.SetLeft(eyeLeft, 5.6);
                Canvas.SetTop(eyeLeft, 4.0);
                canvas.Children.Add(eyeLeft);

                var eyeRight = new System.Windows.Shapes.Ellipse
                {
                    Width = 1.2,
                    Height = 1.2,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 20, 40, 70))
                };
                Canvas.SetLeft(eyeRight, 8.2);
                Canvas.SetTop(eyeRight, 4.0);
                canvas.Children.Add(eyeRight);
                return canvas;
            }

            var path = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            path.Data = Geometry.Parse(id == "ai"
                ? "M8,1.2 L9.7,6.3 L14.8,8 L9.7,9.7 L8,14.8 L6.3,9.7 L1.2,8 L6.3,6.3 Z"
                : "M11.7,2.0 L11.7,8.9 A2.9,2.9 0 1 1 10.3,6.3 L10.3,4.5 L6.0,5.3 L6.0,11.2 A2.9,2.9 0 1 1 4.6,8.6 L4.6,3.5 Z");
            canvas.Children.Add(path);
            return canvas;
        }

        // ------------------------------------------------------------ 展开 / 收回

        void ToggleOrbHub()
        {
            if (orbAnimating) return;
            if (orbOpen) CloseOrbHub(); else OpenOrbHub();
        }

        void OpenOrbHub()
        {
            if (orbOpen) return;
            orbOpen = true;
            orbAnimating = true;
            RaiseOrbHubVisual(true);
            for (int i = 0; i < orbTools.Count; i++)
            {
                Border orb = orbTools[i].Orb;
                orb.IsHitTestVisible = true;
                int delay = i * 45;
                AnimateOrb(orb, 1, delay, i == orbTools.Count - 1);
            }
            Dispatcher.BeginInvoke(new Action(delegate { orbAnimating = false; }), System.Windows.Threading.DispatcherPriority.Background);
        }

        void CloseOrbHub()
        {
            if (!orbOpen) return;
            orbOpen = false;
            orbAnimating = true;
            RaiseOrbHubVisual(false);
            for (int i = 0; i < orbTools.Count; i++)
            {
                Border orb = orbTools[i].Orb;
                int delay = (orbTools.Count - 1 - i) * 30;
                AnimateOrb(orb, 0, delay, false);
                orb.IsHitTestVisible = false;
            }
            Dispatcher.BeginInvoke(new Action(delegate { orbAnimating = false; }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // 面板收起时立刻复位，不做动画（内容已经不可见了）
        void ResetOrbHubInstant()
        {
            orbOpen = false;
            orbAnimating = false;
            RaiseOrbHubVisual(false);
            foreach (CapsuleTool tool in orbTools)
            {
                if (tool.Orb == null) continue;
                tool.Orb.Opacity = 0;
                tool.Orb.IsHitTestVisible = false;
                TransformGroup group = tool.Orb.RenderTransform as TransformGroup;
                if (group == null) continue;
                foreach (Transform transform in group.Children)
                {
                    ScaleTransform scale = transform as ScaleTransform;
                    if (scale != null) { scale.ScaleX = 0.2; scale.ScaleY = 0.2; }
                    TranslateTransform move = transform as TranslateTransform;
                    if (move != null) move.X = -8;
                }
            }
        }

        void AnimateOrb(Border orb, double to, int delay, bool last)
        {
            if (orb == null) return;
            TransformGroup group = orb.RenderTransform as TransformGroup;
            if (group == null) return;
            ScaleTransform scale = null;
            TranslateTransform move = null;
            foreach (Transform transform in group.Children)
            {
                if (scale == null) scale = transform as ScaleTransform;
                if (move == null) move = transform as TranslateTransform;
            }
            if (scale == null || move == null) return;

            double from = to > 0.5 ? 0.2 : 1.0;
            double toScale = to > 0.5 ? 1.0 : 0.2;
            double fromMove = to > 0.5 ? -8 : 0;
            double toMove = to > 0.5 ? 0 : -8;
            int duration = to > 0.5 ? 260 : 140;

            Motion.AnimateDouble(orb, UIElement.OpacityProperty, orb.Opacity, to, duration, Motion.Enter, delay, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, from, toScale, duration, Motion.Spring, delay, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, from, toScale, duration, Motion.Spring, delay, null);
            Motion.AnimateDouble(move, TranslateTransform.XProperty, fromMove, toMove, duration, Motion.Enter, delay, null);
        }

        // 触发器的视觉状态：三个点 ↔ ×，同时底色变亮
        void RaiseOrbHubVisual(bool open)
        {
            if (orbTrigger == null) return;
            Motion.AnimateDouble(orbDotsIcon, UIElement.OpacityProperty, orbDotsIcon.Opacity, open ? 0 : 1, 140, Motion.Enter, 0, null);
            Motion.AnimateDouble(orbCloseIcon, UIElement.OpacityProperty, orbCloseIcon.Opacity, open ? 1 : 0, 140, Motion.Enter, open ? 60 : 0, null);
            orbTrigger.Background = open
                ? new LinearGradientBrush(Color.FromArgb(120, 124, 108, 242), Color.FromArgb(70, 124, 108, 242), new Point(0, 0), new Point(0, 1))
                : new LinearGradientBrush(Color.FromArgb(38, 255, 255, 255), Color.FromArgb(14, 255, 255, 255), new Point(0, 0), new Point(0, 1));
        }

        void AnimateTriggerHover(bool enter)
        {
            if (orbTrigger == null) return;
            double target = enter ? 1.08 : 1.0;
            ScaleTransform scale = orbTrigger.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                orbTrigger.RenderTransform = scale;
                orbTrigger.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, target, Motion.HoverMs, Motion.Enter, 0, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, target, Motion.HoverMs, Motion.Enter, 0, null);
        }

        void AnimateOrbHover(Border orb, bool enter)
        {
            if (orb == null || orb.Opacity < 0.5) return;
            TransformGroup group = orb.RenderTransform as TransformGroup;
            if (group == null) return;
            ScaleTransform scale = null;
            foreach (Transform transform in group.Children)
            {
                scale = transform as ScaleTransform;
                if (scale != null) break;
            }
            if (scale == null) return;
            double target = enter ? 1.12 : 1.0;
            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, target, Motion.HoverMs, Motion.Enter, 0, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, target, Motion.HoverMs, Motion.Enter, 0, null);
        }

        // ------------------------------------------------------------ 启动

        void RunTool(CapsuleTool tool)
        {
            CloseOrbHub();
            if (tool == null || tool.Launch == null) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { tool.Launch(); }
                catch (Exception exception) { ShowToolMessage(tool.Name, "启动失败：" + exception.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        void SetToolRunning(CapsuleTool tool, bool running)
        {
            if (tool == null) return;
            tool.Running = running;
            if (tool.RunningDot != null)
            {
                tool.RunningDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void ShowToolMessage(string title, string message)
        {
            try
            {
                if (tray != null) tray.ShowBalloonTip(2600, title, message, System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception) { }
        }

        // 三个功能暂时是占位：球和动画先跑通，具体窗口接进来时替换这里就行。
        void LaunchDouyin()
        {
            ShowDouyinWindow();
        }

        void LaunchAiChat()
        {
            OpenAiPanel();
        }

        void LaunchMusic()
        {
            OpenMusicPanel();
        }

        // 微信 / QQ：不嵌内容，只把客户端窗口"吸"到胶囊下方
        void LaunchWeChat()
        {
            ToggleMessengerWindow("wechat");
        }

        void LaunchQq()
        {
            ToggleMessengerWindow("qq");
        }
    }
}
