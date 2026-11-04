using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IslandShot
{
    /// <summary>眼睛表情。新增表情只要往下面的表里加一条路径，不用改动画代码。</summary>
    internal enum EyeExpression
    {
        Open,     // 竖条睁眼（默认）
        Happy,    // 眯眼笑（竖条微微外弯）
        Sleepy,   // 半睁
        Closed    // 闭眼（横线）
    }

    /// <summary>
    /// 胶囊顶部的一双卡通眼睛：竖向粗线条、纯色深蓝、无渐变无虚化，左右对称。
    ///
    /// 造型（对应手机壁纸表情里那种干净的单线描边）：
    ///   睁眼  = 两条竖直的圆头条（单笔画，圆头圆尾）
    ///   闭眼  = 两条横向短线
    ///   眨眼  = 竖条压扁 + 横线淡入，再反向回来
    ///
    /// 布局：内部是一块 60×40 的设计画布，外套 Viewbox，宿主给多大就等比画多大，
    /// 所以胶囊放大/缩小时眼睛跟着一起缩，不会错位、也不会被圆角裁掉。
    ///
    /// 路径全部用 XAML Path 语法（`Data="M ..."`）写成字符串，见下面的 Expressions 表；
    /// 本项目没有 XAML 编译器，所以这里直接用 Geometry.Parse 解析同一份数据。
    /// </summary>
    internal sealed class EyeBlinkControl : UserControl
    {
        // 设计画布：两条竖条分别位于 x=15 / x=45，高 32，线宽 11（圆头），整体 60×40。
        const double DesignWidth = 60;
        const double DesignHeight = 40;
        const double StrokeThickness = 13;
        const double ClosedThickness = 9;

        // -------------------- 表情表（XAML Path 的 Data 全部写在这里） --------------------
        static readonly Dictionary<EyeExpression, string[]> Expressions =
            new Dictionary<EyeExpression, string[]>
            {
                // 竖条睁眼：左右对称的两条竖线
                { EyeExpression.Open, new[] { "M 14,4 L 14,36", "M 46,4 L 46,36" } },
                // 眯眼笑：竖条缩短并略向外弯
                { EyeExpression.Happy, new[] { "M 14,9 C 12,16 12,24 14,31", "M 46,9 C 48,16 48,24 46,31" } },
                // 半睁
                { EyeExpression.Sleepy, new[] { "M 14,13 L 14,30", "M 46,13 L 46,30" } },
                // 闭眼（静表情时用；眨眼过程中由 closedLayer 负责）
                { EyeExpression.Closed, new[] { "M 14,18 L 14,22", "M 46,18 L 46,22" } }
            };

        // 闭眼叠加层：两条横向短线（眨眼中间那一帧用它）
        const string ClosedLeft = "M 8,20 L 20,20";
        const string ClosedRight = "M 40,20 L 52,20";

        // ---- 可调参数 ----
        double sizeRatio = 0.56;          // 眼睛高度 / 胶囊高度
        int intervalMinMs = 2000;         // 眨眼间隔下限
        int intervalMaxMs = 6000;         // 眨眼间隔上限
        int blinkDurationMs = 300;        // 一次眨眼的完整周期
        int closedHoldMs = 100;           // 闭眼保持时长
        Color eyeColor = Color.FromRgb(94, 200, 255);   // 浅蓝
        bool blinking = true;
        bool eyesEnabled = true;
        EyeExpression expression = EyeExpression.Open;

        readonly Path leftEye;
        readonly Path rightEye;
        readonly Canvas openLayer;
        readonly Canvas closedLayer;
        readonly ScaleTransform blinkScale = new ScaleTransform(1, 1);
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Random random = new Random();
        readonly SolidColorBrush brush;

        Storyboard storyboard;
        bool storyboardDirty = true;
        bool animating;
        bool loaded;

        public EyeBlinkControl()
        {
            // 眼睛只是装饰，绝不能吃掉鼠标事件（拖拽、点击展开全靠外面那层 Border）。
            IsHitTestVisible = false;
            Focusable = false;
            Background = null;          // 背景透明

            brush = new SolidColorBrush(eyeColor);
            // 不 Freeze：EyeColor 改了要能立刻反映到已画好的路径上。

            leftEye = CreateBar();
            rightEye = CreateBar();

            openLayer = new Canvas { Width = DesignWidth, Height = DesignHeight, IsHitTestVisible = false };
            openLayer.Children.Add(leftEye);
            openLayer.Children.Add(rightEye);

            closedLayer = new Canvas { Width = DesignWidth, Height = DesignHeight, IsHitTestVisible = false, Opacity = 0 };
            closedLayer.Children.Add(CreateDash(ClosedLeft));
            closedLayer.Children.Add(CreateDash(ClosedRight));

            // 注意：这里故意不加 DropShadowEffect —— 要求是"无虚化"。
            var eyes = new Grid
            {
                RenderTransform = blinkScale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
                Background = null
            };
            eyes.Children.Add(openLayer);
            eyes.Children.Add(closedLayer);

            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                IsHitTestVisible = false,
                Child = eyes
            };

            ApplyExpression(expression);

            timer.Tick += OnBlinkTick;
            Loaded += delegate { loaded = true; ScheduleNext(); };
            Unloaded += delegate { loaded = false; timer.Stop(); };
        }

        Path CreateBar()
        {
            return new Path
            {
                Stroke = brush,
                StrokeThickness = StrokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
        }

        Path CreateDash(string data)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = brush,
                StrokeThickness = ClosedThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
        }

        // ------------------------------ 表情接口 ------------------------------

        /// <summary>
        /// 切换表情。新增表情：在 Expressions 表里加一条路径，再在 EyeExpression 里加一个值即可。
        /// 眨眼动画与表情互不影响：眨完会自动回到当前表情。
        /// </summary>
        public EyeExpression Expression
        {
            get { return expression; }
            set
            {
                if (expression == value) return;
                expression = value;
                if (!animating) ApplyExpression(value);
            }
        }

        void ApplyExpression(EyeExpression value)
        {
            string[] paths;
            if (!Expressions.TryGetValue(value, out paths)) paths = Expressions[EyeExpression.Open];
            leftEye.Data = Geometry.Parse(paths[0]);
            rightEye.Data = Geometry.Parse(paths[1]);

            if (value == EyeExpression.Closed)
            {
                openLayer.Opacity = 0;
                closedLayer.Opacity = 1;
            }
            else
            {
                openLayer.Opacity = 1;
                closedLayer.Opacity = 0;
            }
            blinkScale.ScaleY = 1;
        }

        // ------------------------------ 可调参数 ------------------------------

        /// <summary>眼睛高度 / 胶囊高度。默认 0.56（宽度按 60:40 自动推出）。</summary>
        public double SizeRatio
        {
            get { return sizeRatio; }
            set { sizeRatio = Math.Max(0.1, value); }
        }

        /// <summary>眨眼间隔下限（毫秒），默认 2000。</summary>
        public int IntervalMinMs
        {
            get { return intervalMinMs; }
            set { intervalMinMs = Math.Max(300, value); }
        }

        /// <summary>眨眼间隔上限（毫秒），默认 6000。</summary>
        public int IntervalMaxMs
        {
            get { return intervalMaxMs; }
            set { intervalMaxMs = Math.Max(intervalMinMs, value); }
        }

        /// <summary>一次眨眼的完整周期（毫秒），默认 300。</summary>
        public int BlinkDurationMs
        {
            get { return blinkDurationMs; }
            set { blinkDurationMs = Math.Max(120, value); storyboardDirty = true; }
        }

        /// <summary>闭眼保持时长（毫秒），默认 100。</summary>
        public int ClosedHoldMs
        {
            get { return closedHoldMs; }
            set { closedHoldMs = Math.Max(20, value); storyboardDirty = true; }
        }

        /// <summary>眼睛颜色（纯色，不参与任何渐变/阴影）。</summary>
        public Color EyeColor
        {
            get { return eyeColor; }
            set
            {
                eyeColor = value;
                if (brush != null) brush.Color = value;
            }
        }

        /// <summary>眨眼总开关：false 时立刻恢复当前表情并停表。</summary>
        public bool Blinking
        {
            get { return blinking; }
            set
            {
                if (blinking == value) return;
                blinking = value;
                if (blinking) ScheduleNext();
                else
                {
                    timer.Stop();
                    if (storyboard != null) storyboard.Stop(this);
                    animating = false;
                    ApplyExpression(expression);
                }
            }
        }

        /// <summary>
        /// 配置项 EyesEnabled：整双眼睛显示/隐藏。
        /// 关掉时立刻停止眨眼并隐藏控件（胶囊上什么都不画），重新打开会从当前表情继续眨眼。
        /// </summary>
        public bool EyesEnabled
        {
            get { return eyesEnabled; }
            set
            {
                if (eyesEnabled == value) return;
                eyesEnabled = value;
                if (value)
                {
                    Visibility = Visibility.Visible;
                    blinking = true;
                    ApplyExpression(expression);
                    ScheduleNext();
                }
                else
                {
                    timer.Stop();
                    if (storyboard != null) storyboard.Stop(this);
                    animating = false;
                    ApplyExpression(expression);
                    Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>当前是否睁着眼（眨眼过程中为 false）。</summary>
        public bool IsOpen { get { return !animating; } }

        // ------------------------------ 动作 ------------------------------

        /// <summary>立刻眨一次眼（重复调用是安全的）。</summary>
        public void Blink()
        {
            if (!loaded || animating) return;
            if (!IsVisible) { ScheduleNext(); return; }
            if (!Motion.Enabled) return;   // 全局关掉动效时保持睁眼
            if (!eyesEnabled) return;      // 配置项关掉了就不眨眼
            if (expression == EyeExpression.Closed) return;   // 已经是闭眼表情

            animating = true;
            timer.Stop();
            EnsureStoryboard().Begin(this, true);
        }

        /// <summary>打开自动眨眼。</summary>
        public void StartBlinking() { Blinking = true; }

        /// <summary>关闭自动眨眼（保持当前表情）。</summary>
        public void StopBlinking() { Blinking = false; }

        // ------------------------------ 内部实现 ------------------------------

        void OnBlinkTick(object sender, EventArgs e)
        {
            timer.Stop();
            Blink();
        }

        void ScheduleNext()
        {
            timer.Stop();
            if (!loaded || !blinking || animating) return;
            int min = Math.Max(300, intervalMinMs);
            int max = Math.Max(min + 1, intervalMaxMs);
            timer.Interval = TimeSpan.FromMilliseconds(min + random.Next(max - min));
            timer.Start();
        }

        Storyboard EnsureStoryboard()
        {
            if (storyboard != null && !storyboardDirty) return storyboard;

            int closeMs = Math.Max(40, (blinkDurationMs - closedHoldMs) / 2);
            int holdEnd = closeMs + closedHoldMs;
            int openEnd = Math.Max(holdEnd + 40, blinkDurationMs);

            var board = new Storyboard();
            AddTrack(board, openLayer, 1, 0, closeMs, holdEnd, openEnd);
            AddTrack(board, closedLayer, 0, 1, closeMs, holdEnd, openEnd);
            AddSquash(board, closeMs, holdEnd, openEnd);

            board.Completed += delegate
            {
                // 落地：清掉动画、按当前表情写回基础值，再排下一次。
                animating = false;
                openLayer.BeginAnimation(OpacityProperty, null);
                closedLayer.BeginAnimation(OpacityProperty, null);
                blinkScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ApplyExpression(expression);
                ScheduleNext();
            };

            storyboard = board;
            storyboardDirty = false;
            return storyboard;
        }

        // 睁眼/闭眼两条轨道：0 → 目标值(closeMs) → 保持(holdEnd) → 回到初值(openEnd)
        void AddTrack(Storyboard board, DependencyObject target, double from, double to, int closeMs, int holdEnd, int openEnd)
        {
            var frames = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(openEnd)),
                FillBehavior = FillBehavior.HoldEnd
            };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(closeMs)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdEnd))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(openEnd)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));

            Storyboard.SetTarget(frames, target);
            Storyboard.SetTargetProperty(frames, new PropertyPath(UIElement.OpacityProperty));
            board.Children.Add(frames);
        }

        // 眨眼时整体轻微压扁，配合横线淡入，像真的合了一下眼皮。
        void AddSquash(Storyboard board, int closeMs, int holdEnd, int openEnd)
        {
            var frames = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(openEnd)),
                FillBehavior = FillBehavior.HoldEnd
            };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(closeMs)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdEnd))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(openEnd)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));

            Storyboard.SetTarget(frames, blinkScale);
            Storyboard.SetTargetProperty(frames, new PropertyPath(ScaleTransform.ScaleYProperty));
            board.Children.Add(frames);
        }
    }
}