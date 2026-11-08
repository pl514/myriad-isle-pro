using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // Folia 0.7.27 的「歌词样式」列表（18 个可视化）。
    //
    // 这里做的是样式参数表和本地 WPF 重画，不是把 Folia 的 18 个
    // Pixi/WebGL/Canvas 引擎逐文件拷进来。Folia 是 AGPL-3.0：允许搬运和修改，
    // 但分发衍生作品时要按 AGPL 提供源码；本项目仍是 MIT，所以样式按视觉语言
    // 重新实现，字体/贴图/缩略图也全部由本地代码生成。
    // ---------------------------------------------------------------------
    internal sealed class MusicStageStyle
    {
        public string Id;
        public string Name;
        public string Description;
        public Color BgTop;
        public Color BgBottom;
        public Color Ink;
        public Color Dim;
        public Color Accent;
        public Color Secondary;
        public string FontKind;          // serif / sans / display / hand / mono
        public string BackgroundKind;    // 见 MusicDesktopStageWindow.BuildBackdrop
        public string[] LayoutKinds;     // 这个样式优先使用的 shot 版式
        public bool IsLight;
        public double DefaultDecor = 1.0;
    }

    internal sealed class MusicStageSettings
    {
        public string StyleId = "sonnet";
        public bool BeatBloom = true;
        public bool BeatPush = true;
        public double SparkCount = 1.0;
        public double SparkGlow = 1.0;
        public double ChorusGlow = 1.30;
        public double OverallGlow = 0.80;
        public bool Trail = true;
        public double CameraIntensity = 1.0;
        public double MotionIntensity = 1.0;
        public bool Transitions = true;
        public double BackgroundDim = 0.0;
        public double DecorDensity = 1.0;
        public double FontScale = 1.0;
        public bool CoverTint = true;
        public bool ShowTranslation = true;
        public bool ReduceMotion = false;
        public bool RandomPerSong = false;
        public string ThemeName = "默认";

        public MusicStageSettings Clone()
        {
            var copy = new MusicStageSettings();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(MusicStageSettings other)
        {
            if (other == null) return;
            StyleId = other.StyleId;
            BeatBloom = other.BeatBloom;
            BeatPush = other.BeatPush;
            SparkCount = other.SparkCount;
            SparkGlow = other.SparkGlow;
            ChorusGlow = other.ChorusGlow;
            OverallGlow = other.OverallGlow;
            Trail = other.Trail;
            CameraIntensity = other.CameraIntensity;
            MotionIntensity = other.MotionIntensity;
            Transitions = other.Transitions;
            BackgroundDim = other.BackgroundDim;
            DecorDensity = other.DecorDensity;
            FontScale = other.FontScale;
            CoverTint = other.CoverTint;
            ShowTranslation = other.ShowTranslation;
            ReduceMotion = other.ReduceMotion;
            RandomPerSong = other.RandomPerSong;
            ThemeName = other.ThemeName;
        }

        public static MusicStageSettings Load()
        {
            var settings = new MusicStageSettings();
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path)) return settings;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "style": settings.StyleId = value; break;
                        case "beatBloom": settings.BeatBloom = Bool(value, settings.BeatBloom); break;
                        case "beatPush": settings.BeatPush = Bool(value, settings.BeatPush); break;
                        case "sparkCount": settings.SparkCount = Num(value, settings.SparkCount); break;
                        case "sparkGlow": settings.SparkGlow = Num(value, settings.SparkGlow); break;
                        case "chorusGlow": settings.ChorusGlow = Num(value, settings.ChorusGlow); break;
                        case "overallGlow": settings.OverallGlow = Num(value, settings.OverallGlow); break;
                        case "trail": settings.Trail = Bool(value, settings.Trail); break;
                        case "camera": settings.CameraIntensity = Num(value, settings.CameraIntensity); break;
                        case "motion": settings.MotionIntensity = Num(value, settings.MotionIntensity); break;
                        case "transitions": settings.Transitions = Bool(value, settings.Transitions); break;
                        case "backgroundDim": settings.BackgroundDim = Num(value, settings.BackgroundDim); break;
                        case "decor": settings.DecorDensity = Num(value, settings.DecorDensity); break;
                        case "fontScale": settings.FontScale = Num(value, settings.FontScale); break;
                        case "coverTint": settings.CoverTint = Bool(value, settings.CoverTint); break;
                        case "translation": settings.ShowTranslation = Bool(value, settings.ShowTranslation); break;
                        case "reduceMotion": settings.ReduceMotion = Bool(value, settings.ReduceMotion); break;
                        case "randomPerSong": settings.RandomPerSong = Bool(value, settings.RandomPerSong); break;
                        case "theme": settings.ThemeName = value; break;
                    }
                }
            }
            catch (Exception) { }
            if (MusicStageStyles.Find(settings.StyleId) == null) settings.StyleId = "sonnet";
            return settings;
        }

        public void Save()
        {
            try
            {
                string path = SettingsPath;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var lines = new List<string>();
                lines.Add("# 万象岛 Pro 投射歌词样式（一行一个 key=value）");
                lines.Add("style=" + StyleId);
                lines.Add("beatBloom=" + (BeatBloom ? "1" : "0"));
                lines.Add("beatPush=" + (BeatPush ? "1" : "0"));
                lines.Add("sparkCount=" + SparkCount.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("sparkGlow=" + SparkGlow.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("chorusGlow=" + ChorusGlow.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("overallGlow=" + OverallGlow.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("trail=" + (Trail ? "1" : "0"));
                lines.Add("camera=" + CameraIntensity.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("motion=" + MotionIntensity.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("transitions=" + (Transitions ? "1" : "0"));
                lines.Add("backgroundDim=" + BackgroundDim.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("decor=" + DecorDensity.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("fontScale=" + FontScale.ToString("0.00", CultureInfo.InvariantCulture));
                lines.Add("coverTint=" + (CoverTint ? "1" : "0"));
                lines.Add("translation=" + (ShowTranslation ? "1" : "0"));
                lines.Add("reduceMotion=" + (ReduceMotion ? "1" : "0"));
                lines.Add("randomPerSong=" + (RandomPerSong ? "1" : "0"));
                lines.Add("theme=" + ThemeName);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception) { }
        }

        static string SettingsPath
        {
            get
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "music-stage-settings.txt");
            }
        }

        static bool Bool(string value, bool fallback)
        {
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        static double Num(string value, double fallback)
        {
            double parsed;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }
    }

    internal static class MusicStageStyles
    {
        static readonly MusicStageStyle[] all = new MusicStageStyle[]
        {
            new MusicStageStyle
            {
                Id = "sonnet", Name = "商籁", Description = "排版拼贴 / 衬线大字",
                BgTop = Color.FromRgb(18, 19, 24), BgBottom = Color.FromRgb(5, 5, 8),
                Ink = Color.FromRgb(244, 244, 245), Dim = Color.FromRgb(132, 135, 145),
                Accent = Color.FromRgb(198, 200, 208), Secondary = Color.FromRgb(113, 113, 122),
                FontKind = "serif", BackgroundKind = "sonnet",
                LayoutKinds = new string[] { "fragment-collage", "type-impact", "editorial-column", "poster-blocks", "tracking-ribbon", "mask-reveal", "quiet-tableau" }
            },
            new MusicStageStyle
            {
                Id = "tempera", Name = "凝彩", Description = "纸片色块 / 拼贴海报",
                BgTop = Color.FromRgb(39, 29, 23), BgBottom = Color.FromRgb(14, 11, 10),
                Ink = Color.FromRgb(249, 236, 207), Dim = Color.FromRgb(183, 150, 116),
                Accent = Color.FromRgb(232, 93, 59), Secondary = Color.FromRgb(93, 168, 196),
                FontKind = "display", BackgroundKind = "tempera",
                LayoutKinds = new string[] { "poster-blocks", "editorial-column", "type-impact", "fragment-collage" }
            },
            new MusicStageStyle
            {
                Id = "lumiere", Name = "绘光", Description = "舞台光束 / 暖色泛光",
                BgTop = Color.FromRgb(12, 9, 8), BgBottom = Color.FromRgb(3, 2, 3),
                Ink = Color.FromRgb(255, 244, 229), Dim = Color.FromRgb(174, 151, 125),
                Accent = Color.FromRgb(255, 177, 84), Secondary = Color.FromRgb(230, 120, 80),
                FontKind = "sans", BackgroundKind = "lumiere",
                LayoutKinds = new string[] { "type-impact", "mask-reveal", "tracking-ribbon", "editorial-column" }
            },
            new MusicStageStyle
            {
                Id = "hanabi", Name = "花火", Description = "夜空烟花 / 彩纸歌词",
                BgTop = Color.FromRgb(10, 12, 30), BgBottom = Color.FromRgb(4, 4, 12),
                Ink = Color.FromRgb(255, 246, 225), Dim = Color.FromRgb(160, 170, 200),
                Accent = Color.FromRgb(255, 72, 120), Secondary = Color.FromRgb(76, 201, 240),
                FontKind = "display", BackgroundKind = "hanabi",
                LayoutKinds = new string[] { "type-impact", "fragment-collage", "tracking-ribbon", "poster-blocks" }
            },
            new MusicStageStyle
            {
                Id = "machi", Name = "町景", Description = "霓虹街景 / 招牌字",
                BgTop = Color.FromRgb(6, 10, 22), BgBottom = Color.FromRgb(2, 3, 8),
                Ink = Color.FromRgb(230, 240, 255), Dim = Color.FromRgb(120, 145, 170),
                Accent = Color.FromRgb(255, 46, 136), Secondary = Color.FromRgb(0, 229, 255),
                FontKind = "sans", BackgroundKind = "machi",
                LayoutKinds = new string[] { "poster-blocks", "type-impact" }
            },
            new MusicStageStyle
            {
                Id = "nebbia", Name = "雾笺", Description = "起雾车窗 / 雨滴",
                BgTop = Color.FromRgb(78, 89, 100), BgBottom = Color.FromRgb(32, 38, 44),
                Ink = Color.FromRgb(240, 246, 250), Dim = Color.FromRgb(166, 181, 194),
                Accent = Color.FromRgb(180, 210, 228), Secondary = Color.FromRgb(126, 156, 176),
                FontKind = "sans", BackgroundKind = "nebbia",
                LayoutKinds = new string[] { "quiet-tableau", "editorial-column", "type-impact" }
            },
            new MusicStageStyle
            {
                Id = "classic", Name = "流光", Description = "流光轨迹 / 渐变字",
                BgTop = Color.FromRgb(5, 6, 10), BgBottom = Color.FromRgb(2, 2, 5),
                Ink = Color.FromRgb(245, 247, 255), Dim = Color.FromRgb(140, 150, 180),
                Accent = Color.FromRgb(125, 211, 252), Secondary = Color.FromRgb(192, 132, 252),
                FontKind = "sans", BackgroundKind = "classic",
                LayoutKinds = new string[] { "tracking-ribbon", "type-impact", "mask-reveal" }
            },
            new MusicStageStyle
            {
                Id = "cadenze", Name = "心象", Description = "墨色心象 / 朱印",
                BgTop = Color.FromRgb(19, 15, 13), BgBottom = Color.FromRgb(6, 5, 5),
                Ink = Color.FromRgb(241, 233, 220), Dim = Color.FromRgb(150, 135, 120),
                Accent = Color.FromRgb(178, 58, 46), Secondary = Color.FromRgb(110, 90, 75),
                FontKind = "serif", BackgroundKind = "cadenze",
                LayoutKinds = new string[] { "fragment-collage", "mask-reveal", "editorial-column" }
            },
            new MusicStageStyle
            {
                Id = "partita", Name = "云阶", Description = "云层台阶 / 柔光",
                BgTop = Color.FromRgb(208, 219, 231), BgBottom = Color.FromRgb(147, 163, 184),
                Ink = Color.FromRgb(28, 36, 48), Dim = Color.FromRgb(90, 105, 120),
                Accent = Color.FromRgb(80, 120, 170), Secondary = Color.FromRgb(200, 170, 120),
                FontKind = "serif", BackgroundKind = "partita", IsLight = true,
                LayoutKinds = new string[] { "tracking-ribbon", "editorial-column", "quiet-tableau" }
            },
            new MusicStageStyle
            {
                Id = "fume", Name = "浮名", Description = "浮动的名字 / 细字",
                BgTop = Color.FromRgb(10, 10, 12), BgBottom = Color.FromRgb(4, 4, 5),
                Ink = Color.FromRgb(232, 232, 238), Dim = Color.FromRgb(130, 132, 142),
                Accent = Color.FromRgb(154, 160, 178), Secondary = Color.FromRgb(90, 92, 105),
                FontKind = "serif", BackgroundKind = "fume",
                LayoutKinds = new string[] { "editorial-column", "quiet-tableau", "tracking-ribbon" }
            },
            new MusicStageStyle
            {
                Id = "tilt", Name = "倾述", Description = "信纸手写 / 倾斜",
                BgTop = Color.FromRgb(25, 20, 15), BgBottom = Color.FromRgb(9, 7, 5),
                Ink = Color.FromRgb(244, 231, 211), Dim = Color.FromRgb(170, 145, 120),
                Accent = Color.FromRgb(217, 121, 65), Secondary = Color.FromRgb(180, 160, 130),
                FontKind = "hand", BackgroundKind = "tilt",
                LayoutKinds = new string[] { "editorial-column", "tracking-ribbon", "quiet-tableau" }
            },
            new MusicStageStyle
            {
                Id = "ponder", Name = "思索", Description = "极简暗色 / 等宽",
                BgTop = Color.FromRgb(11, 12, 16), BgBottom = Color.FromRgb(4, 5, 7),
                Ink = Color.FromRgb(216, 219, 228), Dim = Color.FromRgb(120, 125, 140),
                Accent = Color.FromRgb(107, 114, 128), Secondary = Color.FromRgb(80, 85, 100),
                FontKind = "mono", BackgroundKind = "ponder",
                LayoutKinds = new string[] { "quiet-tableau", "editorial-column", "type-impact" }
            },
            new MusicStageStyle
            {
                Id = "still", Name = "清", Description = "留白 / 浅色纸",
                BgTop = Color.FromRgb(244, 243, 238), BgBottom = Color.FromRgb(210, 208, 200),
                Ink = Color.FromRgb(23, 23, 23), Dim = Color.FromRgb(100, 100, 100),
                Accent = Color.FromRgb(75, 85, 95), Secondary = Color.FromRgb(160, 160, 155),
                FontKind = "serif", BackgroundKind = "still", IsLight = true,
                LayoutKinds = new string[] { "quiet-tableau", "type-impact", "editorial-column" }
            },
            new MusicStageStyle
            {
                Id = "pendolo", Name = "时计", Description = "钟表齿轮 / 黄铜",
                BgTop = Color.FromRgb(13, 10, 7), BgBottom = Color.FromRgb(4, 3, 2),
                Ink = Color.FromRgb(247, 231, 176), Dim = Color.FromRgb(170, 150, 110),
                Accent = Color.FromRgb(201, 162, 39), Secondary = Color.FromRgb(130, 100, 60),
                FontKind = "mono", BackgroundKind = "pendolo",
                LayoutKinds = new string[] { "quiet-tableau", "editorial-column", "type-impact" }
            },
            new MusicStageStyle
            {
                Id = "cappella", Name = "群唱", Description = "多重人声 / 叠句",
                BgTop = Color.FromRgb(12, 10, 16), BgBottom = Color.FromRgb(4, 4, 8),
                Ink = Color.FromRgb(255, 255, 255), Dim = Color.FromRgb(160, 160, 180),
                Accent = Color.FromRgb(255, 120, 180), Secondary = Color.FromRgb(120, 200, 255),
                FontKind = "sans", BackgroundKind = "cappella",
                LayoutKinds = new string[] { "type-impact", "poster-blocks", "editorial-column" }
            },
            new MusicStageStyle
            {
                Id = "claddagh", Name = "回环", Description = "环形轨迹 / 青绿",
                BgTop = Color.FromRgb(8, 19, 17), BgBottom = Color.FromRgb(2, 6, 5),
                Ink = Color.FromRgb(234, 255, 247), Dim = Color.FromRgb(130, 170, 160),
                Accent = Color.FromRgb(52, 211, 153), Secondary = Color.FromRgb(94, 234, 212),
                FontKind = "sans", BackgroundKind = "claddagh",
                LayoutKinds = new string[] { "tracking-ribbon", "mask-reveal", "type-impact" }
            },
            new MusicStageStyle
            {
                Id = "monet", Name = "莫奈", Description = "印象派水色 / 柔焦",
                BgTop = Color.FromRgb(134, 163, 181), BgBottom = Color.FromRgb(64, 89, 108),
                Ink = Color.FromRgb(247, 251, 255), Dim = Color.FromRgb(188, 208, 222),
                Accent = Color.FromRgb(166, 205, 220), Secondary = Color.FromRgb(126, 176, 154),
                FontKind = "serif", BackgroundKind = "monet",
                LayoutKinds = new string[] { "editorial-column", "type-impact", "fragment-collage" }
            },
            new MusicStageStyle
            {
                Id = "diorama", Name = "镜台", Description = "镜面舞台 / 对称",
                BgTop = Color.FromRgb(11, 12, 16), BgBottom = Color.FromRgb(3, 4, 7),
                Ink = Color.FromRgb(240, 240, 245), Dim = Color.FromRgb(140, 145, 155),
                Accent = Color.FromRgb(184, 161, 255), Secondary = Color.FromRgb(120, 140, 180),
                FontKind = "serif", BackgroundKind = "diorama",
                LayoutKinds = new string[] { "poster-blocks", "editorial-column", "type-impact" }
            }
        };

        public static IList<MusicStageStyle> All
        {
            get { return all; }
        }

        public static MusicStageStyle Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i].Id, id, StringComparison.Ordinal)) return all[i];
                }
            }
            return all[0];
        }
    }
}
