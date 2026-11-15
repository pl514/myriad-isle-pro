using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 胶囊 / 面板的高级感动效。全部是 WPF 原生动画：
    //
    //   胶囊      · 呼吸光晕 + 高光缓慢扫过（循环，只在胶囊露出时跑）
    //             · 悬停：上浮 1.5px、放大 1.5%、描边提亮、光晕点亮
    //             · 按压：缩到 0.972 再回弹
    //             · 新内容到达：轻轻跳一下
    //   面板      · 展开：从顶部缺口向外展开（缩放 + 上移 + 淡入）
    //             · 收起：快速缩小淡出，胶囊回来时带一点回弹
    //             · 卡片错峰入场（0~260ms 阶梯）
    //             · 图片/文字页签用滑动的底块代替硬切
    //             · 滚轮改为平滑跟随
    //
    // 注意：所有动画只碰 Opacity / RenderTransform / 颜色，不改布局，
    // 所以胶囊拖拽、卡片右键拖出、剪贴板同步、热键、查看器逻辑都不受影响。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        static readonly DateTime MotionStart = DateTime.UtcNow;
        static readonly SolidColorBrush TabIdleWash = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
        // 胶囊贴着屏幕上沿，缩放原点放在顶边中点：放大/回弹时不会有一条被裁掉。
        static readonly Point CapsuleOrigin = new Point(0.5, 0);
        // 面板形变原点：顶边中点。缩到 CapsuleWidthRatio 时就正好盖住胶囊那一格。
        static readonly Point PanelMorphOrigin = new Point(0.5, 0);

        SolidColorBrush capsuleBorderBrush;
        Border capsuleAura;
        Border capsuleAuraHot;
        Grid capsuleSheenHost;
        TranslateTransform capsuleSheenShift;
        EyeBlinkControl capsuleEyes;
        Border tabPill;
        TranslateTransform tabPillShift;
        bool tabPillPlaced;
        bool capsuleHovered;
        bool capsulePressedVisual;
        bool capsuleEntered;
        bool pulseRunning;
        DateTime lastPulse = DateTime.MinValue;
        int cardSerial;
        ScrollGlide imageGlide;
        ScrollGlide textGlide;
        readonly HashSet<Border> removingCards = new HashSet<Border>();

        // ================================================================ 胶囊

        Border DecorateCapsule(Border border)
        {
            if (border == null) return null;
            capsuleBorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            border.BorderBrush = capsuleBorderBrush;
            border.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.36 };
            // 标题文字已经去掉（改用卡通眼睛），清掉旧 child 后挂新的层叠内容。
            border.Child = null;
            border.Child = BuildCapsuleLayers();

            // 眼睛尺寸跟随胶囊，胶囊缩放时自动同步。
            LayoutCapsuleEyes();
            border.SizeChanged += delegate { LayoutCapsuleEyes(); };

            border.MouseEnter += delegate { CapsuleHover(true); };
            border.MouseLeave += delegate { CapsuleHover(false); };
            border.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left) CapsulePress(true);
            };
            border.PreviewMouseLeftButtonUp += delegate { CapsulePress(false); };
            border.LostMouseCapture += delegate { if (capsulePressedVisual) CapsulePress(false); };
            border.Loaded += delegate { CapsuleEnter(); };
            Closed += delegate { StopGlides(); StopCapsuleAmbient(); };
            return border;
        }

        UIElement BuildCapsuleLayers()
        {
            // 未展开的胶囊上不再有标题和蓝色圆点，换成一双会眨眼的卡通眼睛。
            capsuleEyes = new EyeBlinkControl
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            ConfigureCapsuleEyes(2000, 6000, 300, 100, 0.56);
            capsuleEyes.EyesEnabled = AppSettings.EyesEnabled;

            // 光晕只画在胶囊轮廓边缘的一圈（描边 + 模糊），
            // 不用整块径向渐变，否则会把深色玻璃胶囊中间洗白。
            capsuleAura = RingGlow(Color.FromArgb(120, 132, 192, 255), 4, 12, 0.20);
            capsuleAuraHot = RingGlow(Color.FromArgb(180, 168, 208, 255), 5, 14, 0);

            var glass = new Border
            {
                Margin = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(0, 0, 19, 19),
                IsHitTestVisible = false,
                Background = VerticalFade(48, 6, 0)
            };
            var hairline = new Border
            {
                Height = 1,
                Margin = new Thickness(18, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Background = HorizontalFade(0, 120, 0)
            };

            capsuleSheenShift = new TranslateTransform(-150, 0);
            var sheenGroup = new TransformGroup();
            sheenGroup.Children.Add(new SkewTransform(-18, 0));
            sheenGroup.Children.Add(capsuleSheenShift);
            var band = new Border
            {
                Width = 92,
                Height = 37,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Opacity = 0.8,
                Background = HorizontalFade(0, 70, 0),
                RenderTransform = sheenGroup
            };
            capsuleSheenHost = new Grid { IsHitTestVisible = false };
            capsuleSheenHost.Children.Add(band);
            capsuleSheenHost.SizeChanged += delegate
            {
                double width = capsuleSheenHost.ActualWidth;
                double height = capsuleSheenHost.ActualHeight;
                if (width > 0 && height > 0)
                    capsuleSheenHost.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 13, 13);
            };

            var layers = new Grid { IsHitTestVisible = false };
            layers.Children.Add(capsuleAura);
            layers.Children.Add(capsuleAuraHot);
            layers.Children.Add(glass);
            layers.Children.Add(hairline);
            layers.Children.Add(capsuleSheenHost);
            layers.Children.Add(capsuleEyes);

            // 迷你播放器单独一层（要能点按钮，所以不能跟着 layers 一起关掉命中测试）
            var root = new Grid();
            root.Children.Add(layers);
            capsuleMiniPlayer = BuildCapsuleMiniPlayer();
            root.Children.Add(capsuleMiniPlayer);
            return root;
        }

        // 眼睛尺寸跟随胶囊：宽度按比例算，垂直位置略偏上，整体不会超出胶囊（不会被圆角裁到）。
        void LayoutCapsuleEyes()
        {
            if (capsuleEyes == null) return;
            double width = capsule != null && capsule.ActualWidth > 1 ? capsule.ActualWidth : 220;
            double height = capsule != null && capsule.ActualHeight > 1 ? capsule.ActualHeight : 41;
            // 竖条眼睛：先按胶囊高度定眼睛高度，再按设计画布 60:40 推出宽度。
            double eyeHeight = Math.Max(10, height * capsuleEyes.SizeRatio);
            double eyeWidth = eyeHeight * (60.0 / 40.0);
            double limit = width * 0.42;
            if (eyeWidth > limit)
            {
                eyeWidth = limit;
                eyeHeight = eyeWidth * (40.0 / 60.0);
            }
            capsuleEyes.Width = eyeWidth;
            capsuleEyes.Height = eyeHeight;
            // 定位：顶部居中，离胶囊上沿留一点间距。
            capsuleEyes.Margin = new Thickness(0, Math.Max(3, height * 0.13), 0, 0);
        }

        /// <summary>卡通眼睛总开关，读写配置项 EyesEnabled（会写回 data\settings.txt）。</summary>
        internal bool CapsuleEyesEnabled
        {
            get { return capsuleEyes == null || capsuleEyes.EyesEnabled; }
            set
            {
                AppSettings.EyesEnabled = value;
                AppSettings.Save();
                if (capsuleEyes != null) capsuleEyes.EyesEnabled = value;
            }
        }

        /// <summary>一次性调整眨眼参数（间隔下限/上限、周期、闭眼保持、眼睛大小比例）。</summary>
        internal void ConfigureCapsuleEyes(int intervalMinMs, int intervalMaxMs, int blinkDurationMs, int closedHoldMs, double sizeRatio)
        {
            if (capsuleEyes == null) return;
            capsuleEyes.IntervalMinMs = intervalMinMs;
            capsuleEyes.IntervalMaxMs = intervalMaxMs;
            capsuleEyes.BlinkDurationMs = blinkDurationMs;
            capsuleEyes.ClosedHoldMs = closedHoldMs;
            capsuleEyes.SizeRatio = sizeRatio;
            LayoutCapsuleEyes();
        }

        /// <summary>立刻眨一次眼（调试/演示用）。</summary>
        internal void BlinkCapsuleEyes()
        {
            if (capsuleEyes != null) capsuleEyes.Blink();
        }

        // 边缘光晕：一圈描边 + 模糊，向内只是一条细的轮廓光，不污染胶囊主体。
        static Border RingGlow(Color color, double thickness, double blurRadius, double opacity)
        {
            return new Border
            {
                Margin = new Thickness(-5, 0, -5, -6),
                CornerRadius = new CornerRadius(0, 0, 24, 24),
                BorderThickness = new Thickness(thickness),
                BorderBrush = new SolidColorBrush(color),
                IsHitTestVisible = false,
                Opacity = opacity,
                Effect = new BlurEffect { Radius = blurRadius }
            };
        }

        static LinearGradientBrush VerticalFade(byte top, byte middle, byte bottom)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(top, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(middle, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 255, 255, 255), 1));
            return brush;
        }

        static LinearGradientBrush HorizontalFade(byte left, byte middle, byte right)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(left, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(middle, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(right, 255, 255, 255), 1));
            return brush;
        }

        void CapsuleEnter()
        {
            if (capsuleEntered) return;
            capsuleEntered = true;
            if (capsule == null) return;
            StartCapsuleAmbient();
            Motion.Prepare(capsule, CapsuleOrigin);
            ScaleTransform scale = Motion.ScaleOf(capsule, CapsuleOrigin);
            Motion.AnimateDouble(capsule, UIElement.OpacityProperty, 0, 1, 520, Motion.Drop, 40, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, 0.9, 1, 580, Motion.Drop, 40, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, 0.9, 1, 580, Motion.Drop, 40, null);
            Motion.AnimateDouble(Motion.MoveOf(capsule), TranslateTransform.YProperty, -16, 0, 580, Motion.Drop, 40, null);
        }

        void StartCapsuleAmbient()
        {
            if (!Motion.Enabled) return;
            if (capsuleAura != null)
            {
                var breathe = new DoubleAnimation(0.16, 0.36, new Duration(TimeSpan.FromMilliseconds(3800)))
                {
                    EasingFunction = Motion.Soft,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                capsuleAura.BeginAnimation(UIElement.OpacityProperty, breathe);
            }
            if (capsuleSheenShift != null)
            {
                var sweep = new DoubleAnimationUsingKeyFrames
                {
                    Duration = new Duration(TimeSpan.FromMilliseconds(5200)),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                sweep.KeyFrames.Add(new LinearDoubleKeyFrame(-150, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                sweep.KeyFrames.Add(new LinearDoubleKeyFrame(270, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
                sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(270, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(5000))));
                capsuleSheenShift.BeginAnimation(TranslateTransform.XProperty, sweep);
            }
        }

        void StopCapsuleAmbient()
        {
            try
            {
                if (capsuleAura != null)
                {
                    capsuleAura.BeginAnimation(UIElement.OpacityProperty, null);
                    capsuleAura.Opacity = 0.22;
                }
                if (capsuleSheenShift != null)
                {
                    capsuleSheenShift.BeginAnimation(TranslateTransform.XProperty, null);
                    capsuleSheenShift.X = -150;
                }
            }
            catch (Exception) { }
        }

        void CapsuleHover(bool on)
        {
            if (capsule == null || expanded) return;
            capsuleHovered = on;
            Point origin = CapsuleOrigin;
            double scale = on ? 1.015 : (capsulePressedVisual ? 0.972 : 1.0);
            Motion.ScaleTo(capsule, scale, on ? Motion.HoverMs : 240, on ? Motion.Enter : Motion.Spring, 0, origin, null);
            Motion.MoveTo(capsule, 0, on ? -1.5 : 0, on ? Motion.HoverMs : 240, on ? Motion.Enter : Motion.Soft, 0);
            if (capsuleBorderBrush != null)
                Motion.ColorTo(capsuleBorderBrush, on ? Color.FromArgb(96, 255, 255, 255) : Color.FromArgb(46, 255, 255, 255), 200, Motion.Enter);
            if (capsuleAuraHot != null)
                Motion.Fade(capsuleAuraHot, on ? 0.7 : 0, on ? 220 : 280, Motion.Soft, 0, null);
        }

        void CapsulePress(bool down)
        {
            if (capsule == null) return;
            capsulePressedVisual = down;
            Point origin = CapsuleOrigin;
            double scale = down ? 0.972 : (capsuleHovered ? 1.015 : 1.0);
            Motion.ScaleTo(capsule, scale, down ? Motion.TapMs : 260, down ? Motion.Enter : Motion.Spring, 0, origin, null);
            if (capsuleAuraHot == null) return;
            if (down) Motion.Fade(capsuleAuraHot, 0.75, 90, Motion.Enter, 0, null);
            else if (!capsuleHovered) Motion.Fade(capsuleAuraHot, 0, 240, Motion.Soft, 0, null);
        }

        // 展开时：胶囊原地淡出——面板正好从它这一格长出来，会先盖住它，
// 所以不需要再往上飞，“胶囊变大”这个连续感是保住的。
        void CapsuleAway()
        {
            if (capsule == null) return;
            capsuleHovered = false;
            capsulePressedVisual = false;
            StopCapsuleAmbient();
            Motion.AnimateDouble(capsule, UIElement.OpacityProperty, capsule.Opacity, 0, 110, Motion.Enter, 0, delegate
            {
                if (!expanded) return;
                capsule.Visibility = Visibility.Collapsed;
            });
        }

        // 收起时：面板缩到胶囊大小后消失，胶囊在最后一段淡入并轻轻弹一下。
        void CapsuleBack()
        {
            if (capsule == null) return;
            capsule.Visibility = Visibility.Visible;
            if (capsuleAuraHot != null)
            {
                Motion.Stop(capsuleAuraHot, UIElement.OpacityProperty);
                capsuleAuraHot.Opacity = 0;
            }
            Motion.Prepare(capsule, CapsuleOrigin);
            ScaleTransform scale = Motion.ScaleOf(capsule, CapsuleOrigin);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, 0.94, 1, 280, Motion.Spring, 140, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, 0.94, 1, 280, Motion.Spring, 140, null);
            Motion.AnimateDouble(capsule, UIElement.OpacityProperty, 0, 1, 170, Motion.Enter, 140, null);
            StartCapsuleAmbient();
        }

        // 热键重新显示胶囊时的一次小回弹，避免“凭空出现”。
        void CapsuleReturnJump()
        {
            if (capsule == null) return;
            if (panel != null && !expanded) panel.Visibility = Visibility.Collapsed;
            if (capsuleAuraHot != null)
            {
                Motion.Stop(capsuleAuraHot, UIElement.OpacityProperty);
                capsuleAuraHot.Opacity = 0;
            }
            Motion.PopIn(capsule, CapsuleOrigin, 0.97, -10, 300, 0, Motion.Spring);
            StartCapsuleAmbient();
        }

        // 剪贴板/截图来了新内容而胶囊是收起的：轻轻跳一下作为提示。
        void PulseCapsule()
        {
            if (capsule == null || pulseRunning) return;
            if (expanded || capsule.Visibility != Visibility.Visible) return;
            if ((DateTime.UtcNow - MotionStart).TotalMilliseconds < 2200) return;
            if ((DateTime.UtcNow - lastPulse).TotalMilliseconds < 700) return;
            lastPulse = DateTime.UtcNow;
            pulseRunning = true;
            Point origin = CapsuleOrigin;
            Motion.ScaleTo(capsule, 1.05, 130, Motion.Enter, 0, origin, delegate
            {
                Motion.ScaleTo(capsule, capsuleHovered ? 1.015 : 1.0, 340, Motion.Spring, 0, origin, delegate
                {
                    pulseRunning = false;
                });
            });
            if (capsuleAuraHot == null) return;
            Motion.Fade(capsuleAuraHot, 0.8, 120, Motion.Enter, 0, null);
            Motion.Delay(260, delegate
            {
                if (!capsuleHovered && !capsulePressedVisual)
                    Motion.Fade(capsuleAuraHot, 0, 340, Motion.Soft, 0, null);
            });
        }

        // ================================================================ 面板

        // 不关闭面板的「刷新」：隐藏一帧再显示（用户看不见），让 WPF 重新走一遍渲染。
        // 用户提的思路：返回时借形变这段时间刷新一下图片面板，而不是真的关掉再打开。
        internal void RefreshSurfaceQuick(FrameworkElement surface, Action after)
        {
            if (surface == null)
            {
                if (after != null) after();
                return;
            }
            try
            {
                // 关键：整个过程在同一个 Dispatcher 操作里同步做完，
                // 中间不会产生一帧"面板已隐藏"的画面，所以看不到闪烁。
                surface.Visibility = Visibility.Collapsed;
                surface.UpdateLayout();
                surface.Visibility = Visibility.Visible;
                HardSetOpacity(surface, 1);
                surface.UpdateLayout();
                surface.InvalidateVisual();
                UpdateLayout();
            }
            catch (Exception) { }
            if (after != null) after();
        }

        // 元素在窗口坐标系里的矩形（用来做「面板 A → 面板 B」的 FLIP 形变）
        internal Rect BoundsInWindow(FrameworkElement element)
        {
            try
            {
                if (element == null) return Rect.Empty;
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0) UpdateLayout();
                GeneralTransform t = element.TransformToAncestor(this);
                return t.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (Exception) { return Rect.Empty; }
        }

        // 从一个矩形形变到元素自身尺寸：宽、高、位置一起变，才是「换面板」该有的样子
        internal void MorphBetween(FrameworkElement target, Rect from, int durationMs)
        {
            if (target == null || from.IsEmpty || from.Width < 1 || from.Height < 1) return;
            target.Visibility = Visibility.Visible;
            target.IsHitTestVisible = true;
            UpdateLayout();
            Rect to = BoundsInWindow(target);
            if (to.IsEmpty || to.Width < 1 || to.Height < 1) return;

            ScaleTransform scale = Motion.ScaleOf(target, new Point(0, 0));
            TranslateTransform move = Motion.MoveOf(target);
            HardResetScale(scale);
            HardResetMove(move);

            double sx = from.Width / to.Width;
            double sy = from.Height / to.Height;
            double tx = from.X - to.X;
            double ty = from.Y - to.Y;

            scale.ScaleX = sx;
            scale.ScaleY = sy;
            move.X = tx;
            move.Y = ty;

            Motion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, sx, 1, durationMs, Motion.Enter, 0, null);
            Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, sy, 1, durationMs, Motion.Enter, 0, null);
            Motion.AnimateDouble(move, TranslateTransform.XProperty, tx, 0, durationMs, Motion.Enter, 0, null);
            Motion.AnimateDouble(move, TranslateTransform.YProperty, ty, 0, durationMs, Motion.Enter, 0, null);
        }

        // 从图片面板切到更高/更矮的面板时，做一个「由矮变高」的高度动画：
        // 只动面板自己的 ScaleY（顶部为轴），不改窗口尺寸，避免分层窗口黑屏。
        internal void GrowSurfaceFrom(FrameworkElement surface, double fromHeight, int durationMs)
        {
            try
            {
                if (surface == null) return;
                double target = surface.Height > 1 ? surface.Height : surface.ActualHeight;
                if (target <= 1 || fromHeight <= 1) return;
                double ratio = Math.Max(0.25, Math.Min(1, fromHeight / target));
                ScaleTransform scale = Motion.ScaleOf(surface, new Point(0.5, 0));
                HardResetScale(scale);
                scale.ScaleY = ratio;
                Motion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, ratio, 1, durationMs, Motion.Enter, 0, null);
            }
            catch (Exception) { }
        }

        void SetExpandedAnimated(bool value)
        {
            if (expanded == value) return;
            expanded = value;
            FrameworkElement surface = aiVisible && aiPanel != null
                ? (FrameworkElement)aiPanel
                : (musicVisible && musicPanel != null ? (FrameworkElement)musicPanel : panel);
            if (value)
            {
                capsule.Visibility = Visibility.Visible;
                surface.Visibility = Visibility.Visible;
                surface.IsHitTestVisible = true;
                if (!aiVisible && !musicVisible) SetTab(false);
                if (!quietExpand)
                {
                    Activate();
                    Focus();
                }

                // 胶囊 → 面板：面板从“胶囊大小”长到全尺寸，起点正好盖在胶囊那一格，
                // 所以看到的是胶囊自己被拉大成面板，而不是凭空冒出一块新面板。
                // AI 面板同理：surface 换成 aiPanel，形变起点按它自己的宽高算。
                Motion.Prepare(surface, PanelMorphOrigin);
                ScaleTransform panelScale = Motion.ScaleOf(surface, PanelMorphOrigin);
                TranslateTransform panelMove = Motion.MoveOf(surface);
                if (panelScale.ScaleX >= 0.999 && panelScale.ScaleY >= 0.999)
                {
                    // 首次展开（或上一次收起没走完）：先把形变起点摆好，第一帧不会闪一下全尺寸。
                    panelScale.ScaleX = CapsuleWidthRatio(surface);
                    panelScale.ScaleY = CapsuleHeightRatio(surface);
                    panelMove.Y = -surface.Margin.Top;
                }

                Motion.AnimateDouble(panelScale, ScaleTransform.ScaleXProperty, panelScale.ScaleX, 1, 420, Motion.Enter, 0, null);
                Motion.AnimateDouble(panelScale, ScaleTransform.ScaleYProperty, panelScale.ScaleY, 1, 420, Motion.Enter, 0, null);
                Motion.AnimateDouble(panelMove, TranslateTransform.YProperty, panelMove.Y, 0, 420, Motion.Enter, 0, null);
                Motion.AnimateDouble(surface, UIElement.OpacityProperty, 0, 1, 150, Motion.Enter, 0, null);

                Border surfaceBorder = surface as Border;
                UIElement body = surfaceBorder == null ? null : surfaceBorder.Child as UIElement;
                if (body != null) Motion.AnimateDouble(body, UIElement.OpacityProperty, 0, 1, 260, Motion.Enter, 120, null);

                if (!aiVisible && !musicVisible) RevealCards();
                CapsuleAway();
            }
            else
            {
                StopGlides();
                cardSerial = 0;
                surface.IsHitTestVisible = false;

                // 面板 → 胶囊：缩回胶囊大小再消失，胶囊在最后一段淡入并轻微回弹。
                Motion.Prepare(surface, PanelMorphOrigin);
                ScaleTransform panelScale = Motion.ScaleOf(surface, PanelMorphOrigin);
                TranslateTransform panelMove = Motion.MoveOf(surface);

                Motion.AnimateDouble(panelScale, ScaleTransform.ScaleXProperty, panelScale.ScaleX, CapsuleWidthRatio(surface), 300, Motion.Enter, 0, null);
                Motion.AnimateDouble(panelScale, ScaleTransform.ScaleYProperty, panelScale.ScaleY, CapsuleHeightRatio(surface), 300, Motion.Enter, 0, null);
                Motion.AnimateDouble(panelMove, TranslateTransform.YProperty, panelMove.Y, -surface.Margin.Top, 300, Motion.Enter, 0, null);

                Border surfaceBorder = surface as Border;
                UIElement body = surfaceBorder == null ? null : surfaceBorder.Child as UIElement;
                if (body != null) Motion.AnimateDouble(body, UIElement.OpacityProperty, body.Opacity, 0, 130, Motion.Exit, 0, null);

                Motion.AnimateDouble(surface, UIElement.OpacityProperty, surface.Opacity, 0, 170, Motion.Exit, 120, delegate
                {
                    if (expanded) return;
                    if (surface == aiPanel) HideAiSurface();
                    else if (surface == musicPanel) HideMusicSurface();
                    else surface.Visibility = Visibility.Collapsed;
                    surface.IsHitTestVisible = true;
                    if (aiVisible)
                    {
                        // AI 收起动画播完，再还原窗口高度：早改会把面板直接切掉一截。
                        aiVisible = false;
                        ApplyAiWindowHeight(false);
                    }
                    if (musicVisible) musicVisible = false;
                });

                CapsuleBack();

                // 兜底：收起动画如果被下一次展开打断，Completed 不会触发，
                // aiVisible 就会卡在 true（下次点胶囊会直接弹回 AI 面板）。这里超时强制收尾。
                Motion.Delay(340, delegate
                {
                    if (expanded) return;
                    if (surface == aiPanel) HideAiSurface();
                    else if (surface == musicPanel) HideMusicSurface();
                    else surface.Visibility = Visibility.Collapsed;
                    surface.IsHitTestVisible = true;
                    if (aiVisible)
                    {
                        aiVisible = false;
                        ApplyAiWindowHeight(false);
                    }
                    if (musicVisible) musicVisible = false;
                });
            }
        }

        // ---------------------------------------------------------------
        // 图片飞入 / 飞回：剪贴板、截图、拖放进来的图片先“飞”到胶囊或卡片里；
        // 关掉预览窗口时再缩回胶囊。飞行由一个点击穿过的透明窗口完成。
        // ---------------------------------------------------------------
        bool suppressCardIn;
        bool quietExpand;
        bool peekOwned;
        // 收图 peek 结束后要恢复的页面（截图前正在看音乐/AI 时，收完图自动关掉再回去）
        bool peekRestoreMusic;
        bool peekRestoreAi;
        System.Windows.Threading.DispatcherTimer peekTimer;

        sealed class IncomingImage
        {
            public BitmapSource Image;
            public Rect From;
            public Border Card;
            public bool Peek;
            public int Attempts;
        }

        IncomingImage pendingImage;

        double DpiScale()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source == null || source.CompositionTarget == null ? 1.0 : source.CompositionTarget.TransformToDevice.M11;
        }

        Rect ScreenRectOf(FrameworkElement element)
        {
            if (element == null || !element.IsVisible || element.ActualWidth <= 0) return Rect.Empty;
            try
            {
                double dpi = DpiScale();
                Point raw = element.PointToScreen(new Point(0, 0));
                return new Rect(raw.X / dpi, raw.Y / dpi, element.ActualWidth, element.ActualHeight);
            }
            catch (Exception) { return Rect.Empty; }
        }

        // 胶囊在屏幕上的位置（DIP）。展开时胶囊是 Collapsed，拿不到 ActualWidth，
        // 所以直接用它的固定尺寸 + 窗口位置推算。
        internal Rect CapsuleScreenRect()
        {
            double width = capsule != null && capsule.Width > 1 ? capsule.Width : 220;
            double height = capsule != null && capsule.Height > 1 ? capsule.Height : 41;
            double windowWidth = ActualWidth > 1 ? ActualWidth : 1000;
            return new Rect(Left + (windowWidth - width) / 2, Top, width, height);
        }

        internal Rect CapsuleScreenRectPhysical()
        {
            Rect rect = CapsuleScreenRect();
            double dpi = DpiScale();
            return new Rect(rect.X * dpi, rect.Y * dpi, rect.Width * dpi, rect.Height * dpi);
        }

        static BitmapSource LoadThumb(string path, int width)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (width > 0) image.DecodePixelWidth = width;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        }

        // 接受一张“从别处来”的图片：必要时先把面板展开，卡片先隐形，等图飞到位再显形。
        internal void AcceptIncomingImage(string path, Rect fromScreen, bool openPanel, bool peek)
        {
            if (string.IsNullOrEmpty(path) || cards == null) return;

            // 胶囊被热键收起来时不打扰用户：只记下来，等胶囊回来再说。
            if (openPanel && !expanded && !capsuleClosed && IsVisible)
            {
                peekOwned = true;
                quietExpand = true;
                // 新图片要用图片面板接住；forceImageSurface 只影响这次展开，不覆盖"上次是音乐/AI"的记忆
                forceImageSurface = true;
                try { SetExpanded(true); } finally { quietExpand = false; }
            }
            else if (openPanel && expanded)
            {
                // 面板开着但停在音乐 / AI / 文字 / 翻译页：图片必须落在「图片」页上，
                // 所以切回图片页，并且落地后自动收起，省得用户手动关。
                bool switched = false;
                bool wasMusic = musicVisible;
                bool wasAi = aiVisible;
                if (aiVisible) { BackToImagePanel(); switched = true; }
                if (musicVisible) { BackToImagePanelFromMusic(); switched = true; }
                if (showingText || showingTranslate) { SetTab(false); switched = true; }
                if (switched)
                {
                    peek = true;
                    peekOwned = true;
                    peekRestoreMusic = wasMusic || musicLastSurface;
                    peekRestoreAi = wasAi || aiLastSurface;
                }
            }

            if (pendingImage != null)
            {
                // 上一张还没落地就被顶掉了：先让它显形，避免卡片永远隐形。
                Border older = pendingImage.Card;
                pendingImage = null;
                if (older != null) Motion.Fade(older, 1, 160, Motion.Enter, 0, null);
            }

            int before = cards.Children.Count;
            suppressCardIn = true;
            try { AddImage(path); } finally { suppressCardIn = false; }
            if (cards.Children.Count <= before) return;

            Border card = cards.Children[cards.Children.Count - 1] as Border;
            if (card == null) return;

            BitmapSource image = LoadThumb(path, 420);
            if (image == null)
            {
                Motion.Fade(card, 1, 180, Motion.Enter, 0, null);
                if (peek) HoldPeek();
                return;
            }

            pendingImage = new IncomingImage { Image = image, From = fromScreen, Card = card, Peek = peek };
            LaunchPendingImage(openPanel || peek);
        }

        void LaunchPendingImage(bool awaitingPanel)
        {
            if (pendingImage == null) return;
            Motion.Delay(awaitingPanel ? 190 : 40, delegate
            {
                IncomingImage job = pendingImage;
                if (job == null) return;
                Rect target = ScreenRectOf(job.Card);
                if (target.IsEmpty)
                {
                    // 面板还没上屏（截图流程里窗口刚 Show），再等一轮。
                    job.Attempts++;
                    if (job.Attempts <= 6)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { if (pendingImage == job) LaunchPendingImage(true); }),
                            System.Windows.Threading.DispatcherPriority.Loaded);
                        return;
                    }
                    pendingImage = null;
                    Motion.Fade(job.Card, 1, 180, Motion.Enter, 0, null);
                    if (job.Peek) HoldPeek();
                    return;
                }
                pendingImage = null;
                target.Inflate(-6, -6);
                FlyImageWindow.Fly(job.Image, job.From, target, 400, delegate
                {
                    LandInCard(job.Card);
                    if (job.Peek) HoldPeek();
                });
            });
        }

        // 图片落到卡片：卡片显形 + 轻微回弹；面板没开时让胶囊“接住”一下。
        void LandInCard(Border card)
        {
            if (card == null || cards == null || !cards.Children.Contains(card)) return;
            Point origin = new Point(0.5, 0.5);
            Motion.AnimateDouble(card, UIElement.OpacityProperty, 0, 1, 170, Motion.Enter, 0, null);
            Motion.ScaleTo(card, 1.035, 180, Motion.Enter, 0, origin, delegate
            {
                Motion.ScaleTo(card, 1.0, 260, Motion.Soft, 0, origin, null);
            });
            if (!expanded) PulseCapsule();
        }

        // 收图专用：面板自己弹开一下、图飞进来、再自动收起。
        void HoldPeek()
        {
            if (!peekOwned) return;
            if (peekTimer == null)
            {
                peekTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
                peekTimer.Tick += delegate
                {
                    peekTimer.Stop();
                    if (!peekOwned) return;
                    peekOwned = false;
                    if (expanded) SetExpanded(false);
                    // 收完图：把「上次是音乐 / AI」的记忆还回去，下次展开还是原来那页
                    if (peekRestoreMusic) musicLastSurface = true;
                    if (peekRestoreAi) aiLastSurface = true;
                    peekRestoreMusic = false;
                    peekRestoreAi = false;
                };
            }
            peekTimer.Stop();
            peekTimer.Interval = TimeSpan.FromMilliseconds(750);
            peekTimer.Start();
        }

        internal void CollapseShortly(int milliseconds)
        {
            Motion.Delay(milliseconds, delegate { if (expanded) SetExpanded(false); });
        }

        // 图片回到胶囊：胶囊出现 + 弹一下（预览窗口关掉时用）。
        internal void CapsuleCatch()
        {
            suppressDeactivate = false;
            peekOwned = false;
            if (peekTimer != null) peekTimer.Stop();
            if (capsule == null) return;
            capsule.Visibility = Visibility.Visible;
            CapsuleBack();
            Motion.Delay(300, delegate { if (!expanded) PulseCapsule(); });
        }

        // 剪贴板来图：从鼠标位置飞进来。
        internal void AcceptClipboardImage(string path)
        {
            AcceptIncomingImage(path, CursorScreenRect(), true, true);
        }

        Rect CursorScreenRect()
        {
            try
            {
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                double dpi = DpiScale();
                double x = cursor.X / dpi, y = cursor.Y / dpi;
                const double width = 190, height = 130;
                double left = Math.Max(SystemParameters.VirtualScreenLeft + 4,
                    Math.Min(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width - 4, x - width / 2));
                double top = Math.Max(SystemParameters.VirtualScreenTop + 4,
                    Math.Min(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height - 4, y - height / 2));
                return new Rect(left, top, width, height);
            }
            catch (Exception)
            {
                Rect fallback = CapsuleScreenRect();
                return new Rect(fallback.X + fallback.Width / 2 - 95, fallback.Y + 40, 190, 130);
            }
        }

        // 拖文件进胶囊：从落点飞进卡片，飞完再自动收起。
        internal void AcceptDroppedImage(string path, Point dropPoint)
        {
            Rect from;
            try
            {
                double dpi = DpiScale();
                Point raw = PointToScreen(dropPoint);
                from = new Rect(raw.X / dpi - 95, raw.Y / dpi - 65, 190, 130);
            }
            catch (Exception) { from = CursorScreenRect(); }
            AcceptIncomingImage(path, from, false, false);
        }

        // 截图（窗口 / 全屏）：从被截的那块矩形飞进来，面板随后由 RestoreShot 打开。
        internal void AcceptScreenshotImage(string path, System.Drawing.Rectangle bounds)
        {
            double dpi = DpiScale();
            Rect from = new Rect(bounds.X / dpi, bounds.Y / dpi,
                Math.Max(40, bounds.Width / dpi), Math.Max(30, bounds.Height / dpi));
            AcceptIncomingImage(path, from, false, false);
        }

        // 胶囊在面板坐标系里的相对尺寸：面板缩到这两个比例时，正好盖住胶囊那一格。
        double CapsuleWidthRatio(FrameworkElement surface)
        {
            double hostWidth = surface != null && surface.Width > 1 ? surface.Width : 960;
            double capsuleWidth = capsule.Width > 1 ? capsule.Width : 148;
            return capsuleWidth / hostWidth;
        }

        double CapsuleHeightRatio(FrameworkElement surface)
        {
            double hostHeight = surface != null && surface.Height > 1 ? surface.Height : 236;
            double capsuleHeight = capsule.Height > 1 ? capsule.Height : 37;
            return capsuleHeight / hostHeight;
        }

        void RevealCards()
        {
            if (cards == null) return;
            int index = 0;
            foreach (UIElement child in cards.Children)
            {
                FrameworkElement card = child as FrameworkElement;
                if (card == null) continue;
                // 面板长到差不多大了卡片再依次就位，避免在压扁的阶段就入场。
                int delay = Math.Min(200, 120 + index * 20);
                Motion.PopIn(card, new Point(0.5, 0.5), 0.93, 14, 340, delay, Motion.Drop);
                index++;
                if (index >= 12) break;
            }
        }

        void OnCardAdded(Border card)
        {
            if (card == null) return;
            Motion.Prepare(card, new Point(0.5, 0.5));
            AttachCardMotion(card);
            if (suppressCardIn)
            {
                // 有图片正飞进来：卡片先隐形，等它落地再显形（见 LandInCard）。
                suppressCardIn = false;
                card.Opacity = 0;
                return;
            }
            if (expanded)
            {
                int delay = Math.Min(240, cardSerial++ * 26);
                Motion.PopIn(card, new Point(0.5, 0.5), 0.9, 18, 340, delay, Motion.Drop);
            }
            else
            {
                cardSerial = 0;
                PulseCapsule();
            }
        }

        void AttachCardMotion(Border card)
        {
            Point origin = new Point(0.5, 0.5);
            card.MouseEnter += delegate
            {
                if (card.IsMouseCaptured) return;
                Motion.ScaleTo(card, 1.02, 180, Motion.Enter, 0, origin, null);
                Motion.MoveTo(card, 0, -3, 180, Motion.Enter, 0);
            };
            card.MouseLeave += delegate
            {
                Motion.ScaleTo(card, 1.0, 220, Motion.Soft, 0, origin, null);
                Motion.MoveTo(card, 0, 0, 220, Motion.Soft, 0);
            };
            card.PreviewMouseLeftButtonDown += delegate
            {
                Motion.ScaleTo(card, 0.985, Motion.TapMs, Motion.Enter, 0, origin, null);
            };
            card.PreviewMouseLeftButtonUp += delegate
            {
                Motion.ScaleTo(card, card.IsMouseOver ? 1.02 : 1.0, 240, Motion.Spring, 0, origin, null);
            };
        }

        // 拖出去的时候卡片轻微抬起，和系统拖拽缩略图呼应；结束后回位。
        internal void CardDragVisual(Border card, bool on)
        {
            if (card == null) return;
            Point origin = new Point(0.5, 0.5);
            Motion.ScaleTo(card, on ? 0.965 : 1.0, on ? 150 : 300, on ? Motion.Enter : Motion.Spring, 0, origin, null);
            Motion.MoveTo(card, 0, on ? -5 : 0, on ? 150 : 300, on ? Motion.Enter : Motion.Soft, 0);
        }

        internal void RemoveCardAnimated(Border card)
        {
            if (card == null) return;
            if (!removingCards.Add(card)) return;
            card.IsHitTestVisible = false;
            Motion.PopOut(card, 0.86, -6, 170, 0, Motion.Exit, delegate
            {
                removingCards.Remove(card);
                if (cards.Children.Contains(card)) cards.Children.Remove(card);
                if (empty != null)
                    empty.Visibility = !showingText && cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SaveImages();
            });
        }

        // ================================================================ 页签

        UIElement BuildTabHost(StackPanel tabs)
        {
            tabPillShift = new TranslateTransform(0, 0);
            tabPill = new Border
            {
                Width = 58,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransform = tabPillShift,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(124, 108, 242), 0),
                        new GradientStop(Color.FromRgb(96, 148, 246), 1)
                    },
                    new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(124, 108, 242), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 }
            };
            var host = new Grid();
            host.Children.Add(tabPill);
            host.Children.Add(tabs);
            return host;
        }

        void MoveTabPill(int mode, bool animate)
        {
            if (tabPill == null || tabPillShift == null) return;
            double target = Math.Max(0, mode) * 58;
            if (!tabPillPlaced)
            {
                tabPillPlaced = true;
                tabPillShift.X = target;
                Motion.AnimateDouble(tabPill, UIElement.OpacityProperty, 0, 1, 300, Motion.Enter, 80, null);
                return;
            }
            Motion.AnimateDouble(tabPillShift, TranslateTransform.XProperty, tabPillShift.X, target,
                animate ? 240 : 0, Motion.Drop, 0, null);
        }

        void FadeEmptyStates()
        {
            if (!expanded) return;
            if (empty != null && empty.Visibility == Visibility.Visible)
                Motion.PopIn(empty, new Point(0.5, 0.5), 0.98, 6, 260, 40, Motion.Enter);
            if (textEmpty != null && textEmpty.Visibility == Visibility.Visible)
                Motion.PopIn(textEmpty, new Point(0.5, 0.5), 0.98, 6, 260, 40, Motion.Enter);
        }

        void AnimateTextCards()
        {
            if (textList == null || !expanded) return;
            int index = 0;
            foreach (UIElement child in textList.Children)
            {
                FrameworkElement item = child as FrameworkElement;
                if (item == null) continue;
                Motion.PopIn(item, new Point(0.5, 0.35), 0.96, -12, 320, Math.Min(220, index * 24), Motion.Drop);
                index++;
                if (index >= 8) break;
            }
        }

        // ================================================================ 滚动

        void GlideImages(double delta)
        {
            if (scroller == null) return;
            if (imageGlide == null) imageGlide = new ScrollGlide(scroller, true);
            imageGlide.Nudge(delta);
        }

        void GlideText(double delta)
        {
            if (textScroller == null) return;
            if (textGlide == null) textGlide = new ScrollGlide(textScroller, false);
            textGlide.Nudge(delta);
        }

        void StopImageGlide()
        {
            if (imageGlide != null) imageGlide.Stop();
        }

        void StopGlides()
        {
            StopImageGlide();
            if (textGlide != null) textGlide.Stop();
        }
    }
}
