using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 动效基础层：只使用 WPF 自带的 DoubleAnimation / ColorAnimation / 关键帧。
    //
    // 三条硬规则：
    //   1) 只改变 Opacity、RenderTransform、颜色，绝不改布局属性（Width/Height/Margin），
    //      这样命中测试、右键拖出、OLE DoDragDrop 的路径完全不变；
    //   2) 动画结束时把终值写回基础值并清除动画，所以后面直接赋值
    //      （例如拖拽时 card.Opacity = 0.45）依旧立刻生效；
    //   3) Enabled 是应用内总开关（默认开启）。刻意不跟“最小化/最大化动画”这个系统开关联动：
    //      很多优化过的系统把它关了，但用户仍然希望应用内有动效；需要一键降级时改这里即可。
    // ---------------------------------------------------------------------
    internal static class Motion
    {
        // 统一节奏（毫秒）
        public const int TapMs = 90;      // 按压反馈
        public const int HoverMs = 160;   // 悬停
        public const int EnterMs = 260;   // 进入
        public const int ExitMs = 150;    // 退出

        public static readonly IEasingFunction Enter = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction Exit = new QuarticEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction Drop = new ExponentialEase { Exponent = 7, EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction Soft = new SineEase { EasingMode = EasingMode.EaseInOut };
        public static readonly IEasingFunction Spring = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };

        static readonly bool allowed = true;

        public static bool Enabled { get { return allowed; } }

        static bool ReadAllowed()
        {
            try { return System.Windows.SystemParameters.ClientAreaAnimation; }
            catch (Exception) { return true; }
        }

        sealed class Slot
        {
            public readonly Dictionary<DependencyProperty, int> Version = new Dictionary<DependencyProperty, int>();
            public TransformGroup Group;
            public ScaleTransform Scale;
            public TranslateTransform Move;
        }

        static readonly ConditionalWeakTable<DependencyObject, Slot> Slots =
            new ConditionalWeakTable<DependencyObject, Slot>();

        static Slot MakeSlot(DependencyObject key) { return new Slot(); }

        static Slot SlotOf(DependencyObject target) { return Slots.GetValue(target, MakeSlot); }

        static IAnimatable AnimationTarget(DependencyObject target)
        {
            return target as IAnimatable;
        }

        // ------------------------------------------------------------ 基础

        /// <summary>给一个 double 依赖属性做带版本号的一次性动画，结束时把终值写回基础值。</summary>
        public static void AnimateDouble(DependencyObject target, DependencyProperty property, double from, double to,
            int milliseconds, IEasingFunction easing, int delayMilliseconds, Action completed)
        {
            if (target == null || property == null) return;
            if (!Enabled || milliseconds <= 0)
            {
                try { target.SetValue(property, to); } catch (Exception) { }
                if (completed != null) { try { completed(); } catch (Exception) { } }
                return;
            }

            Slot slot = SlotOf(target);
            int version;
            slot.Version.TryGetValue(property, out version);
            version++;
            slot.Version[property] = version;

            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
            {
                EasingFunction = easing == null ? Enter : easing,
                BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delayMilliseconds)),
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += delegate
            {
                int current;
                if (!SlotOf(target).Version.TryGetValue(property, out current) || current != version) return;
                try
                {
                    target.SetValue(property, to);
                    IAnimatable animatable = AnimationTarget(target);
                    if (animatable != null) animatable.BeginAnimation(property, null);
                }
                catch (Exception) { }
                if (completed != null) { try { completed(); } catch (Exception) { } }
            };

            try
            {
                target.SetValue(property, from);
                IAnimatable animatable = AnimationTarget(target);
                if (animatable != null) animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception) { }
        }

        public static void Stop(DependencyObject target, DependencyProperty property)
        {
            if (target == null || property == null) return;
            IAnimatable animatable = AnimationTarget(target);
            if (animatable == null) return;
            try { animatable.BeginAnimation(property, null); } catch (Exception) { }
        }

        public static void Delay(int milliseconds, Action action)
        {
            if (action == null) return;
            if (!Enabled || milliseconds <= 0) { try { action(); } catch (Exception) { } return; }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate
            {
                timer.Stop();
                try { action(); } catch (Exception) { }
            };
            timer.Start();
        }

        // ------------------------------------------------------- 位移 / 缩放

        public static void Prepare(FrameworkElement element, Point? origin)
        {
            if (element == null) return;
            Slot slot = SlotOf(element);
            if (slot.Group == null)
            {
                slot.Scale = new ScaleTransform(1, 1);
                slot.Move = new TranslateTransform(0, 0);
                var group = new TransformGroup();
                group.Children.Add(slot.Scale);
                group.Children.Add(slot.Move);
                slot.Group = group;
                element.RenderTransform = group;
            }
            if (origin.HasValue) element.RenderTransformOrigin = origin.Value;
        }

        public static ScaleTransform ScaleOf(FrameworkElement element, Point? origin)
        {
            Prepare(element, origin);
            return SlotOf(element).Scale;
        }

        public static TranslateTransform MoveOf(FrameworkElement element)
        {
            Prepare(element, null);
            return SlotOf(element).Move;
        }

        public static void ScaleTo(FrameworkElement element, double scale, int milliseconds, IEasingFunction easing,
            int delay, Point? origin, Action completed)
        {
            if (element == null) return;
            ScaleTransform transform = ScaleOf(element, origin);
            AnimateDouble(transform, ScaleTransform.ScaleXProperty, transform.ScaleX, scale, milliseconds, easing, delay, null);
            AnimateDouble(transform, ScaleTransform.ScaleYProperty, transform.ScaleY, scale, milliseconds, easing, delay, completed);
        }

        public static void MoveTo(FrameworkElement element, double x, double y, int milliseconds, IEasingFunction easing, int delay)
        {
            if (element == null) return;
            TranslateTransform transform = MoveOf(element);
            AnimateDouble(transform, TranslateTransform.XProperty, transform.X, x, milliseconds, easing, delay, null);
            AnimateDouble(transform, TranslateTransform.YProperty, transform.Y, y, milliseconds, easing, delay, null);
        }

        public static void Fade(UIElement element, double to, int milliseconds, IEasingFunction easing, int delay, Action completed)
        {
            if (element == null) return;
            AnimateDouble(element, UIElement.OpacityProperty, element.Opacity, to, milliseconds, easing, delay, completed);
        }

        // ------------------------------------------------------- 组合动作

        /// <summary>入场：从 from 缩放 + 纵向偏移回到原位，同时淡入。</summary>
        public static void PopIn(FrameworkElement element, Point origin, double scaleFrom, double offsetY,
            int milliseconds, int delay, IEasingFunction easing)
        {
            if (element == null) return;
            Prepare(element, origin);
            Slot slot = SlotOf(element);
            AnimateDouble(slot.Scale, ScaleTransform.ScaleXProperty, scaleFrom, 1, milliseconds, easing, delay, null);
            AnimateDouble(slot.Scale, ScaleTransform.ScaleYProperty, scaleFrom, 1, milliseconds, easing, delay, null);
            AnimateDouble(slot.Move, TranslateTransform.YProperty, offsetY, 0, milliseconds, easing, delay, null);
            // 透明度用不带过冲的缓动，BackEase 会让 alpha 冲出 1。
            AnimateDouble(element, UIElement.OpacityProperty, 0, 1, Math.Max(90, milliseconds - 70), Enter, delay, null);
        }

        /// <summary>退场：快速缩小 / 上移 / 淡出，完成后回调（通常用来设 Visibility）。</summary>
        public static void PopOut(FrameworkElement element, double scaleTo, double offsetY,
            int milliseconds, int delay, IEasingFunction easing, Action completed)
        {
            if (element == null) return;
            Prepare(element, null);
            Slot slot = SlotOf(element);
            AnimateDouble(slot.Scale, ScaleTransform.ScaleXProperty, slot.Scale.ScaleX, scaleTo, milliseconds, easing, delay, null);
            AnimateDouble(slot.Scale, ScaleTransform.ScaleYProperty, slot.Scale.ScaleY, scaleTo, milliseconds, easing, delay, null);
            AnimateDouble(slot.Move, TranslateTransform.YProperty, slot.Move.Y, offsetY, milliseconds, easing, delay, null);
            AnimateDouble(element, UIElement.OpacityProperty, element.Opacity, 0, Math.Max(90, milliseconds), easing, delay, completed);
        }

        /// <summary>颜色过渡；画笔必须是非冻结的自有实例。</summary>
        public static void ColorTo(SolidColorBrush brush, Color to, int milliseconds, IEasingFunction easing)
        {
            if (brush == null || brush.IsFrozen) return;
            if (!Enabled || milliseconds <= 0)
            {
                try { brush.Color = to; } catch (Exception) { }
                return;
            }
            var animation = new ColorAnimation(brush.Color, to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
            {
                EasingFunction = easing == null ? Enter : easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            IAnimatable animatable = brush;
            try { animatable.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace); }
            catch (Exception) { }
        }

        /// <summary>弹出菜单一类小窗口的入场（内容整体淡入 + 轻微上浮）。</summary>
        public static void WindowIn(Window window, FrameworkElement surface)
        {
            if (window == null || surface == null) return;
            Prepare(surface, new Point(0.5, 0.5));
            Slot slot = SlotOf(surface);
            surface.Opacity = 0;
            slot.Scale.ScaleX = 0.965;
            slot.Scale.ScaleY = 0.965;
            slot.Move.Y = 8;

            RoutedEventHandler handler = null;
            handler = delegate
            {
                window.Loaded -= handler;
                if (!Enabled)
                {
                    surface.Opacity = 1;
                    slot.Scale.ScaleX = 1;
                    slot.Scale.ScaleY = 1;
                    slot.Move.Y = 0;
                    return;
                }
                AnimateDouble(surface, UIElement.OpacityProperty, 0, 1, 150, Enter, 0, null);
                AnimateDouble(slot.Scale, ScaleTransform.ScaleXProperty, 0.965, 1, 220, Drop, 0, null);
                AnimateDouble(slot.Scale, ScaleTransform.ScaleYProperty, 0.965, 1, 220, Drop, 0, null);
                AnimateDouble(slot.Move, TranslateTransform.YProperty, 8, 0, 220, Drop, 0, null);
            };
            window.Loaded += handler;
        }
    }

    // ---------------------------------------------------------------------
    // 滚轮平滑滚动：只改 ScrollViewer 的偏移量，不碰布局，也不接管鼠标。
    // ---------------------------------------------------------------------
    internal sealed class ScrollGlide
    {
        readonly ScrollViewer viewer;
        readonly bool horizontal;
        bool running;
        bool primed;
        double target;
        DateTime lastFrame;

        public ScrollGlide(ScrollViewer viewer, bool horizontal)
        {
            this.viewer = viewer;
            this.horizontal = horizontal;
        }

        double Current
        {
            get
            {
                if (viewer == null) return 0;
                return horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
            }
        }

        public void Nudge(double delta)
        {
            if (viewer == null) return;
            if (!primed)
            {
                target = Current;
                primed = true;
            }
            double extent = horizontal ? viewer.ExtentWidth : viewer.ExtentHeight;
            double viewport = horizontal ? viewer.ViewportWidth : viewer.ViewportHeight;
            double max = Math.Max(0, extent - viewport);
            target = Math.Max(0, Math.Min(max, target + delta));
            if (Math.Abs(target - Current) < 0.5) return;
            if (!Motion.Enabled)
            {
                Apply(target);
                return;
            }
            if (!running)
            {
                running = true;
                lastFrame = DateTime.UtcNow;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        public void Stop()
        {
            if (running)
            {
                running = false;
                CompositionTarget.Rendering -= OnFrame;
            }
            primed = false;
            target = Current;
        }

        void OnFrame(object sender, EventArgs e)
        {
            double dt = (DateTime.UtcNow - lastFrame).TotalMilliseconds;
            lastFrame = DateTime.UtcNow;
            if (dt <= 0) return;
            if (dt > 40) dt = 40;
            double current = Current;
            double diff = target - current;
            if (Math.Abs(diff) < 0.6)
            {
                Apply(target);
                Stop();
                return;
            }
            Apply(current + diff * (1 - Math.Exp(-dt / 48.0)));
        }

        void Apply(double value)
        {
            if (viewer == null) return;
            try
            {
                if (horizontal) viewer.ScrollToHorizontalOffset(value);
                else viewer.ScrollToVerticalOffset(value);
            }
            catch (Exception) { }
        }
    }
}