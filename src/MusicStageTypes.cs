using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 桌面投射用的轻量数据模型。
    //
    // 和音乐面板的 MusicLyricLine 分开：面板那套只关心"一行 + 时间"，这里要
    // 按 Folia Sonnet 的做法把一行再拆成词/字块，并带上逐词的进入时间。
    // 独立出来还方便用 test\stage-demo 单独编译预览，不用启动整个胶囊。
    // ---------------------------------------------------------------------
    internal sealed class MusicStageLyric
    {
        public TimeSpan Time;
        public string Text;
    }

    internal sealed class MusicStageSnapshot
    {
        public string SongMid = "";
        public string Title = "";
        public string Artist = "";
        public string AlbumMid = "";
        public string Source = "";
        public TimeSpan Position;
        public TimeSpan Duration;
        public bool Playing;
        public bool Resolving;
        public string PlayMode = "shuffle";
        public int LyricsVersion;
        public List<MusicStageLyric> Lyrics = new List<MusicStageLyric>();
    }

    // 一个排版单元（一个词 / 一个中文两字块 / 一段装饰大字）
    internal sealed class MusicStageWord
    {
        public int Index;
        public int LineIndex;
        public string Text = "";
        public bool IsHero;
        public bool IsSemiHero;
        public bool IsVertical;
        public bool IsDecoration;
        public bool IsHighlight;
        public bool HasChroma;
        public bool IsBold;
        public double FontScale = 1.0;
        public double FontSize = 40.0;
        public double X;
        public double Y;
        public double Rotation;          // 角度，绕自身中心
        public double EntryRotation;
        public double EnterX;
        public double EnterY;
        public double Start;             // 秒（相对歌曲）
        public double End;
        public double Settle;
        public double LineStart;
        public double LineEnd;
        public double Width;
        public double Height;
        public double Depth;             // 视差深度，0 = 主层
        public double RuntimeFocus = 0.55; // 运行时平滑后的"当前句"权重
        public double Guide;             // 0 = 不加辅助线，1/2 = 不同样式
    }

    internal sealed class MusicStageShot
    {
        public int Index;
        public int FirstLineIndex;
        public double Start;
        public double End;
        public string Kind = "fragment-collage";
        public int Seed;
        public double BaseFont = 56;
        public double CameraX;
        public double CameraY;
        public double CameraZoom = 1.08;
        public double CameraRotation;
        public readonly List<MusicStageWord> Words = new List<MusicStageWord>();
    }

    // 简单确定性随机（xorshift32）。同一条歌每次打开得到同一套版式，
    // 不会因为重播就换一个样子。
    internal sealed class MusicStageRng
    {
        uint state;

        public MusicStageRng(uint seed)
        {
            state = seed == 0 ? 0x9E3779B9u : seed;
            for (int i = 0; i < 4; i++) Next();
        }

        public double Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216.0;
        }

        public double Range(double min, double max)
        {
            return min + (max - min) * Next();
        }

        public bool Chance(double p)
        {
            return Next() < p;
        }

        public int Pick(int count)
        {
            if (count <= 0) return 0;
            int value = (int)(Next() * count);
            return value < 0 ? 0 : (value >= count ? count - 1 : value);
        }
    }

    internal static class MusicStageUtil
    {
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        public static double EaseOutExpo(double t)
        {
            t = Clamp(t, 0, 1);
            if (t >= 1) return 1;
            return 1 - Math.Pow(2, -10 * t);
        }

        public static double EaseOutCubic(double t)
        {
            t = Clamp(t, 0, 1);
            double u = 1 - t;
            return 1 - u * u * u;
        }

        public static double EaseInOutSine(double t)
        {
            t = Clamp(t, 0, 1);
            return 0.5 - 0.5 * Math.Cos(Math.PI * t);
        }

        // FNV-1a：给每条歌 / 每个段落一个稳定的种子
        public static uint Hash(string text)
        {
            uint hash = 2166136261u;
            if (text == null) text = "";
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }

        public static double Hash01(string text, int salt)
        {
            uint h = Hash(text + "|" + salt.ToString(CultureInfo.InvariantCulture));
            return (h & 0x00FFFFFFu) / 16777216.0;
        }

        public static Color Alpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Lift(Color color, double amount)
        {
            return Mix(color, Colors.White, amount);
        }

        public static bool IsCjk(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x3040 && c <= 0x30FF)
                || (c >= 0xAC00 && c <= 0xD7AF)
                || (c >= 0xF900 && c <= 0xFAFF);
        }

        public static bool HasCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsCjk(text[i])) return true;
            }
            return false;
        }

        static bool IsCjkPunctuation(char c)
        {
            return "，。！？、；：…—～（）【】《》「」『』〈〉·".IndexOf(c) >= 0;
        }

        static bool IsLatinPunctuation(char c)
        {
            return ",.!?;:'\"()[]{}<>/\\-—_…&".IndexOf(c) >= 0;
        }

        // 把一行拆成排版单元：
        //   · 英文按空格拆词，标点跟着词走（Atlanta, / by: / Technology®）
        //   · 中文按 2 字一块（奇数尾巴并进上一块），标点并进前一块
        //   · "♪" 或纯符号行单独保留
        public static List<string> SplitWords(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            text = text.Replace("\r", "").Replace("\n", " ").Trim();
            if (text.Length == 0) return result;

            if (text.Trim() == "♪" || text.Trim() == "♫")
            {
                result.Add(text.Trim());
                return result;
            }

            var latin = new StringBuilder();
            var cjk = new StringBuilder();

            Action flushLatin = delegate
            {
                string token = latin.ToString().Trim();
                latin.Length = 0;
                if (token.Length == 0) return;
                if (result.Count > 0 && token.Length == 1 && IsLatinPunctuation(token[0]))
                {
                    result[result.Count - 1] = result[result.Count - 1] + token;
                    return;
                }
                result.Add(token);
            };

            Action flushCjk = delegate
            {
                string run = cjk.ToString();
                cjk.Length = 0;
                if (run.Length == 0) return;
                // 中文不按两个字硬切：先按标点分成短句，整句作为一个排版单元，
                // 这样一句话能保持可读；太长（>10 字）才以 6 字为一组拆开。
                var clauses = new List<string>();
                var sb = new StringBuilder();
                for (int i = 0; i < run.Length; i++)
                {
                    char ch = run[i];
                    sb.Append(ch);
                    if (IsCjkPunctuation(ch))
                    {
                        clauses.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
                if (sb.Length > 0) clauses.Add(sb.ToString());
                for (int c = 0; c < clauses.Count; c++)
                {
                    string clause = clauses[c];
                    if (clause.Trim().Length == 0) continue;
                    if (clause.Length <= 10)
                    {
                        result.Add(clause);
                    }
                    else
                    {
                        for (int i = 0; i < clause.Length; i += 6)
                        {
                            result.Add(clause.Substring(i, Math.Min(6, clause.Length - i)));
                        }
                    }
                }
            };

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    flushLatin();
                    flushCjk();
                }
                else if (IsCjk(c) || IsCjkPunctuation(c))
                {
                    flushLatin();
                    cjk.Append(c);
                }
                else
                {
                    flushCjk();
                    latin.Append(c);
                }
            }
            flushLatin();
            flushCjk();

            // 纯标点块丢掉
            for (int i = result.Count - 1; i >= 0; i--)
            {
                bool onlyPunctuation = true;
                string token = result[i];
                for (int j = 0; j < token.Length; j++)
                {
                    if (!IsLatinPunctuation(token[j]) && !IsCjkPunctuation(token[j]) && token[j] != ' ')
                    {
                        onlyPunctuation = false;
                        break;
                    }
                }
                if (onlyPunctuation) result.RemoveAt(i);
            }
            return result;
        }

        public static string FormatTime(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            return ((int)value.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" +
                   value.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool IsCreditLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string t = text.Trim();
            if (t.Length == 0) return true;
            if (t.IndexOf("纯音乐", StringComparison.Ordinal) >= 0 && t.IndexOf("欣赏", StringComparison.Ordinal) >= 0) return true;
            if (t.IndexOf("此歌曲为没有填词", StringComparison.Ordinal) >= 0) return true;
            // 明显的制作名单：只在有冒号且整体不长时过滤，避免误伤正常歌词
            if (t.Length > 42) return false;
            if (t.IndexOf(':') < 0 && t.IndexOf('：') < 0) return false;
            string lower = t.ToLowerInvariant();
            string[] heads = new string[]
            {
                "作词", "作曲", "编曲", "制作", "混音", "母带", "录音", "和声", "监制", "出品", "词", "曲",
                "lyrics", "lyricist", "composer", "arranger", "producer", "mixing", "mastering", "recording", "written", "composed"
            };
            for (int i = 0; i < heads.Length; i++)
            {
                if (lower.StartsWith(heads[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static string JoinChars(string text)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n') continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // WPF 文本测量。设计坐标就是 DIP，所以 pixelsPerDip 用 1.0。
    internal static class MusicStageText
    {
        static readonly FontFamily LatinSerif = new FontFamily("Times New Roman");
        static readonly FontFamily LatinSans = new FontFamily("Segoe UI");
        static readonly FontFamily LatinDisplay = new FontFamily("Arial Black");
        static readonly FontFamily LatinHand = new FontFamily("Segoe Script");
        static readonly FontFamily LatinMono = new FontFamily("Consolas");
        static readonly FontFamily CjkSerif = new FontFamily("SimSun");
        static readonly FontFamily CjkSans = new FontFamily("Microsoft YaHei UI");
        static readonly FontFamily CjkHand = new FontFamily("KaiTi");
        static readonly FontFamily Ui = new FontFamily("Microsoft YaHei UI");
        static string fontKind = "serif";

        public static void SetFontKind(string kind)
        {
            fontKind = string.IsNullOrEmpty(kind) ? "serif" : kind;
        }

        public static FontFamily FamilyFor(string text)
        {
            bool cjk = MusicStageUtil.HasCjk(text);
            if (cjk)
            {
                if (fontKind == "sans" || fontKind == "display") return CjkSans;
                if (fontKind == "hand") return CjkHand;
                if (fontKind == "mono") return CjkSerif;
                return CjkSerif;
            }
            if (fontKind == "sans") return LatinSans;
            if (fontKind == "display") return LatinDisplay;
            if (fontKind == "hand") return LatinHand;
            if (fontKind == "mono") return LatinMono;
            return LatinSerif;
        }

        public static FontFamily UiFamily
        {
            get { return Ui; }
        }

        public static Typeface TypefaceFor(string text, bool bold)
        {
            return new Typeface(FamilyFor(text), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        }

        public static Size Measure(string text, double fontSize, bool bold, bool vertical)
        {
            string content = vertical ? MusicStageUtil.JoinChars(text) : text;
            var typeface = TypefaceFor(text, bold);
            var ft = new FormattedText(content, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, Math.Max(1, fontSize), Brushes.White, 1.0);
            if (vertical)
            {
                ft.LineHeight = Math.Max(1, fontSize * 0.86);
                ft.TextAlignment = TextAlignment.Center;
            }
            double width = Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 2;
            double height = Math.Ceiling(ft.Height) + 2;
            if (vertical) width = Math.Max(width, fontSize * 0.7);
            return new Size(width, height);
        }

        public static Geometry BuildGeometry(string text, double fontSize, bool bold)
        {
            var typeface = TypefaceFor(text, bold);
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, Math.Max(1, fontSize), Brushes.White, 1.0);
            return ft.BuildGeometry(new Point(0, 0));
        }
    }
}

