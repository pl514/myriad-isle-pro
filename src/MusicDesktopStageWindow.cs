using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 投射到桌面：全屏歌词舞台（Folia Sonnet 风格）
    //
    // 和旧版"三行桌面歌词"不同，这一版按 Folia 的 Sonnet 视觉语言重写：
    //   · 一整段歌词排成一张海报（散落排版、巨大的竖排主词、描边幽灵字）；
    //   · 逐词出现：每个词有自己的时间，按字符数从这一行的时间里分出来；
    //   · 镜头缓慢推拉/平移，换段时淡出+模糊+轻微缩放进下一张；
    //   · 背景是程序生成的植物线稿、轨道线、测量框和超大幽灵字母，
    //     配色从封面主色里取（取不到就用中性灰）。
    //
    // 窗口是真正的全屏不透明窗口：点击不会穿透到桌面（这是用户明确要求的）。
    // 控制条只在底部中央，Esc / 空格 / ←→ 都能用。
    //
    // 视觉参考：Folia (AGPL-3.0) 的 Sonnet visualizer。这里没有复制它的
    // Pixi/WebGL 代码，版式规则、镜头曲线和节奏是用 C# + WPF 重新实现的；
    // 字体和主题走 Windows 自带字体 / 中性 "Midnight Default" 配色。
    // ---------------------------------------------------------------------
    internal sealed class MusicDesktopStageWindow : Window
    {
        // ------------------------------------------------------------ 依赖
        readonly Func<MusicStageSnapshot> snapshotProvider;
        readonly Action togglePlay;
        readonly Action nextTrack;
        readonly Action previousTrack;
        readonly Action cycleMode;
        readonly Action<TimeSpan> seek;
        readonly Action closedCallback;
        readonly Action raiseHost;

        // ------------------------------------------------------------ 颜色
        static Color Ink = Color.FromRgb(244, 244, 245);
        static Color Dim = Color.FromRgb(132, 135, 145);
        static Color BgTop = Color.FromRgb(22, 23, 29);
        static Color BgBottom = Color.FromRgb(5, 5, 8);
        static readonly Color DefaultAccent = Color.FromRgb(198, 200, 208);
        Color accent = DefaultAccent;
        MusicStageSettings settings;
        MusicStageStyle stageStyle;
        Border stylePanelRoot;
        Border stylePanelDimmer;
        bool stylePanelVisible;
        MusicStageStylePanel stylePanel;
        Rectangle stageDim;
        TextBlock subtitleText;

        // ------------------------------------------------------------ 根视觉
        readonly Grid root;
        readonly Canvas shotHost;
        readonly Canvas frameLayer;
        readonly Canvas chromeLayer;
        readonly Rectangle grain;
        readonly Rectangle vignette;
        readonly ScaleTransform cameraScale;
        readonly RotateTransform cameraRotate;
        readonly TranslateTransform cameraTranslate;

        // ------------------------------------------------------------ 控制条
        Border bar;
        Border playButton;
        TextBlock titleText;
        TextBlock leftTime;
        TextBlock rightTime;
        Grid progressHost;
        Border progressFill;
        Ellipse progressKnob;
        Border modeButton;
        Border expandButton;
        Border closeButton;
        Border infoCard;
        Border infoCoverHost;
        TextBlock infoTitle;
        TextBlock infoArtist;
        TextBlock infoAlbum;
        TextBlock hintText;
        TextBlock themeText;
        bool infoVisible;
        bool seeking;
        double seekRatio;

        // ------------------------------------------------------------ 状态
        readonly DispatcherTimer tick;
        readonly DispatcherTimer cursorTimer;
        DateTime openedAt = DateTime.UtcNow;
        MusicStageSnapshot last;
        List<MusicStageShot> program = new List<MusicStageShot>();
        int activeIndex = -1;
        ShotVisual activeVisual;
        string songKey = "\0";
        int lyricsVersion = -1;
        int lyricsCount = -1;
        double lastDuration = -1;
        double lastWidth;
        double lastHeight;
        double currentPos;
        double wall;
        bool sizeDirty;
        bool closed;
        bool chromeHidden;
        string lastPlayIcon = "";
        string lastModeIcon = "";
        string albumKey = "";
        bool tintPending;
        bool suppressTintRebuild;
        bool appliedCoverTint;

        // 镜头当前值（切段时平滑过去，避免硬跳）
        double camX;
        double camY;
        double camScale = 1;
        double camRotation;

        public MusicDesktopStageWindow(Func<MusicStageSnapshot> snapshotProvider, Action togglePlay,
            Action nextTrack, Action previousTrack, Action<TimeSpan> seek, Action cycleMode, Action closedCallback,
            Action raiseHost)
        {
            this.snapshotProvider = snapshotProvider;
            this.togglePlay = togglePlay;
            this.nextTrack = nextTrack;
            this.previousTrack = previousTrack;
            this.seek = seek;
            this.cycleMode = cycleMode;
            this.closedCallback = closedCallback;
            this.raiseHost = raiseHost;

            settings = MusicStageSettings.Load();
            stageStyle = MusicStageStyles.Find(settings.StyleId);
            Ink = stageStyle.Ink;
            Dim = stageStyle.Dim;
            BgTop = stageStyle.BgTop;
            BgBottom = stageStyle.BgBottom;
            accent = stageStyle.Accent;
            MusicStageText.SetFontKind(stageStyle.FontKind);

            Title = "投射到桌面";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = new SolidColorBrush(BgBottom);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            FontFamily = MusicStageText.UiFamily;
            Cursor = Cursors.Arrow;

            root = new Grid { Background = new SolidColorBrush(Color.FromRgb(7, 7, 10)), ClipToBounds = true };

            shotHost = new Canvas { Width = 1920, Height = 1080, ClipToBounds = true, IsHitTestVisible = false };
            cameraScale = new ScaleTransform(1, 1, 960, 540);
            cameraRotate = new RotateTransform(0, 960, 540);
            cameraTranslate = new TranslateTransform(0, 0);
            var camera = new TransformGroup();
            camera.Children.Add(cameraScale);
            camera.Children.Add(cameraRotate);
            camera.Children.Add(cameraTranslate);
            shotHost.RenderTransform = camera;
            root.Children.Add(shotHost);

            grain = new Rectangle
            {
                Width = 1920,
                Height = 1080,
                Fill = GrainBrush(),
                Opacity = 0.32,
                IsHitTestVisible = false
            };
            root.Children.Add(grain);

            vignette = new Rectangle
            {
                Width = 1920,
                Height = 1080,
                Fill = VignetteBrush(),
                IsHitTestVisible = false
            };
            root.Children.Add(vignette);

            frameLayer = new Canvas { Width = 1920, Height = 1080, IsHitTestVisible = false };
            root.Children.Add(frameLayer);

            stageDim = new Rectangle
            {
                Width = 1920,
                Height = 1080,
                Fill = Brushes.Black,
                Opacity = 0,
                IsHitTestVisible = false
            };
            root.Children.Add(stageDim);

            chromeLayer = new Canvas { Width = 1920, Height = 1080 };
            root.Children.Add(chromeLayer);

            Content = root;
            BuildChrome();

            tick = new DispatcherTimer(DispatcherPriority.Render);
            tick.Interval = TimeSpan.FromMilliseconds(33);
            tick.Tick += delegate { OnTick(); };

            cursorTimer = new DispatcherTimer();
            cursorTimer.Interval = TimeSpan.FromSeconds(3.2);
            cursorTimer.Tick += delegate
            {
                cursorTimer.Stop();
                if (!closed) Cursor = Cursors.None;
            };

            Loaded += delegate
            {
                openedAt = DateTime.UtcNow;
                ApplyBounds();
                BuildFrame();
                PositionChrome();
                RebuildAll(true);
                tick.Start();
                Activate();
                Focus();
                FadeIn();
            };

            SizeChanged += delegate
            {
                ApplyBounds();
                PositionChrome();
                BuildFrame();
                sizeDirty = true;
            };

            PreviewKeyDown += OnPreviewKeyDown;
            Activated += delegate
            {
                // 投射窗抢到焦点时，把胶囊所在的宿主窗口重新放到 Topmost 最上层，
                // 但不要抢焦点（SWP_NOACTIVATE）。这样胶囊始终露在投射层上面，
                // 同时键盘输入仍然留在投射窗里。
                if (raiseHost != null)
                {
                    try { raiseHost(); } catch (Exception) { }
                }
            };
            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnBackgroundClick;
            MouseDoubleClick += OnDoubleClick;
            Deactivated += delegate { if (infoVisible) SetInfoVisible(false); };
            Closed += delegate
            {
                closed = true;
                tick.Stop();
                cursorTimer.Stop();
                Cursor = Cursors.Arrow;
                if (closedCallback != null)
                {
                    Action callback = closedCallback;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { callback(); } catch (Exception) { }
                    }));
                }
            };
        }

        // ================================================================ 尺寸 / 窗框

        void ApplyBounds()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            if (sw <= 0 || sh <= 0) return;
            if (Math.Abs(Left) > 0.5 || Math.Abs(Top) > 0.5)
            {
                Left = 0;
                Top = 0;
            }
            if (Math.Abs(Width - sw) > 0.5) Width = sw;
            if (Math.Abs(Height - sh) > 0.5) Height = sh;
        }

        double W
        {
            get { return ActualWidth > 10 ? ActualWidth : (Width > 10 ? Width : 1920); }
        }

        double H
        {
            get { return ActualHeight > 10 ? ActualHeight : (Height > 10 ? Height : 1080); }
        }

        void RebuildAll(bool immediate)
        {
            if (settings != null)
            {
                stageStyle = MusicStageStyles.Find(settings.StyleId);
                MusicStageText.SetFontKind(stageStyle.FontKind);
                Ink = stageStyle.Ink;
                Dim = stageStyle.Dim;
                BgTop = stageStyle.BgTop;
                BgBottom = stageStyle.BgBottom;
                if (!settings.CoverTint) accent = stageStyle.Accent;
                if (stageDim != null) stageDim.Opacity = MusicStageUtil.Clamp(settings.BackgroundDim, 0, 0.85);
                if (vignette != null) vignette.Opacity = stageStyle.IsLight ? 0.45 : 0.8;
            }
            last = snapshotProvider == null ? null : SafeSnapshot();
            program = MusicStageProgram.Build(last, W, H, settings);
            activeIndex = -1;
            activeVisual = null;
            shotHost.Children.Clear();
            if (last != null)
            {
                songKey = SongKey(last);
                lyricsVersion = last.LyricsVersion;
                lyricsCount = last.Lyrics == null ? 0 : last.Lyrics.Count;
                lastDuration = last.Duration.TotalSeconds;
                LoadAlbum(last.AlbumMid);
                UpdateChrome(last);
            }
            lastWidth = W;
            lastHeight = H;
            sizeDirty = false;
            currentPos = last == null ? 0 : last.Position.TotalSeconds;
            ShowShot(FindShotIndex(currentPos), currentPos, immediate);
        }

        MusicStageSnapshot SafeSnapshot()
        {
            try { return snapshotProvider(); }
            catch (Exception) { return null; }
        }

        static string SongKey(MusicStageSnapshot s)
        {
            if (s == null) return "";
            return (s.SongMid == null ? "" : s.SongMid) + "|" +
                   (s.Title == null ? "" : s.Title) + "|" +
                   (s.Artist == null ? "" : s.Artist) + "|" +
                   (s.AlbumMid == null ? "" : s.AlbumMid) + "|" +
                   s.LyricsVersion.ToString(CultureInfo.InvariantCulture) + "|" +
                   (s.Lyrics == null ? 0 : s.Lyrics.Count);
        }

        // ================================================================ 主循环

        void OnTick()
        {
            if (closed) return;
            MusicStageSnapshot s = SafeSnapshot();
            if (s == null) return;
            last = s;
            wall = (DateTime.UtcNow - openedAt).TotalSeconds;
            currentPos = s.Position.TotalSeconds;

            string key = SongKey(s);
            if (key != songKey && settings != null && settings.RandomPerSong && !string.IsNullOrEmpty(s.SongMid))
            {
                int pick = (int)(MusicStageUtil.Hash01(s.SongMid + "|random-style", 0) * MusicStageStyles.All.Count);
                if (pick < 0) pick = 0;
                if (pick >= MusicStageStyles.All.Count) pick = MusicStageStyles.All.Count - 1;
                settings.StyleId = MusicStageStyles.All[pick].Id;
                settings.ThemeName = MusicStageStyles.All[pick].Name;
                settings.Save();
            }
            if (key != songKey || lyricsVersion != s.LyricsVersion || lyricsCount != (s.Lyrics == null ? 0 : s.Lyrics.Count)
                || sizeDirty)
            {
                bool bigChange = key != songKey;
                RebuildAll(bigChange);
            }
            else if (lastDuration <= 0 && s.Duration.TotalSeconds > 0.2)
            {
                lastDuration = s.Duration.TotalSeconds;
                program = MusicStageProgram.Build(s, W, H, settings);
                activeIndex = -1;
                ShowShot(FindShotIndex(currentPos), currentPos, true);
            }

            UpdateChrome(s);
            UpdateSubtitle();
            int index = FindShotIndex(currentPos);
            if (index != activeIndex)
            {
                ShowShot(index, currentPos, false);
            }
            if (activeVisual != null)
            {
                UpdateCamera(program[activeIndex], currentPos);
                UpdateWords(activeVisual, currentPos);
                UpdateParticles(activeVisual, wall);
                UpdateFireworks(activeVisual, wall);
            }
        }

        int FindShotIndex(double position)
        {
            if (program.Count == 0) return -1;
            for (int i = program.Count - 1; i >= 0; i--)
            {
                if (position >= program[i].Start) return i;
            }
            return 0;
        }

        // ================================================================ 切段

        void ShowShot(int index, double position, bool immediate)
        {
            if (index < 0 || index >= program.Count) return;
            if (settings != null && !settings.Transitions) immediate = true;
            if (index == activeIndex && activeVisual != null) return;
            MusicStageShot shot = program[index];
            if (activeVisual != null)
            {
                if (immediate)
                {
                    shotHost.Children.Remove(activeVisual.Root);
                }
                else
                {
                    FadeOutShot(activeVisual);
                }
            }
            activeIndex = index;
            activeVisual = BuildShotVisual(shot);
            shotHost.Children.Add(activeVisual.Root);
            if (immediate)
            {
                activeVisual.Root.Opacity = 1;
                activeVisual.EntryScale.ScaleX = 1;
                activeVisual.EntryScale.ScaleY = 1;
                if (activeVisual.Blur != null) activeVisual.Blur.Radius = 0;
            }
            else
            {
                activeVisual.Root.Opacity = 0;
                activeVisual.EntryScale.ScaleX = 1.025;
                activeVisual.EntryScale.ScaleY = 1.025;
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                activeVisual.Root.BeginAnimation(OpacityProperty, fade);
                var scale = new DoubleAnimation(1.025, 1, TimeSpan.FromMilliseconds(700));
                scale.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                activeVisual.EntryScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                activeVisual.EntryScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
                if (activeVisual.Blur != null)
                {
                    var blur = new DoubleAnimation(11, 0, TimeSpan.FromMilliseconds(520));
                    blur.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    activeVisual.Blur.BeginAnimation(BlurEffect.RadiusProperty, blur);
                }
            }
        }

        void FadeOutShot(ShotVisual visual)
        {
            var old = visual;
            var fade = new DoubleAnimation(old.Root.Opacity, 0, TimeSpan.FromMilliseconds(330));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            fade.Completed += delegate
            {
                try { shotHost.Children.Remove(old.Root); } catch (Exception) { }
            };
            old.Root.BeginAnimation(OpacityProperty, fade);
            if (old.Blur != null)
            {
                var blur = new DoubleAnimation(0, 9, TimeSpan.FromMilliseconds(330));
                old.Blur.BeginAnimation(BlurEffect.RadiusProperty, blur);
            }
        }

        // ================================================================ 镜头

        void UpdateCamera(MusicStageShot shot, double position)
        {
            double span = shot.End - shot.Start;
            if (span <= 0.001 || double.IsInfinity(span)) span = 8;
            double p = MusicStageUtil.Clamp((position - shot.Start) / span, 0, 1);
            double m = CameraProgress(shot.Kind, p);
            double x = 0, y = 0, scale = 1, rotation = 0;
            switch (shot.Kind)
            {
                case "editorial-column":
                    x = -0.055 + m * 0.095;
                    y = 0.025 - m * 0.04;
                    scale = 0.98 + m * 0.07;
                    rotation = -0.006 + m * 0.01;
                    break;
                case "type-impact":
                    x = -0.035 + m * 0.07;
                    y = 0.018 - m * 0.028;
                    scale = 1 + (1 - MusicStageUtil.EaseOutExpo(Math.Min(1, p / 0.18))) * 0.22 + m * 0.08;
                    rotation = -0.01 + m * 0.016;
                    break;
                case "tracking-ribbon":
                    x = -0.16 + m * 0.28;
                    y = 0.05 - m * 0.085;
                    scale = 0.98 + m * 0.07;
                    rotation = 0.008 - m * 0.014;
                    break;
                case "mask-reveal":
                    x = 0.035 - m * 0.065;
                    y = 0.1 - m * 0.135;
                    scale = 0.96 + m * 0.12;
                    rotation = -0.006 + m * 0.009;
                    break;
                case "poster-blocks":
                    x = -0.012 + m * 0.024;
                    y = 0.008 - m * 0.016;
                    scale = 0.99 + m * 0.025;
                    rotation = -0.0015 + m * 0.003;
                    break;
                case "quiet-tableau":
                    x = -0.022 + m * 0.04;
                    y = 0.014 - m * 0.025;
                    scale = 1 + m * 0.028;
                    rotation = -0.002 + m * 0.003;
                    break;
                default: // fragment-collage
                    x = -0.045 + m * 0.085;
                    y = 0.028 - Math.Sin(m * Math.PI) * 0.055;
                    scale = 0.97 + m * 0.09;
                    rotation = -0.014 + m * 0.028;
                    break;
            }

            double breath = Math.Sin(wall * 0.085) * 0.0035;
            double intensity = settings == null ? 1.0 : MusicStageUtil.Clamp(settings.CameraIntensity, 0, 2);
            double targetX = shot.CameraX + (x + Math.Sin(wall * 0.11) * 0.004) * intensity;
            double targetY = shot.CameraY + (y + Math.Cos(wall * 0.09) * 0.003) * intensity;
            double targetScale = 1 + (shot.CameraZoom * scale - 1) * intensity;
            targetScale *= 1 + breath * intensity;
            double targetRotation = shot.CameraRotation + (rotation * 180.0 / Math.PI + Math.Sin(wall * 0.07) * 0.12) * intensity;
            if (settings != null && settings.BeatPush && !settings.ReduceMotion)
            {
                double beat = (Math.Sin(wall * Math.PI * 2 * 1.72) + 1) * 0.5;
                targetScale *= 1 + beat * 0.006 * MusicStageUtil.Clamp(settings.OverallGlow, 0, 2);
            }
            if (settings != null && settings.ReduceMotion)
            {
                targetX = shot.CameraX;
                targetY = shot.CameraY;
                targetScale = shot.CameraZoom;
                targetRotation = shot.CameraRotation;
            }

            camX = MusicStageUtil.Lerp(camX, targetX, 0.10);
            camY = MusicStageUtil.Lerp(camY, targetY, 0.10);
            camScale = MusicStageUtil.Lerp(camScale, targetScale, 0.10);
            camRotation = MusicStageUtil.Lerp(camRotation, targetRotation, 0.10);

            cameraScale.ScaleX = camScale;
            cameraScale.ScaleY = camScale;
            cameraScale.CenterX = W / 2;
            cameraScale.CenterY = H / 2;
            cameraRotate.CenterX = W / 2;
            cameraRotate.CenterY = H / 2;
            cameraRotate.Angle = camRotation;
            cameraTranslate.X = camX * W;
            cameraTranslate.Y = camY * H;
        }

        // Folia 的 m(kind, p)：前 18% 用指数缓出，后段线性再收
        static double CameraProgress(string kind, double p)
        {
            if (kind == "tracking-ribbon" || kind == "fragment-collage" || kind == "quiet-tableau" || kind == "poster-blocks")
            {
                return p * 0.55 + MusicStageUtil.EaseOutCubic(p) * 0.45;
            }
            if (p < 0.18) return MusicStageUtil.EaseOutExpo(p / 0.18) * 0.22;
            if (p < 0.78) return 0.22 + (p - 0.18) / 0.6 * 0.56;
            double o = (p - 0.78) / 0.22;
            return 0.78 + (1 - (1 - o) * (1 - o)) * 0.22;
        }

        // ================================================================ 词的动画

        void UpdateWords(ShotVisual visual, double position)
        {
            for (int i = 0; i < visual.Words.Count; i++)
            {
                WordVisual v = visual.Words[i];
                MusicStageWord w = v.Word;
                double span = w.Settle - w.Start;
                if (span <= 0.02) span = 0.02;
                double p = MusicStageUtil.Clamp((position - w.Start) / span, 0, 1);
                bool started = position >= w.Start - 0.01;
                double e = MusicStageUtil.EaseOutExpo(p);

                double focusTarget;
                if (w.IsDecoration) focusTarget = 0.55;
                else if (w.LineIndex < 0) focusTarget = 1.0;
                else if (position < w.Start) focusTarget = 0.30;
                else if (position >= w.LineStart && position < w.LineEnd) focusTarget = 1.0;
                else if (position >= w.LineEnd) focusTarget = 0.56;
                else focusTarget = 0.10;
                v.Focus = MusicStageUtil.Lerp(v.Focus, focusTarget, 0.14);

                // 整段先以 20% 的淡影铺满画面，唱到哪个词再点亮：
                // 这样换段的第一秒不会只剩一个空框，同时保留逐词进入的节奏。
                bool future = position < w.Start;
                double baseAlpha = future ? 0.20 : (0.20 + 0.80 * e);
                double alpha = baseAlpha * (future ? 0.9 : v.Focus);
                if (w.IsDecoration) alpha = baseAlpha * 0.18;
                if (!started && w.IsDecoration) alpha = 0.04;

                double scale = w.IsHero && visual.Descriptor.Kind == "type-impact"
                    ? 0.52 + 0.48 * e
                    : 0.86 + 0.14 * e;
                if (!w.IsDecoration) scale *= 0.965 + v.Focus * 0.035;
                if (future) scale *= 0.965;

                v.Move.X = w.EnterX * (1 - e);
                v.Move.Y = w.EnterY * (1 - e);
                v.Scale.ScaleX = scale;
                v.Scale.ScaleY = scale;
                v.Rotate.Angle = w.Rotation + w.EntryRotation * (1 - e);
                v.Root.Opacity = MusicStageUtil.Clamp(alpha, 0, 1);
                if (alpha < 0.006) v.Root.Visibility = Visibility.Collapsed;
                else v.Root.Visibility = Visibility.Visible;

                if (v.Halo != null)
                {
                    v.Halo.Opacity = 0.52 * (1 - e * 0.4);
                }
                if (v.Cyan != null) v.Cyan.Opacity = 0.40;
                if (v.Red != null) v.Red.Opacity = 0.40;
                if (v.Highlight != null) v.Highlight.Opacity = w.IsHighlight ? 0.62 : 0;
                if (v.Outline != null) v.Outline.Opacity = 1;
            }
        }

        void UpdateFireworks(ShotVisual visual, double time)
        {
            if (visual == null || visual.Fireworks == null || settings == null) return;
            if (settings.ReduceMotion || !settings.BeatBloom) return;
            if (stageStyle == null || stageStyle.Id != "hanabi") return;

            if (visual.NextFirework < 0) visual.NextFirework = time + 0.65;
            if (time >= visual.NextFirework)
            {
                SpawnFirework(visual, time);
                visual.NextFirework = time + 1.35 + (visual.BurstCount % 3) * 0.32;
            }

            for (int i = visual.Bursts.Count - 1; i >= 0; i--)
            {
                FireworkBurst burst = visual.Bursts[i];
                double progress = (time - burst.Born) / Math.Max(0.001, burst.Life);
                if (progress >= 1.0)
                {
                    for (int j = 0; j < burst.Particles.Count; j++)
                    {
                        try { visual.Fireworks.Children.Remove(burst.Particles[j].Node); } catch (Exception) { }
                    }
                    if (burst.Trail != null) { try { visual.Fireworks.Children.Remove(burst.Trail); } catch (Exception) { } }
                    visual.Bursts.RemoveAt(i);
                    continue;
                }

                double fade = 1 - progress;
                double gravity = progress * progress * burst.Size * 0.45;
                for (int j = 0; j < burst.Particles.Count; j++)
                {
                    FireworkParticle p = burst.Particles[j];
                    double distance = p.Speed * progress * burst.Size;
                    double x = burst.X + Math.Cos(p.Angle) * distance;
                    double y = burst.Y + Math.Sin(p.Angle) * distance + gravity;
                    Canvas.SetLeft(p.Node, x - p.Node.Width / 2);
                    Canvas.SetTop(p.Node, y - p.Node.Height / 2);
                    p.Node.Opacity = MusicStageUtil.Clamp(fade * fade * settings.SparkGlow, 0, 1);
                }
                if (burst.Trail != null)
                {
                    burst.Trail.Opacity = Math.Max(0, 0.55 * (1 - progress * 1.2)) * settings.SparkGlow;
                }
            }
        }

        void SpawnFirework(ShotVisual visual, double time)
        {
            visual.BurstCount++;
            var rng = new MusicStageRng((uint)visual.Descriptor.Seed ^ ((uint)visual.BurstCount * 2654435761u));
            bool chorus = visual.BurstCount % 4 == 0;
            double x = W * (0.16 + rng.Next() * 0.68);
            double y = H * (0.12 + rng.Next() * 0.42);
            double size = H * (chorus ? 0.26 : 0.17) * (0.8 + rng.Next() * 0.4);
            Color color = visual.BurstCount % 2 == 0 ? stageStyle.Accent : stageStyle.Secondary;
            var burst = new FireworkBurst
            {
                Born = time,
                Life = chorus ? 1.55 : 1.15,
                X = x,
                Y = y,
                Size = size
            };

            if (settings.Trail)
            {
                var trail = new Line
                {
                    X1 = x,
                    Y1 = H * 0.92,
                    X2 = x,
                    Y2 = y,
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(color, 100)),
                    StrokeThickness = 1.2,
                    IsHitTestVisible = false
                };
                visual.Fireworks.Children.Add(trail);
                burst.Trail = trail;
            }

            int count = (int)Math.Round(26 * Math.Max(0.1, settings.SparkCount));
            if (chorus) count = (int)(count * 1.5);
            count = Math.Max(8, Math.Min(64, count));
            for (int i = 0; i < count; i++)
            {
                double dotSize = 2.0 + rng.Next() * 2.6;
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(
                        i % 3 == 0 ? Colors.White : color, (byte)(150 + rng.Next() * 90))),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, y);
                visual.Fireworks.Children.Add(dot);
                burst.Particles.Add(new FireworkParticle
                {
                    Node = dot,
                    Angle = i / (double)count * Math.PI * 2 + rng.Range(-0.03, 0.03),
                    Speed = 0.5 + rng.Next() * 0.75,
                    Radius = dotSize
                });
            }
            visual.Bursts.Add(burst);
        }

        void UpdateParticles(ShotVisual visual, double time)
        {
            if (settings != null && settings.ReduceMotion) return;
            for (int i = 0; i < visual.Particles.Count; i++)
            {
                Particle p = visual.Particles[i];
                double x = p.X + Math.Sin(time * p.Speed + p.Phase) * p.Amplitude;
                double y = p.Y + Math.Cos(time * p.Speed * 0.83 + p.Phase * 1.7) * p.Amplitude * 0.8;
                Canvas.SetLeft(p.Node, x);
                Canvas.SetTop(p.Node, y);
                p.Node.Opacity = p.Alpha * (0.72 + 0.28 * Math.Sin(time * 0.7 + p.Phase));
            }
            if (visual.DecorDrift != null)
            {
                visual.DecorDrift.X = Math.Sin(time * 0.045) * 9;
                visual.DecorDrift.Y = Math.Cos(time * 0.037) * 6;
            }
        }

        // ================================================================ 构建一张 shot 的视觉

        ShotVisual BuildShotVisual(MusicStageShot shot)
        {
            var visual = new ShotVisual();
            visual.Descriptor = shot;
            double w = W;
            double h = H;
            var rootCanvas = new Canvas
            {
                Width = w,
                Height = h,
                ClipToBounds = true,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            visual.EntryScale = new ScaleTransform(1, 1, w / 2, h / 2);
            rootCanvas.RenderTransform = visual.EntryScale;
            visual.Root = rootCanvas;

            var bg = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            rootCanvas.Children.Add(bg);
            BuildBackdrop(bg, shot, w, h);

            var ghost = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.Ghost = ghost;
            rootCanvas.Children.Add(ghost);
            AddGhostLetters(ghost, shot, w, h);

            var decor = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.Decor = decor;
            visual.DecorDrift = new TranslateTransform(0, 0);
            decor.RenderTransform = visual.DecorDrift;
            rootCanvas.Children.Add(decor);
            AddDecor(decor, shot, w, h);

            var particles = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.ParticlesLayer = particles;
            rootCanvas.Children.Add(particles);
            AddParticles(visual, shot, w, h);

            var fireworks = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.Fireworks = fireworks;
            rootCanvas.Children.Add(fireworks);

            var guides = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.Guides = guides;
            rootCanvas.Children.Add(guides);
            AddScreenGuides(guides, w, h);

            var words = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            visual.WordsLayer = words;
            rootCanvas.Children.Add(words);

            for (int i = 0; i < shot.Words.Count; i++)
            {
                WordVisual wordVisual = CreateWordVisual(shot, shot.Words[i], w, h);
                visual.Words.Add(wordVisual);
                words.Children.Add(wordVisual.Root);
            }
            AddWordGuides(guides, visual, w, h);

            visual.Blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
            rootCanvas.Effect = visual.Blur;
            if (stageStyle != null && stageStyle.Id == "hanabi") visual.NextFirework = 0;
            return visual;
        }

        void BuildBackdrop(Canvas layer, MusicStageShot shot, double w, double h)
        {
            if (stageStyle == null || stageStyle.BackgroundKind == "sonnet")
            {
                BuildSonnetBackdrop(layer, shot, w, h);
                return;
            }

            // 其他 17 个样式：先铺自己的底色，再按样式画一层可辨认的场景。
            var bg = new Rectangle
            {
                Width = w,
                Height = h,
                Fill = new LinearGradientBrush(stageStyle.BgTop, stageStyle.BgBottom, 90)
            };
            layer.Children.Add(bg);

            AddStyleGlow(layer, w * 0.5, h * 0.42, w * 0.72, h * 0.62,
                MusicStageUtil.Alpha(stageStyle.Accent, 40));

            switch (stageStyle.BackgroundKind)
            {
                case "tempera": BuildTempera(layer, shot, w, h); break;
                case "lumiere": BuildLumiere(layer, shot, w, h); break;
                case "hanabi": BuildHanabi(layer, shot, w, h); break;
                case "machi": BuildMachi(layer, shot, w, h); break;
                case "nebbia": BuildNebbia(layer, shot, w, h); break;
                case "classic": BuildClassic(layer, shot, w, h); break;
                case "cadenze": BuildCadenze(layer, shot, w, h); break;
                case "partita": BuildPartita(layer, shot, w, h); break;
                case "fume": BuildFume(layer, shot, w, h); break;
                case "tilt": BuildTilt(layer, shot, w, h); break;
                case "ponder": BuildPonder(layer, shot, w, h); break;
                case "still": BuildStill(layer, shot, w, h); break;
                case "pendolo": BuildPendolo(layer, shot, w, h); break;
                case "cappella": BuildCappella(layer, shot, w, h); break;
                case "claddagh": BuildCladdagh(layer, shot, w, h); break;
                case "monet": BuildMonet(layer, shot, w, h); break;
                case "diorama": BuildDiorama(layer, shot, w, h); break;
            }
        }

        // ---------------------------------------------------------------- 样式场景

        void AddStyleGlow(Canvas layer, double cx, double cy, double width, double height, Color color)
        {
            var glow = new Ellipse
            {
                Width = width,
                Height = height,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    GradientStops =
                    {
                        new GradientStop(MusicStageUtil.Alpha(color, 70), 0),
                        new GradientStop(MusicStageUtil.Alpha(color, 22), 0.48),
                        new GradientStop(Colors.Transparent, 1)
                    }
                },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(glow, cx - width / 2);
            Canvas.SetTop(glow, cy - height / 2);
            layer.Children.Add(glow);
        }

        void AddStyleBlock(Canvas layer, double x, double y, double width, double height, Color color, double angle, double radius)
        {
            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                RadiusX = radius,
                RadiusY = radius,
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angle, width / 2, height / 2)
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            layer.Children.Add(rect);
        }

        void AddStyleText(Canvas layer, string text, double x, double y, double size, Color color, bool bold, double angle)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = MusicStageText.FamilyFor(text),
                FontSize = Math.Max(8, size),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(color),
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angle, 0, 0)
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            layer.Children.Add(tb);
        }

        void AddStyleStars(Canvas layer, int count, Color color, MusicStageRng rng, double w, double h, double alpha)
        {
            for (int i = 0; i < count; i++)
            {
                double size = rng.Range(1.0, 2.8);
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(color, (byte)(alpha * rng.Range(0.4, 1.0)))),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, rng.Range(0, w));
                Canvas.SetTop(dot, rng.Range(0, h));
                layer.Children.Add(dot);
            }
        }

        void BuildTempera(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x71A2u);
            Color[] colors = new Color[] { stageStyle.Accent, stageStyle.Secondary, Color.FromRgb(244, 210, 122), Color.FromRgb(248, 240, 218) };
            for (int i = 0; i < 7; i++)
            {
                double bw = w * rng.Range(0.12, 0.3);
                double bh = h * rng.Range(0.08, 0.22);
                AddStyleBlock(layer, rng.Range(-0.05, 0.8) * w, rng.Range(0.05, 0.78) * h,
                    bw, bh, MusicStageUtil.Alpha(colors[i % colors.Length], (byte)rng.Range(22, 58)),
                    rng.Range(-14, 14), 3);
            }
            // 胶带 / 贴纸
            for (int i = 0; i < 5; i++)
            {
                double x = rng.Range(0.1, 0.86) * w;
                double y = rng.Range(0.1, 0.84) * h;
                AddStyleBlock(layer, x, y, rng.Range(40, 110), rng.Range(10, 18),
                    MusicStageUtil.Alpha(Color.FromRgb(250, 240, 210), 60), rng.Range(-32, 32), 1);
            }
            for (int i = 0; i < 22; i++)
            {
                var dot = new Ellipse
                {
                    Width = rng.Range(2, 6),
                    Height = rng.Range(2, 6),
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Ink, 70)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, rng.Range(0, w));
                Canvas.SetTop(dot, rng.Range(0, h));
                layer.Children.Add(dot);
            }
        }

        void BuildLumiere(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x3B71u);
            for (int i = 0; i < 6; i++)
            {
                double baseX = w * (0.05 + i * 0.18);
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(baseX, h + 20) };
                fig.Segments.Add(new LineSegment(new Point(baseX - w * 0.08, -20), true));
                fig.Segments.Add(new LineSegment(new Point(baseX + w * 0.14, -20), true));
                fig.Segments.Add(new LineSegment(new Point(baseX + w * 0.03, h + 20), true));
                geo.Figures.Add(fig);
                var beam = new Path
                {
                    Data = geo,
                    Fill = new LinearGradientBrush(
                        MusicStageUtil.Alpha(stageStyle.Accent, 0),
                        MusicStageUtil.Alpha(stageStyle.Accent, (byte)(20 + i * 5)),
                        90),
                    IsHitTestVisible = false
                };
                layer.Children.Add(beam);
            }
            AddStyleGlow(layer, w * 0.5, h * 0.9, w * 0.6, h * 0.28,
                MusicStageUtil.Alpha(stageStyle.Accent, 80));
            for (int i = 0; i < 28; i++)
            {
                double x = rng.Range(0.05, 0.95) * w;
                double y = rng.Range(0.1, 0.9) * h;
                var dust = new Ellipse
                {
                    Width = rng.Range(1.2, 3.2),
                    Height = rng.Range(1.2, 3.2),
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Accent, 90))
                };
                Canvas.SetLeft(dust, x);
                Canvas.SetTop(dust, y);
                layer.Children.Add(dust);
            }
        }

        void BuildHanabi(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x4E1Bu);
            AddStyleStars(layer, 70, Color.FromRgb(255, 246, 220), rng, w, h, 130);
            for (int i = 0; i < 3; i++)
            {
                double cx = w * rng.Range(0.15, 0.85);
                double cy = h * rng.Range(0.12, 0.6);
                double radius = w * rng.Range(0.045, 0.11);
                for (int ring = 0; ring < 3; ring++)
                {
                    var burst = new Ellipse
                    {
                        Width = radius * 2,
                        Height = radius * 2 * 0.6,
                        Stroke = new SolidColorBrush(MusicStageUtil.Alpha(
                            i % 2 == 0 ? stageStyle.Accent : stageStyle.Secondary, (byte)(60 - ring * 16))),
                        StrokeThickness = 1.2,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(burst, cx - radius);
                    Canvas.SetTop(burst, cy - radius * 0.6);
                    layer.Children.Add(burst);
                }
            }
            for (int i = 0; i < 26; i++)
            {
                AddStyleBlock(layer, rng.Range(0, w), rng.Range(0.15, 0.9) * h,
                    rng.Range(4, 16), rng.Range(2, 5),
                    MusicStageUtil.Alpha(i % 2 == 0 ? stageStyle.Accent : stageStyle.Secondary, (byte)rng.Range(60, 140)),
                    rng.Range(-80, 80), 0);
            }
        }

        void BuildMachi(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x2F8Au);
            // 楼群剪影
            for (int i = 0; i < 12; i++)
            {
                double bw = w * rng.Range(0.05, 0.11);
                double bh = h * rng.Range(0.18, 0.45);
                AddStyleBlock(layer, i * w * 0.085 - 20, h - bh, bw, bh,
                    Color.FromArgb(190, 6, 10, 20), 0, 0);
            }
            // 霓虹招牌
            for (int i = 0; i < 10; i++)
            {
                double x = rng.Range(0.05, 0.92) * w;
                double y = rng.Range(0.12, 0.72) * h;
                double height = rng.Range(50, 170);
                AddStyleBlock(layer, x, y, rng.Range(8, 15), height,
                    MusicStageUtil.Alpha(i % 2 == 0 ? stageStyle.Accent : stageStyle.Secondary, (byte)rng.Range(50, 120)),
                    0, 2);
            }
            for (int i = 0; i < 80; i++)
            {
                double x = rng.Range(0, w);
                double y = rng.Range(0, h);
                AddLine(layer, x, y, x - 4, y + 18,
                    MusicStageUtil.Alpha(stageStyle.Secondary, 34), 1, null);
            }
        }

        void BuildNebbia(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x5B2Du);
            for (int i = 0; i < 5; i++)
            {
                var fog = new Ellipse
                {
                    Width = w * rng.Range(0.6, 1.2),
                    Height = h * rng.Range(0.12, 0.28),
                    Fill = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.5),
                        GradientStops =
                        {
                            new GradientStop(MusicStageUtil.Alpha(Color.FromRgb(220, 232, 240), 26), 0),
                            new GradientStop(Colors.Transparent, 1)
                        }
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(fog, rng.Range(-0.2, 0.6) * w);
                Canvas.SetTop(fog, rng.Range(0.1, 0.82) * h);
                layer.Children.Add(fog);
            }
            for (int i = 0; i < 90; i++)
            {
                double x = rng.Range(0, w);
                double y = rng.Range(0, h);
                AddLine(layer, x, y, x - rng.Range(1, 4), y + rng.Range(10, 34),
                    MusicStageUtil.Alpha(Color.FromRgb(230, 242, 250), (byte)rng.Range(25, 70)), 1, null);
            }
            // 车窗框
            AddLine(layer, w * 0.05, h * 0.04, w * 0.95, h * 0.04, MusicStageUtil.Alpha(Ink, 60), 1, null);
            AddLine(layer, w * 0.05, h * 0.96, w * 0.95, h * 0.96, MusicStageUtil.Alpha(Ink, 60), 1, null);
        }

        void BuildClassic(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x6C11u);
            for (int i = 0; i < 4; i++)
            {
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(-w * 0.1, h * (0.25 + i * 0.14)) };
                fig.Segments.Add(new BezierSegment(
                    new Point(w * 0.28, h * (0.05 + i * 0.12)),
                    new Point(w * 0.68, h * (0.82 - i * 0.1)),
                    new Point(w * 1.1, h * (0.35 + i * 0.09)), true));
                geo.Figures.Add(fig);
                var path = new Path
                {
                    Data = geo,
                    Stroke = new LinearGradientBrush(
                        MusicStageUtil.Alpha(stageStyle.Accent, 0),
                        MusicStageUtil.Alpha(stageStyle.Secondary, (byte)(60 + i * 20)),
                        0),
                    StrokeThickness = 1.5 + i * 0.8,
                    IsHitTestVisible = false
                };
                layer.Children.Add(path);
            }
            for (int i = 0; i < 24; i++)
            {
                AddLine(layer, rng.Range(0, w), rng.Range(0, h),
                    rng.Range(0, w), rng.Range(0, h),
                    MusicStageUtil.Alpha(stageStyle.Accent, (byte)rng.Range(12, 38)), 1, null);
            }
        }

        void BuildCadenze(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x1A7Bu);
            for (int i = 0; i < 4; i++)
            {
                var blob = new Ellipse
                {
                    Width = w * rng.Range(0.18, 0.42),
                    Height = h * rng.Range(0.12, 0.34),
                    Fill = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.5),
                        GradientStops =
                        {
                            new GradientStop(MusicStageUtil.Alpha(stageStyle.Accent, (byte)rng.Range(24, 54)), 0),
                            new GradientStop(Colors.Transparent, 1)
                        }
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(blob, rng.Range(0, w));
                Canvas.SetTop(blob, rng.Range(0, h));
                layer.Children.Add(blob);
            }
            for (int i = 0; i < 3; i++)
            {
                double x = rng.Range(0.15, 0.8) * w;
                double y = rng.Range(0.1, 0.7) * h;
                AddLine(layer, x, y, x + rng.Range(80, 260), y + rng.Range(-40, 40),
                    MusicStageUtil.Alpha(stageStyle.Ink, 40), rng.Range(2, 7), null);
            }
            AddStyleText(layer, "印", w * 0.86, h * 0.78, h * 0.08,
                MusicStageUtil.Alpha(stageStyle.Accent, 110), true, 0);
        }

        void BuildPartita(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x2D44u);
            for (int i = 0; i < 5; i++)
            {
                var cloud = new Ellipse
                {
                    Width = w * rng.Range(0.35, 0.8),
                    Height = h * rng.Range(0.08, 0.18),
                    Fill = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.5),
                        GradientStops =
                        {
                            new GradientStop(MusicStageUtil.Alpha(Color.FromRgb(255, 255, 255),
                                (byte)rng.Range(50, 100)), 0),
                            new GradientStop(Colors.Transparent, 1)
                        }
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(cloud, rng.Range(-0.1, 0.7) * w);
                Canvas.SetTop(cloud, rng.Range(0.05, 0.7) * h);
                layer.Children.Add(cloud);
            }
            for (int i = 0; i < 7; i++)
            {
                double y = h * (0.58 + i * 0.055);
                AddLine(layer, w * 0.08, y, w * 0.92, y,
                    MusicStageUtil.Alpha(stageStyle.Accent, (byte)(18 + i * 4)), 1, null);
            }
        }

        void BuildFume(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x4B77u);
            string[] names = new string[] { "FUME", "浮名", "NAME", "浮名", "FLOATING" };
            for (int i = 0; i < 16; i++)
            {
                AddStyleText(layer, names[i % names.Length], rng.Range(0.05, 0.82) * w,
                    rng.Range(0.08, 0.88) * h, rng.Range(11, 36),
                    MusicStageUtil.Alpha(Ink, (byte)rng.Range(20, 70)), false, rng.Range(-8, 8));
            }
            for (int i = 0; i < 8; i++)
            {
                double y = h * (0.1 + i * 0.11);
                AddLine(layer, w * 0.05, y, w * 0.95, y, MusicStageUtil.Alpha(Ink, 20), 1, null);
            }
        }

        void BuildTilt(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x7E12u);
            AddStyleBlock(layer, w * 0.16, h * 0.14, w * 0.7, h * 0.7,
                MusicStageUtil.Alpha(Color.FromRgb(244, 231, 211), 18), -5, 8);
            AddStyleBlock(layer, w * 0.2, h * 0.12, w * 0.55, h * 0.06,
                MusicStageUtil.Alpha(Color.FromRgb(250, 240, 210), 40), 8, 2);
            for (int i = 0; i < 5; i++)
            {
                double y = h * (0.3 + i * 0.13);
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(w * 0.25, y) };
                fig.Segments.Add(new BezierSegment(
                    new Point(w * 0.4, y - 10), new Point(w * 0.68, y + 12), new Point(w * 0.8, y), true));
                geo.Figures.Add(fig);
                var path = new Path
                {
                    Data = geo,
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Ink, (byte)rng.Range(30, 70))),
                    StrokeThickness = 1.2,
                    IsHitTestVisible = false
                };
                layer.Children.Add(path);
            }
        }

        void BuildPonder(Canvas layer, MusicStageShot shot, double w, double h)
        {
            for (int i = 1; i <= 5; i++)
            {
                var ring = new Ellipse
                {
                    Width = w * 0.18 * i,
                    Height = h * 0.12 * i,
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Accent, (byte)(32 - i * 5))),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new double[] { 2, 7 }),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, w * 0.5 - ring.Width / 2);
                Canvas.SetTop(ring, h * 0.48 - ring.Height / 2);
                layer.Children.Add(ring);
            }
            for (int i = 0; i < 30; i++)
            {
                var dot = new Ellipse
                {
                    Width = 2,
                    Height = 2,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Ink, 80))
                };
                Canvas.SetLeft(dot, w * 0.1 + i * w * 0.027);
                Canvas.SetTop(dot, h * 0.85);
                layer.Children.Add(dot);
            }
        }

        void BuildStill(Canvas layer, MusicStageShot shot, double w, double h)
        {
            AddLine(layer, w * 0.08, h * 0.18, w * 0.92, h * 0.18, MusicStageUtil.Alpha(Ink, 40), 1, null);
            AddLine(layer, w * 0.08, h * 0.82, w * 0.92, h * 0.82, MusicStageUtil.Alpha(Ink, 30), 1, null);
            AddStyleBlock(layer, w * 0.1, h * 0.12, w * 0.02, h * 0.02,
                MusicStageUtil.Alpha(stageStyle.Accent, 150), 0, 0);
            AddStyleText(layer, "清", w * 0.82, h * 0.84, h * 0.08,
                MusicStageUtil.Alpha(stageStyle.Accent, 80), false, 0);
        }

        void BuildPendolo(Canvas layer, MusicStageShot shot, double w, double h)
        {
            double cx = w * 0.5;
            double cy = h * 0.48;
            for (int i = 1; i <= 6; i++)
            {
                var ring = new Ellipse
                {
                    Width = Math.Min(w, h) * 0.14 * i,
                    Height = Math.Min(w, h) * 0.14 * i,
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Accent, (byte)(70 - i * 7))),
                    StrokeThickness = i == 1 ? 2 : 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, cx - ring.Width / 2);
                Canvas.SetTop(ring, cy - ring.Height / 2);
                layer.Children.Add(ring);
            }
            for (int i = 0; i < 60; i++)
            {
                double a = i / 60.0 * Math.PI * 2;
                double r1 = Math.Min(w, h) * 0.39;
                double r2 = r1 + (i % 5 == 0 ? 16 : 7);
                AddLine(layer, cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1,
                    cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2,
                    MusicStageUtil.Alpha(stageStyle.Accent, i % 5 == 0 ? (byte)150 : (byte)60), 1, null);
            }
            AddStyleText(layer, "XII", cx - 18, cy - Math.Min(w, h) * 0.46, 16,
                MusicStageUtil.Alpha(stageStyle.Ink, 120), false, 0);
            AddStyleText(layer, "VI", cx - 10, cy + Math.Min(w, h) * 0.40, 16,
                MusicStageUtil.Alpha(stageStyle.Ink, 120), false, 0);
        }

        void BuildCappella(Canvas layer, MusicStageShot shot, double w, double h)
        {
            for (int i = 0; i < 5; i++)
            {
                var beam = new Ellipse
                {
                    Width = w * (0.5 + i * 0.2),
                    Height = h * (0.2 + i * 0.08),
                    Fill = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.8),
                        GradientStops =
                        {
                            new GradientStop(MusicStageUtil.Alpha(
                                i % 2 == 0 ? stageStyle.Accent : stageStyle.Secondary, 24), 0),
                            new GradientStop(Colors.Transparent, 1)
                        }
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(beam, w * 0.5 - beam.Width / 2);
                Canvas.SetTop(beam, h * 0.88 - beam.Height / 2);
                layer.Children.Add(beam);
            }
            for (int i = 0; i < 4; i++)
            {
                AddStyleText(layer, "唱", w * (0.12 + i * 0.2), h * 0.84, h * 0.04,
                    MusicStageUtil.Alpha(stageStyle.Ink, 60), false, 0);
            }
        }

        void BuildCladdagh(Canvas layer, MusicStageShot shot, double w, double h)
        {
            double cx = w * 0.5;
            double cy = h * 0.46;
            for (int i = 0; i < 3; i++)
            {
                var ring = new Ellipse
                {
                    Width = w * (0.28 + i * 0.12),
                    Height = h * (0.34 + i * 0.12),
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Accent, (byte)(80 - i * 18))),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    RenderTransform = new RotateTransform(i * 30, 0, 0)
                };
                Canvas.SetLeft(ring, cx - ring.Width / 2);
                Canvas.SetTop(ring, cy - ring.Height / 2);
                layer.Children.Add(ring);
            }
            for (int i = 0; i < 28; i++)
            {
                double a = i / 28.0 * Math.PI * 2;
                double r = Math.Min(w, h) * 0.38;
                var dot = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(stageStyle.Secondary, 90))
                };
                Canvas.SetLeft(dot, cx + Math.Cos(a) * r);
                Canvas.SetTop(dot, cy + Math.Sin(a) * r);
                layer.Children.Add(dot);
            }
        }

        void BuildMonet(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x9C31u);
            for (int i = 0; i < 9; i++)
            {
                var blob = new Ellipse
                {
                    Width = w * rng.Range(0.14, 0.38),
                    Height = h * rng.Range(0.1, 0.28),
                    Fill = new RadialGradientBrush
                    {
                        Center = new Point(0.5, 0.5),
                        GradientStops =
                        {
                            new GradientStop(MusicStageUtil.Alpha(
                                i % 3 == 0 ? stageStyle.Secondary : stageStyle.Accent, (byte)rng.Range(24, 60)), 0),
                            new GradientStop(Colors.Transparent, 1)
                        }
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(blob, rng.Range(-0.1, 0.8) * w);
                Canvas.SetTop(blob, rng.Range(0, 0.8) * h);
                layer.Children.Add(blob);
            }
            for (int i = 0; i < 12; i++)
            {
                double x = rng.Range(0.05, 0.95) * w;
                AddLine(layer, x, h * 0.86, x + rng.Range(-10, 20), h * 0.98,
                    MusicStageUtil.Alpha(stageStyle.Ink, 35), 1.2, null);
            }
        }

        void BuildDiorama(Canvas layer, MusicStageShot shot, double w, double h)
        {
            AddLine(layer, w * 0.12, h * 0.16, w * 0.12, h * 0.84, MusicStageUtil.Alpha(Ink, 50), 1, null);
            AddLine(layer, w * 0.88, h * 0.16, w * 0.88, h * 0.84, MusicStageUtil.Alpha(Ink, 50), 1, null);
            AddLine(layer, w * 0.12, h * 0.84, w * 0.88, h * 0.84, MusicStageUtil.Alpha(Ink, 40), 1, null);
            AddLine(layer, w * 0.12, h * 0.16, w * 0.88, h * 0.16, MusicStageUtil.Alpha(Ink, 40), 1, null);
            for (int i = 0; i < 5; i++)
            {
                double y = h * (0.18 + i * 0.14);
                AddLine(layer, w * 0.12, y, w * 0.88, y,
                    MusicStageUtil.Alpha(stageStyle.Accent, (byte)(20 + i * 4)), 1, null);
            }
            // 镜面反光
            var mirror = new Rectangle
            {
                Width = w * 0.68,
                Height = h * 0.16,
                Fill = new LinearGradientBrush(
                    MusicStageUtil.Alpha(stageStyle.Accent, 0),
                    MusicStageUtil.Alpha(stageStyle.Accent, 36),
                    90),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(mirror, w * 0.16);
            Canvas.SetTop(mirror, h * 0.82);
            layer.Children.Add(mirror);
        }


        void BuildSonnetBackdrop(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var bg = new Rectangle
            {
                Width = w,
                Height = h,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.44),
                    GradientOrigin = new Point(0.5, 0.44),
                    RadiusX = 0.78,
                    RadiusY = 0.78,
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(38, 40, 48), 0),
                        new GradientStop(BgTop, 0.46),
                        new GradientStop(Color.FromRgb(9, 9, 13), 0.82),
                        new GradientStop(BgBottom, 1)
                    }
                }
            };
            layer.Children.Add(bg);

            var glow = new Ellipse
            {
                Width = w * 0.62,
                Height = h * 0.58,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    GradientStops =
                    {
                        new GradientStop(MusicStageUtil.Alpha(MusicStageUtil.Lift(accent, 0.25), 44), 0),
                        new GradientStop(MusicStageUtil.Alpha(accent, 16), 0.45),
                        new GradientStop(Colors.Transparent, 1)
                    }
                },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(glow, w * 0.5 - glow.Width / 2);
            Canvas.SetTop(glow, h * 0.46 - glow.Height / 2);
            layer.Children.Add(glow);

            // 很淡的"巨大字母"底纹，像参考图左侧那个 V / S / L
            var rng = new MusicStageRng((uint)shot.Seed ^ 0xA53C9E1Du);
            string alphabet = "VSLTAMNR";
            for (int i = 0; i < 3; i++)
            {
                double size = h * (0.42 + rng.Next() * 0.34);
                char ch = alphabet[rng.Pick(alphabet.Length)];
                double x = w * rng.Range(-0.02, 0.92);
                double y = h * rng.Range(-0.12, 0.7);
                AddGhostLetter(layer, ch.ToString(), x, y, size, rng.Range(-18, 18),
                    MusicStageUtil.Alpha(Ink, (byte)(5 + rng.Next() * 6)), false);
            }

            // 细水平扫描线：Folia 背景 MG 里的 editorial 标记
            var scan = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
            for (int i = 0; i < 5; i++)
            {
                double y = h * (0.12 + i * 0.19) + rng.Range(-8, 8);
                AddLine(scan, w * 0.04, y, w * 0.96, y,
                    MusicStageUtil.Alpha(i % 2 == 0 ? Dim : accent, (byte)(12 + i * 3)), 1, null);
            }
            layer.Children.Add(scan);

            AddBotanical(layer, w * 0.12, h * 0.18, Math.Min(w, h) * 0.00105, -22, w, h, rng, true);
            AddBotanical(layer, w * 0.88, h * 0.16, Math.Min(w, h) * 0.00078, 28, w, h, rng, false);
            AddOrbit(layer, w * 0.76, h * 0.4, Math.Min(w, h) * 0.27, -14, rng);
            AddArchitecture(layer, w, h, rng);
        }

        void AddDecor(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x51ED2B77u);
            // 左下角的花枝，和参考图一致：花/叶都是细线，透明度很低
            AddBotanical(layer, w * 0.08, h * 0.82, Math.Min(w, h) * 0.00062, 38, w, h, rng, true);
            AddBotanical(layer, w * 0.91, h * 0.76, Math.Min(w, h) * 0.0005, -52, w, h, rng, false);
            AddOrbit(layer, w * 0.2, h * 0.42, Math.Min(w, h) * 0.19, 22, rng);
            AddPlusMarks(layer, w, h, rng);
        }

        // ---------------------------------------------------------------- 背景装饰

        static void AddLine(Canvas layer, double x1, double y1, double x2, double y2, Color color, double thickness, double[] dash)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                IsHitTestVisible = false
            };
            if (dash != null) line.StrokeDashArray = new DoubleCollection(dash);
            layer.Children.Add(line);
        }

        static void AddEllipse(Canvas layer, double x, double y, double width, double height, Color color, double thickness, bool fill, double[] dash)
        {
            var e = new Ellipse
            {
                Width = width,
                Height = height,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                Fill = fill ? new SolidColorBrush(color) : null,
                IsHitTestVisible = false
            };
            if (dash != null) e.StrokeDashArray = new DoubleCollection(dash);
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            layer.Children.Add(e);
        }

        static void AddLineLocal(Canvas layer, double x1, double y1, double x2, double y2, Color color, double thickness, double[] dash)
        {
            AddLine(layer, x1, y1, x2, y2, color, thickness, dash);
        }

        static void AddBotanical(Canvas layer, double x, double y, double scale, double angle, double w, double h, MusicStageRng rng, bool flower)
        {
            var group = new Canvas { Width = 2, Height = 2, IsHitTestVisible = false };
            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(angle, 0, 0));
            transform.Children.Add(new TranslateTransform(x, y));
            group.RenderTransform = transform;
            Canvas.SetLeft(group, 0);
            Canvas.SetTop(group, 0);

            double stem = Math.Min(w, h) * (flower ? 0.22 : 0.16) * (0.8 + rng.Next() * 0.5);
            var stemGeo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(0, 0) };
            fig.Segments.Add(new BezierSegment(new Point(-stem * 0.16, -stem * 0.3), new Point(stem * 0.2, -stem * 0.66), new Point(0, -stem), true));
            stemGeo.Figures.Add(fig);
            var stemPath = new Path
            {
                Data = stemGeo,
                Stroke = new SolidColorBrush(MusicStageUtil.Alpha(Ink, 42)),
                StrokeThickness = 1.1,
                IsHitTestVisible = false
            };
            group.Children.Add(stemPath);

            int leaves = 3 + rng.Pick(3);
            for (int i = 0; i < leaves; i++)
            {
                double t = (i + 1.0) / (leaves + 1);
                double ly = -stem * t;
                double side = i % 2 == 0 ? -1 : 1;
                AddLeaf(group, side * 4, ly, stem * (0.18 + rng.Next() * 0.1), stem * 0.055,
                    side * (38 + rng.Next() * 22), MusicStageUtil.Alpha(Ink, (byte)(30 + rng.Next() * 18)));
            }

            if (flower)
            {
                AddFlower(group, 0, -stem, Math.Max(7, stem * 0.08), MusicStageUtil.Alpha(Ink, 52), rng);
                // 几颗散落的小点
                for (int i = 0; i < 4; i++)
                {
                    var dot = new Ellipse
                    {
                        Width = 2.2,
                        Height = 2.2,
                        Fill = new SolidColorBrush(MusicStageUtil.Alpha(Ink, 38))
                    };
                    Canvas.SetLeft(dot, rng.Range(-stem * 0.3, stem * 0.3));
                    Canvas.SetTop(dot, rng.Range(-stem, -stem * 0.2));
                    group.Children.Add(dot);
                }
            }
            layer.Children.Add(group);
        }

        static void AddLeaf(Canvas parent, double x, double y, double length, double width, double angle, Color color)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            fig.Segments.Add(new QuadraticBezierSegment(new Point(length * 0.42, -width), new Point(length, 0), true));
            fig.Segments.Add(new QuadraticBezierSegment(new Point(length * 0.42, width), new Point(0, 0), true));
            geo.Figures.Add(fig);
            var leaf = new Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.0,
                Fill = new SolidColorBrush(MusicStageUtil.Alpha(color, (byte)(color.A * 0.22))),
                IsHitTestVisible = false
            };
            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(angle, 0, 0));
            transform.Children.Add(new TranslateTransform(x, y));
            leaf.RenderTransform = transform;
            parent.Children.Add(leaf);
        }

        static void AddFlower(Canvas parent, double cx, double cy, double size, Color color, MusicStageRng rng)
        {
            int petals = 6 + rng.Pick(2);
            for (int i = 0; i < petals; i++)
            {
                double angle = i * 360.0 / petals + rng.Range(-4, 4);
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
                fig.Segments.Add(new BezierSegment(
                    new Point(-size * 0.55, -size * 1.1),
                    new Point(size * 0.55, -size * 1.1),
                    new Point(0, -size * 2.0), true));
                fig.Segments.Add(new BezierSegment(
                    new Point(size * 0.18, -size * 1.4),
                    new Point(-size * 0.18, -size * 1.4),
                    new Point(0, 0), true));
                geo.Figures.Add(fig);
                var petal = new Path
                {
                    Data = geo,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.0,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(color, 12)),
                    IsHitTestVisible = false
                };
                var transform = new TransformGroup();
                transform.Children.Add(new RotateTransform(angle, 0, 0));
                transform.Children.Add(new TranslateTransform(cx, cy));
                petal.RenderTransform = transform;
                parent.Children.Add(petal);
            }
            var center = new Ellipse
            {
                Width = size * 0.55,
                Height = size * 0.55,
                Fill = new SolidColorBrush(MusicStageUtil.Alpha(color, 60)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(center, cx - center.Width / 2);
            Canvas.SetTop(center, cy - center.Height / 2);
            parent.Children.Add(center);
        }

        static void AddOrbit(Canvas layer, double cx, double cy, double radius, double angle, MusicStageRng rng)
        {
            var group = new Canvas { Width = 2, Height = 2, IsHitTestVisible = false };
            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(angle, 0, 0));
            transform.Children.Add(new TranslateTransform(cx, cy));
            group.RenderTransform = transform;

            for (int i = 0; i < 3; i++)
            {
                var e = new Ellipse
                {
                    Width = radius * (2 - i * 0.27),
                    Height = radius * (1.1 - i * 0.17),
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(Ink, (byte)(26 - i * 6))),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                if (i == 1) e.StrokeDashArray = new DoubleCollection(new double[] { 1.5, 5 });
                Canvas.SetLeft(e, -e.Width / 2);
                Canvas.SetTop(e, -e.Height / 2);
                group.Children.Add(e);
            }
            for (int i = 0; i < 18; i++)
            {
                double a = i / 18.0 * Math.PI * 2;
                double rx = radius * 0.95;
                double ry = radius * 0.52;
                var dot = new Ellipse
                {
                    Width = 2.1,
                    Height = 2.1,
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(Ink, 40))
                };
                Canvas.SetLeft(dot, Math.Cos(a) * rx - 1);
                Canvas.SetTop(dot, Math.Sin(a) * ry - 1);
                group.Children.Add(dot);
            }
            AddLine(group, -radius * 1.1, 0, radius * 1.1, 0, MusicStageUtil.Alpha(Ink, 22), 1, null);
            AddLine(group, 0, -radius * 0.7, 0, radius * 0.7, MusicStageUtil.Alpha(Ink, 18), 1, null);
            layer.Children.Add(group);
        }

        static void AddArchitecture(Canvas layer, double w, double h, MusicStageRng rng)
        {
            var color = MusicStageUtil.Alpha(Ink, 26);
            AddLine(layer, w * 0.065, h * 0.05, w * 0.065, h * 0.95, color, 1, new double[] { 4, 9 });
            AddLine(layer, w * 0.935, h * 0.05, w * 0.935, h * 0.95, color, 1, null);
            AddLine(layer, w * 0.06, h * 0.055, w * 0.94, h * 0.055, MusicStageUtil.Alpha(Ink, 20), 1, null);
            AddLine(layer, w * 0.06, h * 0.945, w * 0.94, h * 0.945, MusicStageUtil.Alpha(Ink, 20), 1, null);

            // 中央的大框，像参考图里围着竖排主词的矩形
            AddLine(layer, w * 0.49, h * 0.14, w * 0.49, h * 0.84, MusicStageUtil.Alpha(Ink, 30), 1, null);
            AddLine(layer, w * 0.4, h * 0.13, w * 0.62, h * 0.13, MusicStageUtil.Alpha(Ink, 26), 1, null);
            AddLine(layer, w * 0.4, h * 0.86, w * 0.62, h * 0.86, MusicStageUtil.Alpha(Ink, 26), 1, null);

            // 角上的测量标记
            double m = w * 0.065;
            double t = w * 0.02;
            AddLine(layer, m, h * 0.05, m + t, h * 0.05, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, m, h * 0.05, m, h * 0.05 + t, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, w - m, h * 0.05, w - m - t, h * 0.05, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, w - m, h * 0.05, w - m, h * 0.05 + t, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, m, h - h * 0.05, m + t, h - h * 0.05, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, m, h - h * 0.05, m, h - h * 0.05 - t, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, w - m, h - h * 0.05, w - m - t, h - h * 0.05, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
            AddLine(layer, w - m, h - h * 0.05, w - m, h - h * 0.05 - t, MusicStageUtil.Alpha(Ink, 60), 1.4, null);
        }

        static void AddPlusMarks(Canvas layer, double w, double h, MusicStageRng rng)
        {
            for (int i = 0; i < 5; i++)
            {
                double x = w * rng.Range(0.06, 0.94);
                double y = h * rng.Range(0.08, 0.92);
                double s = rng.Range(3, 7);
                AddLine(layer, x - s, y, x + s, y, MusicStageUtil.Alpha(Ink, 34), 1, null);
                AddLine(layer, x, y - s, x, y + s, MusicStageUtil.Alpha(Ink, 34), 1, null);
            }
        }

        void AddGhostLetters(Canvas layer, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0x2C9277B5u);
            string source = "SONNET";
            if (last != null && !string.IsNullOrEmpty(last.Title))
            {
                string latin = "";
                for (int i = 0; i < last.Title.Length && latin.Length < 6; i++)
                {
                    char c = last.Title[i];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin += c;
                }
                if (latin.Length >= 3) source = latin;
            }
            int added = 0;
            for (int i = 0; i < source.Length && added < 2; i++)
            {
                char c = source[i];
                if (!char.IsLetter(c)) continue;
                double size = h * (0.42 + rng.Next() * 0.18);
                AddGhostLetter(layer, c.ToString(), w * (0.58 + added * 0.24), h * (0.16 + rng.Next() * 0.28),
                    size, rng.Range(-10, 14), MusicStageUtil.Alpha(Ink, 6), false);
                added++;
            }
        }

        static void AddGhostLetter(Canvas layer, string text, double x, double y, double size, double angle, Color color, bool outline)
        {
            var geometry = MusicStageText.BuildGeometry(text, size, true);
            var path = new Path
            {
                Data = geometry,
                Fill = outline ? null : new SolidColorBrush(MusicStageUtil.Alpha(color, 24)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = Math.Max(0.8, size * 0.0012),
                IsHitTestVisible = false
            };
            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(angle, 0, 0));
            transform.Children.Add(new TranslateTransform(x, y));
            path.RenderTransform = transform;
            layer.Children.Add(path);
        }

        void AddParticles(ShotVisual visual, MusicStageShot shot, double w, double h)
        {
            var rng = new MusicStageRng((uint)shot.Seed ^ 0xBADC0FFEu);
            int particleCount = (int)Math.Round(12 * (settings == null ? 1 : MusicStageUtil.Clamp(settings.DecorDensity, 0.2, 1.8)));
            particleCount = Math.Max(3, Math.Min(30, particleCount));
            for (int i = 0; i < particleCount; i++)
            {
                var dot = new Ellipse
                {
                    Width = rng.Range(1.4, 3.2),
                    Height = rng.Range(1.4, 3.2),
                    Fill = new SolidColorBrush(MusicStageUtil.Alpha(i % 3 == 0 ? accent : Ink, 90)),
                    IsHitTestVisible = false
                };
                var p = new Particle
                {
                    Node = dot,
                    X = w * rng.Range(0.08, 0.92),
                    Y = h * rng.Range(0.1, 0.88),
                    Amplitude = rng.Range(3, 14),
                    Speed = rng.Range(0.15, 0.5),
                    Phase = rng.Range(0, Math.PI * 2),
                    Alpha = rng.Range(0.16, 0.55)
                };
                Canvas.SetLeft(dot, p.X);
                Canvas.SetTop(dot, p.Y);
                visual.ParticlesLayer.Children.Add(dot);
                visual.Particles.Add(p);
            }
        }

        // ---------------------------------------------------------------- 词的视觉

        WordVisual CreateWordVisual(MusicStageShot shot, MusicStageWord word, double w, double h)
        {
            var v = new WordVisual();
            v.Word = word;
            double rw = Math.Max(4, word.Width);
            double rh = Math.Max(4, word.Height);
            var canvas = new Canvas
            {
                Width = rw,
                Height = rh,
                ClipToBounds = false,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            v.Root = canvas;

            var fontFamily = MusicStageText.FamilyFor(word.Text);
            var foreground = new SolidColorBrush(word.IsDecoration ? MusicStageUtil.Alpha(accent, 120) : Ink);

            if (word.IsHighlight && !word.IsDecoration)
            {
                double padX = word.FontSize * 0.22;
                double padY = word.FontSize * 0.10;
                var block = new Border
                {
                    Width = rw + padX,
                    Height = rh + padY,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(stageStyle != null && stageStyle.IsLight
                        ? Color.FromArgb(24, 0, 0, 0)
                        : Color.FromArgb(30, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(stageStyle != null && stageStyle.IsLight
                        ? Color.FromArgb(20, 0, 0, 0)
                        : Color.FromArgb(24, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    IsHitTestVisible = false,
                    Opacity = 0.62
                };
                Canvas.SetLeft(block, -padX / 2);
                Canvas.SetTop(block, -padY / 2);
                v.Highlight = block;
                canvas.Children.Add(block);
            }

            if (word.IsDecoration)
            {
                Geometry geometry = MusicStageText.BuildGeometry(word.Text, word.FontSize, word.IsBold);
                var path = new Path
                {
                    Data = geometry,
                    Stroke = new SolidColorBrush(MusicStageUtil.Alpha(accent, 150)),
                    StrokeThickness = Math.Max(0.9, word.FontSize * 0.0013),
                    Fill = new SolidColorBrush(Color.FromArgb(7, 255, 255, 255)),
                    IsHitTestVisible = false
                };
                v.Outline = path;
                canvas.Children.Add(path);
            }
            else
            {
                if (word.IsHero || word.IsSemiHero)
                {
                    var halo = MakeWordText(word, fontFamily, rw, rh, Brushes.White);
                    halo.Foreground = new SolidColorBrush(MusicStageUtil.Alpha(accent, 235));
                    halo.Effect = new BlurEffect { Radius = Math.Max(14, word.FontSize * 0.33), RenderingBias = RenderingBias.Performance };
                    halo.Opacity = 0.5;
                    v.Halo = halo;
                    canvas.Children.Add(halo);
                }
                if (word.HasChroma)
                {
                    double offset = Math.Max(1.4, word.FontSize * 0.017);
                    var cyan = MakeWordText(word, fontFamily, rw, rh, Brushes.White);
                    cyan.Foreground = new SolidColorBrush(Color.FromRgb(0, 232, 255));
                    cyan.Opacity = 0.42;
                    cyan.RenderTransform = new TranslateTransform(-offset, offset * 0.45);
                    v.Cyan = cyan;
                    canvas.Children.Add(cyan);

                    var red = MakeWordText(word, fontFamily, rw, rh, Brushes.White);
                    red.Foreground = new SolidColorBrush(Color.FromRgb(255, 28, 105));
                    red.Opacity = 0.42;
                    red.RenderTransform = new TranslateTransform(offset, -offset * 0.45);
                    v.Red = red;
                    canvas.Children.Add(red);
                }
                var main = MakeWordText(word, fontFamily, rw, rh, foreground);
                main.Effect = new DropShadowEffect
                {
                    Color = stageStyle != null && stageStyle.IsLight ? Colors.Black : Colors.White,
                    ShadowDepth = 0,
                    BlurRadius = word.IsHero || word.IsSemiHero ? Math.Max(16, word.FontSize * 0.22) : Math.Max(8, word.FontSize * 0.16),
                    Opacity = (word.IsHero || word.IsSemiHero ? 0.58 : 0.30) * Math.Min(1.5, Math.Max(0.2, settings == null ? 1 : settings.OverallGlow / 0.8)),
                    RenderingBias = RenderingBias.Performance
                };
                v.Main = main;
                canvas.Children.Add(main);
            }

            v.Scale = new ScaleTransform(1, 1);
            v.Rotate = new RotateTransform(word.Rotation);
            v.Move = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(v.Scale);
            group.Children.Add(v.Rotate);
            group.Children.Add(v.Move);
            canvas.RenderTransform = group;

            Canvas.SetLeft(canvas, word.X - rw / 2);
            Canvas.SetTop(canvas, word.Y - rh / 2);
            return v;
        }

        static TextBlock MakeWordText(MusicStageWord word, FontFamily family, double width, double height, Brush foreground)
        {
            var text = new TextBlock
            {
                Text = word.IsVertical ? MusicStageUtil.JoinChars(word.Text) : word.Text,
                FontFamily = family,
                FontSize = Math.Max(1, word.FontSize),
                FontWeight = word.IsBold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
                Width = width,
                Height = height,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.None,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            if (word.IsVertical)
            {
                text.LineHeight = Math.Max(1, word.FontSize * 0.86);
            }
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);
            return text;
        }

        // ---------------------------------------------------------------- 辅助线

        void AddScreenGuides(Canvas layer, double w, double h)
        {
            AddLine(layer, w * 0.2, h * 0.08, w * 0.2, h * 0.92, MusicStageUtil.Alpha(Ink, 18), 1, new double[] { 3, 12 });
            AddLine(layer, w * 0.8, h * 0.08, w * 0.8, h * 0.92, MusicStageUtil.Alpha(Ink, 18), 1, new double[] { 3, 12 });
            AddLine(layer, w * 0.05, h * 0.2, w * 0.95, h * 0.2, MusicStageUtil.Alpha(Ink, 14), 1, null);
            AddLine(layer, w * 0.05, h * 0.8, w * 0.95, h * 0.8, MusicStageUtil.Alpha(Ink, 14), 1, null);
        }

        void AddWordGuides(Canvas layer, ShotVisual visual, double w, double h)
        {
            for (int i = 0; i < visual.Words.Count; i++)
            {
                WordVisual v = visual.Words[i];
                MusicStageWord word = v.Word;
                if (word.IsDecoration || word.Guide <= 0) continue;
                double pad = Math.Max(18, word.FontSize * 0.18);
                double left = word.X - word.Width / 2 - pad;
                double top = word.Y - word.Height / 2 - pad;
                double width = word.Width + pad * 2;
                double height = word.Height + pad * 2;
                var color = MusicStageUtil.Alpha(word.Guide == 1 ? accent : Ink, (byte)(word.Guide == 1 ? 66 : 38));
                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                if (word.Guide == 2) rect.StrokeDashArray = new DoubleCollection(new double[] { 4, 7 });
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                layer.Children.Add(rect);

                // 四角的测量短线
                double tick = Math.Max(5, pad * 0.42);
                Color cornerColor = MusicStageUtil.Alpha(accent, (byte)(word.Guide == 1 ? 110 : 54));
                AddLine(layer, left, top, left + tick, top, cornerColor, 1.2, null);
                AddLine(layer, left, top, left, top + tick, cornerColor, 1.2, null);
                AddLine(layer, left + width, top, left + width - tick, top, cornerColor, 1.2, null);
                AddLine(layer, left + width, top, left + width, top + tick, cornerColor, 1.2, null);
                AddLine(layer, left, top + height, left + tick, top + height, cornerColor, 1.2, null);
                AddLine(layer, left, top + height, left, top + height - tick, cornerColor, 1.2, null);
                AddLine(layer, left + width, top + height, left + width - tick, top + height, cornerColor, 1.2, null);
                AddLine(layer, left + width, top + height, left + width, top + height - tick, cornerColor, 1.2, null);

                if (word.Guide == 1)
                {
                    AddLine(layer, left + width / 2, top - 58, left + width / 2, top - 8,
                        MusicStageUtil.Alpha(accent, 58), 1, new double[] { 2, 5 });
                    AddLine(layer, left + width / 2, top + height + 8, left + width / 2, top + height + 58,
                        MusicStageUtil.Alpha(accent, 58), 1, new double[] { 2, 5 });
                    var dot = new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Stroke = new SolidColorBrush(MusicStageUtil.Alpha(accent, 160)),
                        StrokeThickness = 1,
                        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                    };
                    Canvas.SetLeft(dot, left + width - 3.5);
                    Canvas.SetTop(dot, top - 3.5);
                    layer.Children.Add(dot);
                }
            }
        }

        // ================================================================ 控制条

        void BuildChrome()
        {
            bar = new Border
            {
                Width = 500,
                Height = 68,
                CornerRadius = new CornerRadius(34),
                Background = new SolidColorBrush(Color.FromArgb(225, 10, 11, 15)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 8,
                    BlurRadius = 30,
                    Opacity = 0.62,
                    RenderingBias = RenderingBias.Performance
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });
            bar.Child = grid;

            playButton = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "播放 / 暂停"
            };
            playButton.Child = Icon("play", Color.FromRgb(12, 13, 17), 18);
            playButton.MouseLeftButtonUp += delegate { if (togglePlay != null) togglePlay(); };
            Grid.SetColumn(playButton, 0);
            grid.Children.Add(playButton);

            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            titleText = new TextBlock
            {
                Text = "未在播放",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(238, 244, 244, 245)),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 300
            };
            center.Children.Add(titleText);

            var progressRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            leftTime = TimeLabel("0:00", HorizontalAlignment.Left);
            rightTime = TimeLabel("0:00", HorizontalAlignment.Right);
            Grid.SetColumn(leftTime, 0);
            Grid.SetColumn(rightTime, 2);
            progressRow.Children.Add(leftTime);
            progressRow.Children.Add(rightTime);

            progressHost = new Grid { Height = 18, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var track = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            progressHost.Children.Add(track);
            progressFill = new Border
            {
                Height = 3,
                Width = 0,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(MusicStageUtil.Lift(accent, 0.35)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            progressHost.Children.Add(progressFill);
            progressKnob = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(0, 0)
            };
            progressHost.Children.Add(progressKnob);
            progressHost.MouseLeftButtonDown += ProgressDown;
            progressHost.MouseMove += ProgressMove;
            progressHost.MouseLeftButtonUp += ProgressUp;
            progressHost.MouseLeave += delegate { if (seeking) ProgressUp(null, null); };
            Grid.SetColumn(progressHost, 1);
            progressRow.Children.Add(progressHost);
            center.Children.Add(progressRow);

            Grid.SetColumn(center, 1);
            grid.Children.Add(center);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            modeButton = BarButton("shuffle", "播放模式", delegate
            {
                if (cycleMode != null) cycleMode();
            });
            expandButton = BarButton("gear", "歌词样式设置", delegate { ToggleStylePanel(); });
            closeButton = BarButton("close", "收回投射（Esc）", delegate { Close(); });
            right.Children.Add(modeButton);
            right.Children.Add(expandButton);
            right.Children.Add(closeButton);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            bar.MouseEnter += delegate { if (!chromeHidden) bar.Background = new SolidColorBrush(Color.FromArgb(240, 10, 11, 15)); };
            bar.MouseLeave += delegate { bar.Background = new SolidColorBrush(Color.FromArgb(225, 10, 11, 15)); };
            chromeLayer.Children.Add(bar);

            BuildHint();
            BuildThemeLabel();

            subtitleText = new TextBlock
            {
                Width = 900,
                FontSize = 14,
                FontFamily = MusicStageText.FamilyFor("字幕"),
                Foreground = new SolidColorBrush(Ink),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.82,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 0,
                    BlurRadius = 10,
                    Opacity = 0.8,
                    RenderingBias = RenderingBias.Performance
                }
            };
            chromeLayer.Children.Add(subtitleText);
        }

        void BuildInfoCard()
        {
            infoCard = new Border
            {
                Width = 368,
                Height = 318,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(238, 11, 12, 17)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 10,
                    BlurRadius = 34,
                    Opacity = 0.6,
                    RenderingBias = RenderingBias.Performance
                }
            };
            var grid = new Grid { Margin = new Thickness(20, 20, 20, 18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            infoCoverHost = new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            grid.Children.Add(infoCoverHost);

            var copy = new StackPanel { Margin = new Thickness(116, 2, 0, 0) };
            infoTitle = new TextBlock
            {
                Text = "未在播放",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Ink),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 210
            };
            infoArtist = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(MusicStageUtil.Alpha(accent, 220)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 210,
                Margin = new Thickness(0, 4, 0, 0)
            };
            infoAlbum = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Dim),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 210,
                Margin = new Thickness(0, 3, 0, 0)
            };
            copy.Children.Add(infoTitle);
            copy.Children.Add(infoArtist);
            copy.Children.Add(infoAlbum);
            grid.Children.Add(copy);

            var body = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
            body.Children.Add(new TextBlock
            {
                Text = "FOLIA · SONNET",
                FontSize = 10,
                Foreground = new SolidColorBrush(MusicStageUtil.Alpha(accent, 190)),
                FontWeight = FontWeights.SemiBold
            });
            body.Children.Add(new TextBlock
            {
                Text = "歌词按段落排成一张海报；每个词在唱到它时出现。",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 200, 203, 212)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = "空格 暂停 · ← → 快进 · Esc 收回",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 180, 184, 196)),
                Margin = new Thickness(0, 10, 0, 0)
            });
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var close = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                Child = Icon("close", Color.FromArgb(220, 255, 255, 255), 13)
            };
            close.MouseLeftButtonUp += delegate { SetInfoVisible(false); };
            Grid.SetRow(close, 0);
            Grid.SetColumn(close, 0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(close);

            infoCard.Child = grid;
            infoCard.RenderTransform = new TranslateTransform(0, 12);
            chromeLayer.Children.Add(infoCard);
        }

        void BuildHint()
        {
            hintText = new TextBlock
            {
                Text = "Esc 收回投射    ·    空格 暂停    ·    ← → 快进",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(170, 220, 222, 230)),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            chromeLayer.Children.Add(hintText);
            var fade = new DoubleAnimation(0.9, 0, TimeSpan.FromSeconds(1.2));
            fade.BeginTime = TimeSpan.FromSeconds(5.5);
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            hintText.BeginAnimation(OpacityProperty, fade);
        }

        void BuildThemeLabel()
        {
            themeText = new TextBlock
            {
                Text = "[ THEME ] MIDNIGHT DEFAULT",
                FontSize = 10,
                Foreground = new SolidColorBrush(MusicStageUtil.Alpha(Ink, 80)),
                RenderTransform = new RotateTransform(-90, 0, 0),
                IsHitTestVisible = false
            };
            chromeLayer.Children.Add(themeText);
        }

        static TextBlock TimeLabel(string text, HorizontalAlignment align)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(150, 214, 217, 225)),
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = align == HorizontalAlignment.Left ? new Thickness(0, 0, 0, 0) : new Thickness(0, 0, 0, 0)
            };
        }

        Border BarButton(string icon, string tooltip, Action click)
        {
            var button = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = Icon(icon, Color.FromArgb(205, 236, 238, 244), 17),
                Margin = new Thickness(2, 0, 2, 0)
            };
            button.MouseEnter += delegate { button.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate { if (click != null) click(); };
            return button;
        }

        static new FrameworkElement Icon(string kind, Color color, double size)
        {
            var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
            var brush = new SolidColorBrush(color);
            if (kind == "play")
            {
                canvas.Children.Add(new Path { Data = Geometry.Parse("M8,5 L19,12 L8,19 Z"), Fill = brush });
            }
            else if (kind == "pause")
            {
                var bar1 = new Rectangle { Width = 4, Height = 13, RadiusX = 1.2, RadiusY = 1.2, Fill = brush };
                Canvas.SetLeft(bar1, 7);
                Canvas.SetTop(bar1, 5.5);
                canvas.Children.Add(bar1);
                var bar2 = new Rectangle { Width = 4, Height = 13, RadiusX = 1.2, RadiusY = 1.2, Fill = brush };
                Canvas.SetLeft(bar2, 13);
                Canvas.SetTop(bar2, 5.5);
                canvas.Children.Add(bar2);
            }
            else if (kind == "prev" || kind == "next")
            {
                var bar = new Rectangle { Width = 2.4, Height = 13, RadiusX = 1.2, RadiusY = 1.2, Fill = brush };
                Canvas.SetLeft(bar, kind == "prev" ? 5 : 16.6);
                Canvas.SetTop(bar, 5.5);
                canvas.Children.Add(bar);
                canvas.Children.Add(new Path { Data = Geometry.Parse(kind == "prev" ? "M17,5.5 L9,12 L17,18.5 Z" : "M7,5.5 L15,12 L7,18.5 Z"), Fill = brush });
            }
            else if (kind == "shuffle")
            {
                canvas.Children.Add(new Path { Data = Geometry.Parse("M3,7 L7,7 L17,17 L21,17 M3,17 L7,17 L17,7 L21,7"), Stroke = brush, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M17,7 L21,7 L21,11 M17,17 L21,17 L21,13"), Stroke = brush, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "repeat")
            {
                canvas.Children.Add(new Path { Data = Geometry.Parse("M7,6 L17,6 L20,9 M4,9 L4,12 M17,18 L7,18 L4,15 M20,15 L20,12"), Stroke = brush, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "sequential")
            {
                for (int i = 0; i < 3; i++)
                {
                    var line = new Rectangle { Width = 13, Height = 1.8, Fill = brush };
                    Canvas.SetLeft(line, 4);
                    Canvas.SetTop(line, 6 + i * 5);
                    canvas.Children.Add(line);
                }
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,6 L22,8 L18,10 Z"), Fill = brush });
            }
            else if (kind == "list")
            {
                for (int i = 0; i < 3; i++)
                {
                    var dot = new Ellipse { Width = 2.4, Height = 2.4, Fill = brush };
                    Canvas.SetLeft(dot, 4);
                    Canvas.SetTop(dot, 5.2 + i * 5.5);
                    canvas.Children.Add(dot);
                    var line = new Rectangle { Width = 13, Height = 1.8, Fill = brush };
                    Canvas.SetLeft(line, 9);
                    Canvas.SetTop(line, 5.5 + i * 5.5);
                    canvas.Children.Add(line);
                }
            }
            else if (kind == "close")
            {
                canvas.Children.Add(new Path { Data = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"), Stroke = brush, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else if (kind == "gear")
            {
                // 齿轮：一个圆 + 8 个齿
                canvas.Children.Add(new Ellipse { Width = 8.5, Height = 8.5, Stroke = brush, StrokeThickness = 1.6, Margin = new Thickness(7.75, 7.75, 0, 0) });
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    double x1 = 12 + Math.Cos(a) * 7.2;
                    double y1 = 12 + Math.Sin(a) * 7.2;
                    double x2 = 12 + Math.Cos(a) * 10.2;
                    double y2 = 12 + Math.Sin(a) * 10.2;
                    canvas.Children.Add(new Line
                    {
                        X1 = x1,
                        Y1 = y1,
                        X2 = x2,
                        Y2 = y2,
                        Stroke = brush,
                        StrokeThickness = 2,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    });
                }
            }
            else if (kind == "volume")
            {
                canvas.Children.Add(new Path { Data = Geometry.Parse("M4,9 L8,9 L12,5 L12,19 L8,15 L4,15 Z"), Fill = brush });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M15,9 A4,4 0 0 1 15,15"), Stroke = brush, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round });
            }
            else if (kind == "cast")
            {
                canvas.Children.Add(new Rectangle { Width = 16, Height = 11, RadiusX = 2, RadiusY = 2, Stroke = brush, StrokeThickness = 1.4, Margin = new Thickness(4, 4, 0, 0) });
                canvas.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = brush, Margin = new Thickness(10.5, 10, 0, 0) });
            }
            else
            {
                canvas.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = brush, Margin = new Thickness(10, 10, 0, 0) });
            }
            var viewbox = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, IsHitTestVisible = false };
            return viewbox;
        }

        // ================================================================ 位置 / 状态

        void PositionChrome()
        {
            double w = W;
            double h = H;
            if (w < 200 || h < 200) return;

            if (grain != null) { grain.Width = w; grain.Height = h; }
            if (vignette != null) { vignette.Width = w; vignette.Height = h; }
            if (shotHost != null) { shotHost.Width = w; shotHost.Height = h; }
            if (frameLayer != null) { frameLayer.Width = w; frameLayer.Height = h; }
            if (chromeLayer != null) { chromeLayer.Width = w; chromeLayer.Height = h; }

            if (bar != null)
            {
                bar.Width = Math.Min(540, Math.Max(390, w - 80));
                bar.Height = 68;
                Canvas.SetLeft(bar, (w - bar.Width) / 2);
                Canvas.SetTop(bar, h - 34 - bar.Height);
            }
            if (infoCard != null)
            {
                Canvas.SetLeft(infoCard, w - 30 - infoCard.Width);
                Canvas.SetTop(infoCard, Math.Max(24, h - 34 - 68 - 20 - infoCard.Height));
            }
            if (hintText != null)
            {
                hintText.Width = w;
                Canvas.SetLeft(hintText, 0);
                Canvas.SetTop(hintText, 24);
            }
            if (themeText != null)
            {
                Canvas.SetLeft(themeText, 26);
                Canvas.SetTop(themeText, h - 32);
            }
            if (subtitleText != null)
            {
                subtitleText.Width = Math.Min(1100, w - 80);
                Canvas.SetLeft(subtitleText, (w - subtitleText.Width) / 2);
                Canvas.SetTop(subtitleText, h - 34 - 68 - 32);
            }
            if (stylePanelRoot != null) PositionStylePanel();
            if (stylePanelDimmer != null)
            {
                stylePanelDimmer.Width = w;
                stylePanelDimmer.Height = h;
            }
        }

        void BuildFrame()
        {
            if (frameLayer == null) return;
            frameLayer.Children.Clear();
            double w = W;
            double h = H;
            if (w < 200 || h < 200) return;
            var color = MusicStageUtil.Alpha(Ink, 30);
            AddLine(frameLayer, 36, 30, 36, h - 30, color, 1, null);
            AddLine(frameLayer, w - 36, 30, w - 36, h - 30, color, 1, null);
            AddLine(frameLayer, 36, 30, w - 36, 30, MusicStageUtil.Alpha(Ink, 20), 1, null);
            AddLine(frameLayer, 36, h - 30, w - 36, h - 30, MusicStageUtil.Alpha(Ink, 20), 1, null);
            AddLine(frameLayer, 36, 30, 76, 30, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, 36, 30, 36, 70, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, w - 36, 30, w - 76, 30, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, w - 36, 30, w - 36, 70, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, 36, h - 30, 76, h - 30, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, 36, h - 30, 36, h - 70, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, w - 36, h - 30, w - 76, h - 30, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
            AddLine(frameLayer, w - 36, h - 30, w - 36, h - 70, MusicStageUtil.Alpha(Ink, 80), 1.4, null);
        }

        void UpdateChrome(MusicStageSnapshot s)
        {
            if (bar == null) return;
            bool hasSong = s != null && (!string.IsNullOrEmpty(s.SongMid) || !string.IsNullOrEmpty(s.Title));
            string title;
            if (s == null) title = "未在播放";
            else if (s.Resolving) title = "正在获取播放地址…";
            else if (!string.IsNullOrEmpty(s.Title)) title = s.Title;
            else title = "未在播放";
            if (titleText.Text != title) titleText.Text = title;

            TimeSpan duration = s == null ? TimeSpan.Zero : s.Duration;
            TimeSpan position = s == null ? TimeSpan.Zero : s.Position;
            string left = MusicStageUtil.FormatTime(position);
            string right = duration.TotalSeconds > 0 ? MusicStageUtil.FormatTime(duration) : "0:00";
            if (leftTime.Text != left) leftTime.Text = left;
            if (rightTime.Text != right) rightTime.Text = right;

            string playIcon = s != null && s.Playing ? "pause" : "play";
            if (playIcon != lastPlayIcon)
            {
                lastPlayIcon = playIcon;
                playButton.Child = Icon(playIcon, Color.FromRgb(12, 13, 17), 18);
            }
            string mode = s == null ? "shuffle" : (s.PlayMode == null ? "shuffle" : s.PlayMode);
            if (mode != lastModeIcon)
            {
                lastModeIcon = mode;
                modeButton.Child = Icon(mode, Color.FromArgb(205, 236, 238, 244), 17);
                modeButton.ToolTip = ModeLabel(mode);
            }
            if (infoVisible && s != null)
            {
                if (infoTitle.Text != (string.IsNullOrEmpty(s.Title) ? "未在播放" : s.Title)) infoTitle.Text = string.IsNullOrEmpty(s.Title) ? "未在播放" : s.Title;
                if (infoArtist.Text != (s.Artist == null ? "" : s.Artist)) infoArtist.Text = s.Artist == null ? "" : s.Artist;
                string sourceLine = string.IsNullOrEmpty(s.Source) ? "" : "— " + s.Source;
                if (infoAlbum.Text != sourceLine) infoAlbum.Text = sourceLine;
            }
            if (!seeking) UpdateProgressVisual(duration.TotalSeconds > 0 ? position.TotalSeconds / duration.TotalSeconds : 0);
            if (!hasSong)
            {
                // 没歌时保持控制条可见，点播放会从列表第一首开始
                if (titleText.Text != "未在播放") titleText.Text = "未在播放";
            }
        }

        static string ModeLabel(string mode)
        {
            if (mode == "repeat") return "单曲循环";
            if (mode == "sequential") return "顺序播放";
            return "随机播放";
        }

        void UpdateProgressVisual(double ratio)
        {
            if (progressHost == null || progressFill == null) return;
            double width = progressHost.ActualWidth;
            if (width <= 1) return;
            ratio = MusicStageUtil.Clamp(ratio, 0, 1);
            progressFill.Width = width * ratio;
            var move = progressKnob.RenderTransform as TranslateTransform;
            if (move != null) move.X = width * ratio - 4.5;
        }

        // ================================================================ 交互

        void ProgressDown(object sender, MouseButtonEventArgs e)
        {
            if (progressHost == null) return;
            seeking = true;
            progressHost.CaptureMouse();
            seekRatio = MusicStageUtil.Clamp(e.GetPosition(progressHost).X / Math.Max(1, progressHost.ActualWidth), 0, 1);
            UpdateProgressVisual(seekRatio);
            e.Handled = true;
        }

        void ProgressMove(object sender, MouseEventArgs e)
        {
            if (!seeking || progressHost == null) return;
            seekRatio = MusicStageUtil.Clamp(e.GetPosition(progressHost).X / Math.Max(1, progressHost.ActualWidth), 0, 1);
            UpdateProgressVisual(seekRatio);
        }

        void ProgressUp(object sender, MouseButtonEventArgs e)
        {
            if (!seeking) return;
            seeking = false;
            try { progressHost.ReleaseMouseCapture(); } catch (Exception) { }
            if (seek != null && last != null && last.Duration.TotalSeconds > 0)
            {
                seek(TimeSpan.FromSeconds(last.Duration.TotalSeconds * seekRatio));
            }
            if (e != null) e.Handled = true;
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (stylePanelVisible) CloseStylePanel();
                else Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                if (togglePlay != null) togglePlay();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                if (last != null && seek != null) seek(TimeSpan.FromSeconds(Math.Max(0, last.Position.TotalSeconds - 5)));
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                if (last != null && seek != null) seek(TimeSpan.FromSeconds(last.Position.TotalSeconds + 5));
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (nextTrack != null) nextTrack();
                e.Handled = true;
            }
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (previousTrack != null) previousTrack();
                e.Handled = true;
            }
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (Cursor == Cursors.None) Cursor = Cursors.Arrow;
            cursorTimer.Stop();
            cursorTimer.Start();
        }

        void OnBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (stylePanelVisible)
            {
                if (IsInsideStylePanel(e.OriginalSource)) return;
                CloseStylePanel();
                e.Handled = true;
                return;
            }
            if (infoVisible)
            {
                SetInfoVisible(false);
                e.Handled = true;
            }
        }

        void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (stylePanelVisible)
            {
                if (IsInsideStylePanel(e.OriginalSource)) return;
                CloseStylePanel();
                return;
            }
            // 双击空白处进入"纯画面"：隐藏控制条；再双击恢复
            if (bar != null && bar.IsMouseOver) return;
            chromeHidden = !chromeHidden;
            if (bar != null) bar.Visibility = chromeHidden ? Visibility.Collapsed : Visibility.Visible;
            if (hintText != null) hintText.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        bool IsInsideStylePanel(object source)
        {
            if (stylePanelRoot == null) return false;
            DependencyObject node = source as DependencyObject;
            while (node != null)
            {
                if (node == stylePanelRoot) return true;
                try { node = VisualTreeHelper.GetParent(node); }
                catch (Exception) { node = null; }
            }
            return false;
        }

        void ToggleStylePanel()
        {
            if (stylePanelVisible)
            {
                CloseStylePanel();
                return;
            }
            if (stylePanel == null)
            {
                stylePanel = new MusicStageStylePanel(settings, ApplyStageSettings, CloseStylePanel);
                stylePanelRoot = stylePanel.Root;
            }
            if (stylePanelDimmer == null)
            {
                stylePanelDimmer = new Border
                {
                    Width = Math.Max(1, W),
                    Height = Math.Max(1, H),
                    Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                    Cursor = Cursors.Arrow
                };
                stylePanelDimmer.MouseLeftButtonDown += delegate { CloseStylePanel(); };
            }
            if (!chromeLayer.Children.Contains(stylePanelDimmer)) chromeLayer.Children.Add(stylePanelDimmer);
            if (!chromeLayer.Children.Contains(stylePanelRoot)) chromeLayer.Children.Add(stylePanelRoot);
            stylePanelDimmer.Visibility = Visibility.Visible;
            stylePanelRoot.Visibility = Visibility.Visible;
            stylePanelDimmer.Width = W;
            stylePanelDimmer.Height = H;
            PositionStylePanel();
            stylePanelVisible = true;
            if (expandButton != null)
            {
                expandButton.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            }
        }

        void CloseStylePanel()
        {
            stylePanelVisible = false;
            if (stylePanelDimmer != null) stylePanelDimmer.Visibility = Visibility.Collapsed;
            if (stylePanelRoot != null) stylePanelRoot.Visibility = Visibility.Collapsed;
            if (expandButton != null) expandButton.Background = Brushes.Transparent;
        }

        void PositionStylePanel()
        {
            if (stylePanelRoot == null) return;
            double left = Math.Max(16, (W - stylePanelRoot.Width) / 2);
            double top = Math.Max(16, (H - stylePanelRoot.Height) / 2);
            Canvas.SetLeft(stylePanelRoot, left);
            Canvas.SetTop(stylePanelRoot, top);
            if (stylePanelDimmer != null)
            {
                Canvas.SetLeft(stylePanelDimmer, 0);
                Canvas.SetTop(stylePanelDimmer, 0);
            }
        }

        void ApplyStageSettings()
        {
            if (settings == null) return;
            string oldStyle = stageStyle == null ? "" : stageStyle.Id;
            double oldFont = settings.FontScale;
            double oldGlow = settings.OverallGlow;
            double oldDecor = settings.DecorDensity;
            bool coverChanged = appliedCoverTint != settings.CoverTint;
            appliedCoverTint = settings.CoverTint;
            stageStyle = MusicStageStyles.Find(settings.StyleId);
            MusicStageText.SetFontKind(stageStyle.FontKind);
            Ink = stageStyle.Ink;
            Dim = stageStyle.Dim;
            BgTop = stageStyle.BgTop;
            BgBottom = stageStyle.BgBottom;
            if (!settings.CoverTint)
            {
                accent = stageStyle.Accent;
                albumKey = "";
            }
            if (coverChanged) albumKey = "";
            if (stageDim != null) stageDim.Opacity = MusicStageUtil.Clamp(settings.BackgroundDim, 0, 0.85);
            if (vignette != null) vignette.Opacity = stageStyle.IsLight ? 0.45 : 0.8;
            if (subtitleText != null)
            {
                subtitleText.FontFamily = MusicStageText.FamilyFor("字幕");
                subtitleText.Foreground = new SolidColorBrush(stageStyle.Ink);
            }
            settings.Save();
            bool hard = !string.Equals(oldStyle, stageStyle.Id, StringComparison.Ordinal)
                || Math.Abs(oldFont - settings.FontScale) > 0.0001
                || Math.Abs(oldGlow - settings.OverallGlow) > 0.0001
                || Math.Abs(oldDecor - settings.DecorDensity) > 0.0001
                || coverChanged;
            if (hard)
            {
                RebuildAll(true);
            }
            else if (last != null)
            {
                UpdateChrome(last);
            }
        }

        void UpdateSubtitle()
        {
            if (subtitleText == null) return;
            if (settings == null || !settings.ShowTranslation || last == null || last.Lyrics == null)
            {
                subtitleText.Visibility = Visibility.Collapsed;
                return;
            }
            string text = "";
            double pos = last.Position.TotalSeconds;
            for (int i = 0; i < last.Lyrics.Count; i++)
            {
                MusicStageLyric line = last.Lyrics[i];
                if (line == null) continue;
                if (line.Time.TotalSeconds > pos + 0.05) break;
                text = line.Text == null ? "" : line.Text.Trim();
            }
            if (string.IsNullOrEmpty(text))
            {
                subtitleText.Visibility = Visibility.Collapsed;
                return;
            }
            if (subtitleText.Text != text) subtitleText.Text = text;
            subtitleText.Visibility = Visibility.Visible;
        }

        void SetInfoVisible(bool visible)
        {
            if (infoCard == null) return;
            infoVisible = visible;
            if (visible)
            {
                infoCard.Visibility = Visibility.Visible;
                infoCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                var move = infoCard.RenderTransform as TranslateTransform;
                if (move != null)
                {
                    var animation = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260));
                    animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    move.BeginAnimation(TranslateTransform.YProperty, animation);
                }
                if (expandButton != null) expandButton.Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
            }
            else
            {
                var fade = new DoubleAnimation(infoCard.Opacity, 0, TimeSpan.FromMilliseconds(160));
                fade.Completed += delegate { if (!infoVisible) infoCard.Visibility = Visibility.Collapsed; };
                infoCard.BeginAnimation(OpacityProperty, fade);
                if (expandButton != null) expandButton.Background = Brushes.Transparent;
            }
        }

        void FadeIn()
        {
            Opacity = 0;
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420));
            animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, animation);
        }

        // ================================================================ 封面 / 主题色

        void LoadAlbum(string albumMid)
        {
            if (closed) return;
            if (settings != null && !settings.CoverTint)
            {
                albumKey = "";
                tintPending = false;
                return;
            }
            if (string.IsNullOrEmpty(albumMid))
            {
                albumKey = "";
                tintPending = false;
                return;
            }
            if (albumMid == albumKey) return;
            albumKey = albumMid;
            string url32 = "https://y.gtimg.cn/music/photo_new/T002R100x100M000" + albumMid + ".jpg";

            if (tintPending) return;
            tintPending = true;
            try
            {
                var tiny = new BitmapImage();
                tiny.BeginInit();
                tiny.UriSource = new Uri(url32);
                tiny.DecodePixelWidth = 32;
                tiny.CacheOption = BitmapCacheOption.OnLoad;
                tiny.EndInit();
                tiny.DownloadCompleted += delegate
                {
                    tintPending = false;
                    if (albumKey != albumMid) return;
                    Color tint = AverageColor(tiny);
                    if (tint.A > 0)
                    {
                        SetAccent(SaturateTint(tint));
                    }
                };
                tiny.DownloadFailed += delegate { tintPending = false; };
            }
            catch (Exception) { tintPending = false; }
        }

        static Color AverageColor(BitmapSource source)
        {
            try
            {
                int width = source.PixelWidth;
                int height = source.PixelHeight;
                if (width <= 0 || height <= 0) return Colors.Transparent;
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                long r = 0, g = 0, b = 0;
                long count = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int a = pixels[i + 3];
                    if (a < 40) continue;
                    int bb = pixels[i];
                    int gg = pixels[i + 1];
                    int rr = pixels[i + 2];
                    if (rr + gg + bb < 36) continue;
                    r += rr;
                    g += gg;
                    b += bb;
                    count++;
                }
                if (count == 0) return Colors.Transparent;
                return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
            catch (Exception) { return Colors.Transparent; }
        }

        static Color SaturateTint(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            int sat = max - min;
            Color result = color;
            if (sat < 22) result = MusicStageUtil.Mix(color, DefaultAccent, 0.55);
            if (max < 68) result = MusicStageUtil.Lift(result, 0.28);
            result = MusicStageUtil.Mix(result, Colors.White, 0.18);
            return result;
        }

        void SetAccent(Color color)
        {
            if (closed) return;
            if (settings == null || !settings.CoverTint) return;
            if (color == accent) return;
            accent = color;
            if (progressFill != null) progressFill.Background = new SolidColorBrush(MusicStageUtil.Lift(accent, 0.35));
            if (themeText != null) themeText.Foreground = new SolidColorBrush(MusicStageUtil.Alpha(Ink, 80));
            if (last != null && !suppressTintRebuild)
            {
                suppressTintRebuild = true;
                try
                {
                    program = MusicStageProgram.Build(last, W, H, settings);
                    activeIndex = -1;
                    activeVisual = null;
                    shotHost.Children.Clear();
                    ShowShot(FindShotIndex(currentPos), currentPos, true);
                }
                catch (Exception) { }
                suppressTintRebuild = false;
            }
        }

        // ================================================================ 静态素材

        static Brush grainBrush;
        static Brush vignetteBrush;

        static Brush GrainBrush()
        {
            if (grainBrush != null) return grainBrush;
            const int size = 256;
            var pixels = new byte[size * size * 4];
            var random = new Random(20261107);
            for (int i = 0; i < size * size; i++)
            {
                int v = random.Next(0, 256);
                pixels[i * 4] = (byte)v;
                pixels[i * 4 + 1] = (byte)v;
                pixels[i * 4 + 2] = (byte)v;
                pixels[i * 4 + 3] = (byte)random.Next(0, 22);
            }
            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            brush.Freeze();
            grainBrush = brush;
            return grainBrush;
        }

        static Brush VignetteBrush()
        {
            if (vignetteBrush != null) return vignetteBrush;
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.48),
                GradientOrigin = new Point(0.5, 0.48),
                RadiusX = 0.72,
                RadiusY = 0.72,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5),
                    new GradientStop(Color.FromArgb(70, 0, 0, 0), 0.8),
                    new GradientStop(Color.FromArgb(170, 0, 0, 0), 1)
                }
            };
            brush.Freeze();
            vignetteBrush = brush;
            return vignetteBrush;
        }

        // ================================================================ 小模型

        sealed class ShotVisual
        {
            public MusicStageShot Descriptor;
            public Canvas Root;
            public Canvas Decor;
            public Canvas Ghost;
            public Canvas Guides;
            public Canvas WordsLayer;
            public Canvas ParticlesLayer;
            public Canvas Fireworks;
            public ScaleTransform EntryScale;
            public BlurEffect Blur;
            public TranslateTransform DecorDrift;
            public readonly List<WordVisual> Words = new List<WordVisual>();
            public readonly List<Particle> Particles = new List<Particle>();
            public readonly List<FireworkBurst> Bursts = new List<FireworkBurst>();
            public double NextFirework = -1;
            public int BurstCount;
        }

        sealed class WordVisual
        {
            public MusicStageWord Word;
            public Canvas Root;
            public TextBlock Main;
            public TextBlock Halo;
            public TextBlock Cyan;
            public TextBlock Red;
            public Path Outline;
            public Border Highlight;
            public ScaleTransform Scale;
            public RotateTransform Rotate;
            public TranslateTransform Move;
            public double Focus = 0.5;
        }

        sealed class Particle
        {
            public Ellipse Node;
            public double X;
            public double Y;
            public double Amplitude;
            public double Speed;
            public double Phase;
            public double Alpha;
        }

        sealed class FireworkBurst
        {
            public double Born;
            public double Life;
            public double X;
            public double Y;
            public double Size;
            public Line Trail;
            public readonly List<FireworkParticle> Particles = new List<FireworkParticle>();
        }

        sealed class FireworkParticle
        {
            public Ellipse Node;
            public double Angle;
            public double Speed;
            public double Radius;
        }
    }
}


