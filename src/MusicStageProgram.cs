using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // Folia「Sonnet」式排版程序的本地重写。
    //
    // 原版是 Pixi + WebGL 的整条时间线：把歌词按段落切成 shot，每个 shot 选一种
    // 版式（fragment-collage / type-impact / editorial-column / tracking-ribbon /
    // mask-reveal / poster-blocks / quiet-tableau），再按时间给每个词/字做进入动画。
    //
    // 这里保留的是"阅读结构"而不是原版代码：
    //   · 同一段落内所有词先排好版，唱到哪个词才让它出现；
    //   · 主词（hero）最大，长词/半主词加描边或竖排，再复制 1~2 个超大作背景装饰；
    //   · 每个 shot 有自己的镜头（轻微推拉/平移/旋转）；
    //   · 词位置用确定性随机 + 碰撞检测，同一首歌每次打开一致。
    // 没有逐词 LRC 时，按字符数把这一行的时长分给各个词——时间轴仍是播放时间。
    // ---------------------------------------------------------------------
    internal static class MusicStageProgram
    {
        static MusicStageStyle activeStyle = MusicStageStyles.Find("sonnet");
        static MusicStageSettings activeSettings = new MusicStageSettings();

        static readonly string[] Kinds = new string[]
        {
            "fragment-collage", "type-impact", "editorial-column", "poster-blocks",
            "tracking-ribbon", "mask-reveal", "quiet-tableau"
        };

        public static List<MusicStageShot> Build(MusicStageSnapshot snapshot, double width, double height, MusicStageSettings settings)
        {
            activeSettings = settings == null ? new MusicStageSettings() : settings;
            activeStyle = MusicStageStyles.Find(activeSettings.StyleId);
            MusicStageText.SetFontKind(activeStyle.FontKind);
            var shots = new List<MusicStageShot>();
            if (snapshot == null) return shots;

            string seed = (snapshot.SongMid == null ? "" : snapshot.SongMid) + "|" +
                          (snapshot.Title == null ? "" : snapshot.Title);
            if (seed.Length < 3) seed = "capsule-music";

            var lines = new List<MusicStageLyric>();
            if (snapshot.Lyrics != null)
            {
                for (int i = 0; i < snapshot.Lyrics.Count; i++)
                {
                    MusicStageLyric line = snapshot.Lyrics[i];
                    if (line == null) continue;
                    string text = (line.Text == null ? "" : line.Text).Trim();
                    if (text.Length == 0) continue;
                    if (MusicStageUtil.IsCreditLine(text)) continue;
                    lines.Add(new MusicStageLyric { Time = line.Time, Text = text });
                }
            }
            lines.Sort(delegate(MusicStageLyric a, MusicStageLyric b) { return a.Time.CompareTo(b.Time); });

            double duration = snapshot.Duration.TotalSeconds;
            if (duration <= 0) duration = lines.Count > 0 ? lines[lines.Count - 1].Time.TotalSeconds + 8 : 8;

            if (lines.Count == 0)
            {
                BuildIdle(shots, snapshot, width, height, seed);
                return shots;
            }

            // 前奏太长时先放一张"歌名/歌手"的安静画面，避免前 10 秒全黑
            double first = lines[0].Time.TotalSeconds;
            if (first > 4.5)
            {
                var intro = new MusicStageShot
                {
                    Index = 0,
                    Start = 0,
                    End = first,
                    Kind = "quiet-tableau",
                    Seed = (int)(MusicStageUtil.Hash(seed + "|intro") & 0x7fffffff),
                    BaseFont = BaseFontFor(width, 4, "quiet-tableau")
                };
                AddIdleWords(intro, snapshot, width, height, true);
                ApplyCamera(intro, seed, 0);
                shots.Add(intro);
            }

            int cursor = 0;
            int shotIndex = 0;
            while (cursor < lines.Count)
            {
                int groupStart = cursor;
                double startTime = lines[cursor].Time.TotalSeconds;
                bool firstCjk = MusicStageUtil.HasCjk(lines[cursor].Text);
                double prevEnd = LineEnd(lines, cursor, duration);
                cursor++;
                while (cursor < lines.Count && cursor - groupStart < 5)
                {
                    if (MusicStageUtil.HasCjk(lines[cursor].Text) != firstCjk) break;
                    double gap = lines[cursor].Time.TotalSeconds - prevEnd;
                    double span = LineEnd(lines, cursor, duration) - startTime;
                    if (gap > 3.4 || span > 15.5) break;
                    prevEnd = LineEnd(lines, cursor, duration);
                    cursor++;
                }

                double endTime;
                if (cursor < lines.Count)
                {
                    endTime = lines[cursor].Time.TotalSeconds;
                }
                else
                {
                    endTime = Math.Max(prevEnd + 1.2, Math.Min(duration, prevEnd + 6.0));
                }
                if (endTime <= startTime + 0.5) endTime = startTime + 1.5;

                var shot = new MusicStageShot
                {
                    Index = shots.Count,
                    FirstLineIndex = groupStart,
                    Start = startTime,
                    End = endTime,
                    Seed = (int)(MusicStageUtil.Hash(seed + "|shot|" + shotIndex.ToString(CultureInfo.InvariantCulture)) & 0x7fffffff)
                };
                var rng = new MusicStageRng((uint)shot.Seed);
                shot.Kind = ChooseKind(seed, shotIndex, lines, groupStart, cursor, rng);
                shot.BaseFont = BaseFontFor(width, WordCount(lines, groupStart, cursor), shot.Kind);

                BuildWords(shot, lines, groupStart, cursor, width, height, duration, seed, rng, shotIndex);
                ApplyCamera(shot, seed, shotIndex);
                shots.Add(shot);
                shotIndex++;
            }

            return shots;
        }

        static double LineEnd(List<MusicStageLyric> lines, int index, double duration)
        {
            double start = lines[index].Time.TotalSeconds;
            if (index + 1 < lines.Count)
            {
                double next = lines[index + 1].Time.TotalSeconds;
                if (next > start + 0.15) return Math.Min(next, start + 9.0);
            }
            return Math.Min(Math.Max(start + 3.8, start + 0.1), Math.Max(start + 3.8, duration));
        }

        static int WordCount(List<MusicStageLyric> lines, int start, int end)
        {
            int count = 0;
            for (int i = start; i < end; i++)
            {
                count += Math.Max(1, MusicStageUtil.SplitWords(lines[i].Text).Count);
            }
            return Math.Max(1, count);
        }

        static double BaseFontFor(double width, int wordCount, string kind)
        {
            double kindScale = kind == "type-impact" ? 1.55 : (kind == "quiet-tableau" ? 0.82 : 1.0);
            double scale = activeSettings == null ? 1.0 : activeSettings.FontScale;
            return MusicStageUtil.Clamp(width / Math.Max(7.0, wordCount * 2.15) * kindScale * 0.78 * scale, 22.0, 128.0);
        }

        static string ChooseKind(string seed, int index, List<MusicStageLyric> lines, int start, int end, MusicStageRng rng)
        {
            int words = WordCount(lines, start, end);
            bool cjk = false;
            for (int i = start; i < end; i++)
            {
                if (MusicStageUtil.HasCjk(lines[i].Text)) { cjk = true; break; }
            }
            if (cjk)
            {
                // 中文整句作一个词块：优先按句/块排版，不拆散成"碎片拼贴"
                int cr = (int)(MusicStageUtil.Hash01(seed + "|cjk|kind", index) * 100);
                if (words >= 4)
                {
                    if (cr < 40) return "poster-blocks";
                    if (cr < 75) return "type-impact";
                    if (cr < 90) return "tracking-ribbon";
                    return "quiet-tableau";
                }
                if (cr < 60) return "type-impact";
                return "quiet-tableau";
            }
            if (words <= 2) return rng.Chance(0.55) ? "quiet-tableau" : "type-impact";
            if (words <= 4 && rng.Chance(0.45)) return "quiet-tableau";

            // 每个样式有自己的版式偏好；点「歌词动画」切换样式后整个排版会跟着换。
            if (activeStyle != null && activeStyle.LayoutKinds != null && activeStyle.LayoutKinds.Length > 0)
            {
                string picked = activeStyle.LayoutKinds[(int)(MusicStageUtil.Hash01(seed + "|style-layout", index) * activeStyle.LayoutKinds.Length)];
                if (cjk && (picked == "fragment-collage" || picked == "mask-reveal"))
                {
                    picked = "type-impact";
                }
                if (words >= 6 && picked == "quiet-tableau") picked = "type-impact";
                return picked;
            }

            int roll = (int)(MusicStageUtil.Hash01(seed + "|kind", index) * 100);
            if (words >= 8)
            {
                if (roll < 34) return "fragment-collage";
                if (roll < 51) return "type-impact";
                if (roll < 66) return "editorial-column";
                if (roll < 78) return "poster-blocks";
                if (roll < 88) return "tracking-ribbon";
                if (roll < 96) return "mask-reveal";
                return "quiet-tableau";
            }
            if (roll < 24) return "type-impact";
            if (roll < 42) return "editorial-column";
            if (roll < 60) return "tracking-ribbon";
            if (roll < 76) return "fragment-collage";
            if (roll < 88) return "poster-blocks";
            if (roll < 96) return "mask-reveal";
            return "quiet-tableau";
        }

        static void ApplyCamera(MusicStageShot shot, string seed, int index)
        {
            shot.CameraX = (MusicStageUtil.Hash01(seed + "|camx", index) - 0.5) * 0.08;
            shot.CameraY = (MusicStageUtil.Hash01(seed + "|camy", index) - 0.5) * 0.055;
            shot.CameraZoom = 1.015 + MusicStageUtil.Hash01(seed + "|camz", index) * 0.06;
            shot.CameraRotation = (MusicStageUtil.Hash01(seed + "|camr", index) - 0.5) * 1.7;
        }

        static void BuildIdle(List<MusicStageShot> shots, MusicStageSnapshot snapshot, double width, double height, string seed)
        {
            var shot = new MusicStageShot
            {
                Index = 0,
                Start = 0,
                End = double.MaxValue,
                Kind = "quiet-tableau",
                Seed = (int)(MusicStageUtil.Hash(seed + "|idle") & 0x7fffffff),
                BaseFont = BaseFontFor(width, 5, "quiet-tableau")
            };
            AddIdleWords(shot, snapshot, width, height, false);
            ApplyCamera(shot, seed, 0);
            shots.Add(shot);
        }

        static void AddIdleWords(MusicStageShot shot, MusicStageSnapshot snapshot, double width, double height, bool intro)
        {
            string title = snapshot == null || string.IsNullOrEmpty(snapshot.Title) ? "CAPSULE MUSIC" : snapshot.Title.Trim();
            string artist = snapshot == null || string.IsNullOrEmpty(snapshot.Artist) ? "" : snapshot.Artist.Trim();
            List<string> titleWords = MusicStageUtil.SplitWords(title);
            if (titleWords.Count == 0) titleWords.Add("CAPSULE MUSIC");

            var rng = new MusicStageRng((uint)shot.Seed);
            var words = new List<MusicStageWord>();
            for (int i = 0; i < titleWords.Count && i < 6; i++)
            {
                double titleSize = CapFontSize(titleWords[i], shot.BaseFont * (i == 0 ? 2.15 : 1.15), width * 0.9, height, false, i == 0);
                var w = NewWord(i, -1, titleWords[i], titleSize, height);
                w.IsHero = i == 0;
                w.IsBold = true;
                w.IsSemiHero = false;
                w.Start = -1;
                w.Settle = 0;
                w.End = double.MaxValue;
                w.X = width * 0.5 + (i - (titleWords.Count - 1) * 0.5) * (w.Width + 24);
                w.Y = height * 0.45;
                w.Guide = i == 0 ? 1 : 0;
                w.HasChroma = i == 0;
                words.Add(w);
            }
            if (!string.IsNullOrEmpty(artist))
            {
                double artistSize = CapFontSize(artist, shot.BaseFont * 0.62, width * 0.8, height, false, false);
                var a = NewWord(words.Count, -1, artist.ToUpperInvariant(), artistSize, height);
                a.Start = -1;
                a.Settle = 0;
                a.End = double.MaxValue;
                a.X = width * 0.5;
                a.Y = height * 0.45 + (words.Count > 0 ? words[0].Height * 0.5 + 70 : 70);
                words.Add(a);
            }
            if (!intro && (snapshot == null || snapshot.Lyrics == null || snapshot.Lyrics.Count == 0))
            {
                string hintText = snapshot != null && snapshot.Title.Length > 0 ? "暂无歌词" : "在胶囊音乐面板选一首歌";
                double hintSize = CapFontSize(hintText, shot.BaseFont * 0.42, width * 0.72, height, false, false);
                var hint = NewWord(words.Count, -1, hintText, hintSize, height);
                hint.Start = -1;
                hint.Settle = 0;
                hint.End = double.MaxValue;
                hint.X = width * 0.5;
                hint.Y = height * 0.72;
                words.Add(hint);
            }
            // 一行歌词里没有可用的版面时，至少给一个安静的主标题
            if (words.Count == 0)
            {
                var fallback = NewWord(0, -1, "♪", shot.BaseFont * 1.6, height);
                fallback.Start = 0;
                fallback.Settle = 0.8;
                fallback.End = double.MaxValue;
                fallback.X = width * 0.5;
                fallback.Y = height * 0.5;
                words.Add(fallback);
            }
            var ghosts = new List<MusicStageWord>();
            if (words.Count > 0) AddGhostDecorations(ghosts, shot, words[0], width, height, rng);
            shot.Words.AddRange(ghosts);
            shot.Words.AddRange(words);
        }

        static void BuildWords(MusicStageShot shot, List<MusicStageLyric> lines, int start, int end,
            double width, double height, double duration, string seed, MusicStageRng rng, int shotIndex)
        {
            var words = new List<MusicStageWord>();
            int lineIndex = 0;
            for (int li = start; li < end; li++)
            {
                MusicStageLyric line = lines[li];
                double lineStart = line.Time.TotalSeconds;
                double lineEnd = LineEnd(lines, li, duration);
                if (lineEnd < lineStart + 0.7) lineEnd = lineStart + 1.4;
                List<string> tokens = MusicStageUtil.SplitWords(line.Text);
                double totalChars = 0;
                for (int i = 0; i < tokens.Count; i++) totalChars += Math.Max(1, tokens[i].Length);
                if (totalChars <= 0) totalChars = 1;

                double offset = 0;
                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];
                    int chars = Math.Max(1, token.Length);
                    double at = lineStart + (lineEnd - lineStart) * (offset / totalChars);
                    double next = lineStart + (lineEnd - lineStart) * ((offset + chars) / totalChars);
                    if (next < at + 0.16) next = at + 0.16;
                    var word = new MusicStageWord
                    {
                        Index = words.Count,
                        LineIndex = lineIndex,
                        Text = token,
                        Start = at,
                        End = next,
                        LineStart = lineStart,
                        LineEnd = lineEnd,
                        Settle = Math.Min(next + 0.12, at + 0.52)
                    };
                    if (word.Settle < at + 0.14) word.Settle = at + 0.14;
                    words.Add(word);
                    offset += chars;
                }
                lineIndex++;
            }
            if (words.Count == 0)
            {
                var fallback = NewWord(0, 0, "♪", shot.BaseFont * 1.2, height);
                fallback.Start = shot.Start;
                fallback.Settle = shot.Start + 0.5;
                fallback.End = shot.End;
                words.Add(fallback);
            }

            // hero：长词 + 持续时间权重最高；半主词取离 hero 较远、足够长的词
            int hero = 0;
            double heroScore = -1;
            var scores = new double[words.Count];
            for (int i = 0; i < words.Count; i++)
            {
                int graphemes = words[i].Text.Length;
                double score = Math.Min(graphemes, 8) * 14 + Math.Min(2.5, Math.Max(0, words[i].End - words[i].Start)) * 18;
                scores[i] = score;
                if (score > heroScore)
                {
                    heroScore = score;
                    hero = i;
                }
            }
            words[hero].IsHero = true;

            var semis = new List<int>();
            if (words.Count >= 4)
            {
                var candidates = new List<int>();
                for (int i = 0; i < words.Count; i++)
                {
                    if (i == hero) continue;
                    if (Math.Abs(i - hero) < 2) continue;
                    if (words[i].Text.Length < 2) continue;
                    if (scores[i] < heroScore * 0.35) continue;
                    candidates.Add(i);
                }
                candidates.Sort(delegate(int a, int b) { return scores[b].CompareTo(scores[a]); });
                for (int i = 0; i < candidates.Count && semis.Count < 2; i++)
                {
                    int idx = candidates[i];
                    bool tooClose = false;
                    for (int j = 0; j < semis.Count; j++)
                    {
                        if (Math.Abs(semis[j] - idx) < 2) tooClose = true;
                    }
                    if (!tooClose) semis.Add(idx);
                }
            }

            // 基础字号 / 角色 / 竖排 / 高亮块
            int verticalCount = 0;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                bool semi = semis.Contains(i);
                w.IsSemiHero = semi;
                bool vertical = false;
                bool allowVertical = activeStyle != null
                    && (activeStyle.Id == "sonnet" || activeStyle.Id == "fume" || activeStyle.Id == "tilt");
                if (allowVertical && !MusicStageUtil.HasCjk(w.Text) && w.Text.Length >= 3 && verticalCount < 2)
                {
                    if (semi) vertical = true;
                    else if (shot.Kind == "fragment-collage" && rng.Chance(0.07)) vertical = true;
                    else if (shot.Kind == "tracking-ribbon" && rng.Chance(0.04)) vertical = true;
                }
                if (vertical) verticalCount++;
                w.IsVertical = vertical;
                w.IsBold = w.IsHero || (semi && rng.Chance(0.55));
                w.HasChroma = semi || (w.IsHero && rng.Chance(0.5));
                w.IsHighlight = shot.Kind == "poster-blocks" || (shot.Kind == "fragment-collage" && rng.Chance(0.13));

                double scale = SupportScaleFor(shot.Kind);
                if (w.IsHero) scale = HeroScaleFor(shot.Kind);
                else if (semi) scale = Math.Max(SupportScaleFor(shot.Kind) * 1.8, HeroScaleFor(shot.Kind) * 0.68);
                if (MusicStageUtil.HasCjk(w.Text)) scale *= w.IsHero ? 1.0 : 1.08;
                double fontSize = shot.BaseFont * scale;
                if (w.IsHero)
                {
                    double heroWidth = MusicStageUtil.HasCjk(w.Text) ? width * 0.62 : width * 0.82;
                    fontSize = CapFontSize(w.Text, fontSize, heroWidth, height, vertical, true);
                }
                else if (semi)
                {
                    fontSize = CapFontSize(w.Text, fontSize, width * 0.82, height, vertical, false);
                }
                else
                {
                    fontSize = CapFontSize(w.Text, fontSize, width * 0.45, height * 0.72, vertical, false);
                }
                w.FontSize = fontSize;
                Size size = MusicStageText.Measure(w.Text, fontSize, w.IsBold, vertical);
                w.Width = size.Width;
                w.Height = size.Height;
            }

            LayoutShot(shot, words, hero, width, height, rng);

            // 给 1~2 个词加"辅助线"（Folia 的 guide / frame decor）
            words[hero].Guide = 1;
            int guideCount = 0;
            for (int i = 0; i < words.Count && guideCount < 2; i++)
            {
                if (i == hero) continue;
                if (rng.Chance(0.34))
                {
                    words[i].Guide = 2;
                    guideCount++;
                }
            }

            // 超大作背景装饰：hero 复制一份 + 另一个词复制一份（描边，不抢正文）
            var ghosts = new List<MusicStageWord>();
            AddGhostDecorations(ghosts, shot, words[hero], width, height, rng);
            if (words.Count > 3)
            {
                int pick = rng.Pick(words.Count);
                if (pick == hero) pick = (pick + 1) % words.Count;
                var ghost = new MusicStageWord
                {
                    Index = words.Count,
                    LineIndex = -1,
                    Text = words[pick].Text,
                    IsDecoration = true,
                    IsVertical = false,
                    IsBold = false,
                    FontScale = 2.0,
                    FontSize = CapFontSize(words[pick].Text, shot.BaseFont * 2.0, width, height, false, true),
                    X = words[hero].X + width * 0.24,
                    Y = words[hero].Y + height * 0.17,
                    Rotation = 5.5,
                    Start = shot.Start,
                    Settle = shot.Start + 1.4,
                    End = shot.End,
                    Depth = -0.6
                };
                Size gs = MusicStageText.Measure(ghost.Text, ghost.FontSize, false, false);
                ghost.Width = gs.Width;
                ghost.Height = gs.Height;
                ghosts.Add(ghost);
            }
            // 装饰放在最底层，正文按原顺序
            shot.Words.AddRange(ghosts);
            shot.Words.AddRange(words);
        }

        static MusicStageWord NewWord(int index, int lineIndex, string text, double fontSize, double height)
        {
            var w = new MusicStageWord
            {
                Index = index,
                LineIndex = lineIndex,
                Text = text,
                FontSize = fontSize,
                FontScale = 1.0,
                Start = -1,
                Settle = 0,
                End = double.MaxValue,
                LineStart = 0,
                LineEnd = double.MaxValue
            };
            Size size = MusicStageText.Measure(text, fontSize, false, false);
            w.Width = size.Width;
            w.Height = size.Height;
            return w;
        }

        static void AddGhostDecorations(List<MusicStageWord> ghosts, MusicStageShot shot, MusicStageWord hero, double width, double height, MusicStageRng rng)
        {
            var ghost = new MusicStageWord
            {
                Index = 10000,
                LineIndex = -1,
                Text = hero.Text,
                IsDecoration = true,
                IsVertical = false,
                IsBold = false,
                FontScale = 4.2,
                FontSize = CapFontSize(hero.Text, shot.BaseFont * 4.2, width, height, false, true),
                X = width * (0.32 - rng.Range(0, 0.08)),
                Y = height * (0.28 - rng.Range(0, 0.06)),
                Rotation = -8.0,
                Start = shot.Start - 0.4,
                Settle = shot.Start + 1.2,
                End = shot.End,
                Depth = -0.8
            };
            Size gs = MusicStageText.Measure(ghost.Text, ghost.FontSize, false, false);
            ghost.Width = gs.Width;
            ghost.Height = gs.Height;
            ghosts.Add(ghost);
        }

        static double CapFontSize(string text, double fontSize, double maxWidth, double maxHeight, bool vertical, bool hero)
        {
            Size size = MusicStageText.Measure(text, fontSize, false, vertical);
            if (size.Width > maxWidth && size.Width > 1)
            {
                fontSize *= maxWidth / size.Width;
                size = MusicStageText.Measure(text, fontSize, false, vertical);
            }
            double heightLimit = vertical ? maxHeight * 0.72 : maxHeight * (hero ? 0.38 : 0.26);
            if (size.Height > heightLimit && size.Height > 1)
            {
                fontSize *= heightLimit / size.Height;
            }
            return Math.Max(12, fontSize);
        }

        static double HeroScaleFor(string kind)
        {
            if (kind == "type-impact") return 3.8;
            if (kind == "mask-reveal") return 3.2;
            if (kind == "poster-blocks") return 2.8;
            if (kind == "editorial-column") return 3.0;
            if (kind == "tracking-ribbon") return 2.8;
            if (kind == "fragment-collage") return 2.6;
            return 2.2;
        }

        static double SupportScaleFor(string kind)
        {
            if (kind == "quiet-tableau") return 0.88;
            if (kind == "poster-blocks") return 0.95;
            if (kind == "type-impact") return 0.90;
            if (kind == "mask-reveal") return 1.10;
            if (kind == "editorial-column") return 0.92;
            if (kind == "tracking-ribbon") return 1.05;
            return 0.95;
        }

        static void LayoutShot(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            if (shot.Kind == "fragment-collage") LayoutFragment(shot, words, hero, width, height, rng);
            else if (shot.Kind == "type-impact") LayoutTypeImpact(shot, words, hero, width, height, rng);
            else if (shot.Kind == "editorial-column") LayoutEditorial(shot, words, hero, width, height, rng);
            else if (shot.Kind == "tracking-ribbon") LayoutTracking(shot, words, hero, width, height, rng);
            else if (shot.Kind == "mask-reveal") LayoutMask(shot, words, hero, width, height, rng);
            else if (shot.Kind == "poster-blocks") LayoutPoster(shot, words, hero, width, height, rng);
            else LayoutQuiet(shot, words, hero, width, height, rng);

            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (!w.IsDecoration)
                {
                    double marginX = width * 0.045;
                    double marginY = height * 0.065;
                    double halfW = w.Width / 2;
                    double halfH = w.Height / 2;
                    if (halfW * 2 > width - marginX * 2)
                    {
                        double shrink = (width - marginX * 2) / Math.Max(1, halfW * 2);
                        w.FontSize *= shrink;
                        Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                        w.Width = size.Width;
                        w.Height = size.Height;
                        halfW = w.Width / 2;
                        halfH = w.Height / 2;
                    }
                    if (halfH * 2 > height - marginY * 2)
                    {
                        double shrink = (height - marginY * 2) / Math.Max(1, halfH * 2);
                        w.FontSize *= shrink;
                        Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                        w.Width = size.Width;
                        w.Height = size.Height;
                        halfW = w.Width / 2;
                        halfH = w.Height / 2;
                    }
                    w.X = MusicStageUtil.Clamp(w.X, halfW + marginX, width - halfW - marginX);
                    w.Y = MusicStageUtil.Clamp(w.Y, halfH + marginY, height - halfH - marginY);
                }
                if (w.IsHero) continue;
                if (!w.IsVertical && Math.Abs(w.Rotation) < 0.01) w.Rotation = rng.Range(-1.4, 1.4);
                w.EnterX = (w.X < width * 0.5 ? -1 : 1) * rng.Range(14, 34);
                w.EnterY = rng.Range(-24, 24);
                w.EntryRotation = rng.Range(-3.2, 3.2);
            }
            if (words[hero].Guide == 1)
            {
                words[hero].EnterX = 0;
                words[hero].EnterY = height * 0.045;
            }
        }

        static Rect Box(MusicStageWord word, double pad)
        {
            return new Rect(word.X - word.Width / 2 - pad, word.Y - word.Height / 2 - pad,
                word.Width + pad * 2, word.Height + pad * 2);
        }

        static bool Inside(Rect box, double width, double height, double padX, double padY)
        {
            return box.Left >= padX && box.Top >= padY && box.Right <= width - padX && box.Bottom <= height - padY;
        }

        static void LayoutFragment(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            double cx = width * 0.5;
            double cy = height * 0.47;
            var heroWord = words[hero];
            heroWord.X = cx + rng.Range(-0.035, 0.035) * width;
            heroWord.Y = cy + rng.Range(-0.03, 0.03) * height;
            heroWord.Rotation = rng.Range(-1.2, 1.2);

            var placed = new List<Rect>();
            placed.Add(Box(heroWord, 18));
            double angle = rng.Range(0, Math.PI * 2);
            double radius = Math.Max(heroWord.Width, heroWord.Height) * 0.55 + 54;
            double marginX = width * 0.045;
            double marginY = height * 0.075;

            for (int i = 0; i < words.Count; i++)
            {
                if (i == hero) continue;
                var w = words[i];
                bool ok = false;

                // 竖排长词优先靠左右边缘，像参考图里那根竖直的主词
                if (w.IsVertical)
                {
                    double[] xs = new double[] { 0.16, 0.84 };
                    for (int side = 0; side < 2 && !ok; side++)
                    {
                        double x = width * xs[side];
                        double y = height * (0.49 + (i % 2 == 0 ? -0.02 : 0.02));
                        var box = new Rect(x - w.Width / 2 - 24, y - w.Height / 2 - 14, w.Width + 48, w.Height + 28);
                        bool hit = false;
                        for (int j = 0; j < placed.Count; j++)
                        {
                            if (placed[j].IntersectsWith(box)) { hit = true; break; }
                        }
                        if (!hit && Inside(box, width, height, marginX, marginY))
                        {
                            w.X = x;
                            w.Y = y;
                            placed.Add(box);
                            ok = true;
                        }
                    }
                }

                for (int attempt = 0; attempt < 560 && !ok; attempt++)
                {
                    double x = cx + Math.Cos(angle) * radius;
                    double y = cy + Math.Sin(angle) * radius * 0.66;
                    var box = new Rect(x - w.Width / 2 - 14, y - w.Height / 2 - 14, w.Width + 28, w.Height + 28);
                    bool hit = false;
                    for (int j = 0; j < placed.Count; j++)
                    {
                        if (placed[j].IntersectsWith(box)) { hit = true; break; }
                    }
                    if (!hit && Inside(box, width, height, marginX, marginY))
                    {
                        w.X = x;
                        w.Y = y;
                        placed.Add(box);
                        ok = true;
                        break;
                    }
                    angle += 0.085 + rng.Next() * 0.06;
                    if (attempt % 18 == 17) radius += 20 + rng.Next() * 16;
                }
                if (!ok)
                {
                    // 边缘兜底：按序号错开，避免所有兜底词都叠在同一点
                    double x = (i % 2 == 0 ? 0.16 : 0.84) * width;
                    double y = (0.16 + ((i * 0.137) % 0.68)) * height;
                    var box = new Rect(x - w.Width / 2 - 10, y - w.Height / 2 - 10, w.Width + 20, w.Height + 20);
                    for (int tries = 0; tries < 8; tries++)
                    {
                        bool hit = false;
                        for (int j = 0; j < placed.Count; j++)
                        {
                            if (placed[j].IntersectsWith(box)) { hit = true; break; }
                        }
                        if (!hit) break;
                        y = (y + height * 0.1) % (height * 0.8) + height * 0.1;
                        box = new Rect(x - w.Width / 2 - 10, y - w.Height / 2 - 10, w.Width + 20, w.Height + 20);
                    }
                    w.X = x;
                    w.Y = y;
                    placed.Add(box);
                }
                w.Rotation = w.IsVertical ? rng.Range(-2.2, 2.2) : rng.Range(-3.2, 3.2);
            }
        }

        static void LayoutTypeImpact(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            var heroWord = words[hero];
            heroWord.X = width * 0.5;
            heroWord.Y = height * 0.47;
            heroWord.Rotation = rng.Range(-0.8, 0.8);

            var before = new List<MusicStageWord>();
            var after = new List<MusicStageWord>();
            for (int i = 0; i < words.Count; i++)
            {
                if (i == hero) continue;
                if (i < hero) before.Add(words[i]);
                else after.Add(words[i]);
            }
            LayoutRow(before, width * 0.5, heroWord.Y - heroWord.Height * 0.5 - 66, width * 0.86, height, true);
            LayoutRow(after, width * 0.5, heroWord.Y + heroWord.Height * 0.5 + 66, width * 0.86, height, false);
        }

        static void LayoutRow(List<MusicStageWord> words, double centerX, double y, double maxWidth, double height, bool above)
        {
            if (words.Count == 0) return;
            double gap = 26;
            double total = 0;
            for (int i = 0; i < words.Count; i++) total += words[i].Width;
            total += gap * (words.Count - 1);
            double scale = total > maxWidth ? maxWidth / total : 1;
            if (scale < 1)
            {
                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    w.FontSize *= scale;
                    Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                    w.Width = size.Width;
                    w.Height = size.Height;
                }
                total = 0;
                for (int i = 0; i < words.Count; i++) total += words[i].Width;
                total += gap * (words.Count - 1);
            }
            double x = centerX - total / 2;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                w.X = x + w.Width / 2;
                w.Y = y + (above ? -w.Height / 2 : w.Height / 2);
                w.Rotation = 0;
                x += w.Width + gap;
            }
        }

        static void LayoutEditorial(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            var heroWord = words[hero];
            heroWord.X = width * 0.35;
            heroWord.Y = height * 0.49;
            heroWord.Rotation = rng.Range(-0.6, 0.6);

            int support = 0;
            double top = height * 0.17;
            for (int i = 0; i < words.Count; i++)
            {
                if (i == hero) continue;
                var w = words[i];
                double column = (support % 3 == 2) ? 0.79 : 0.67;
                double x = width * column;
                if (top + w.Height > height * 0.88)
                {
                    column = support % 2 == 0 ? 0.55 : 0.83;
                    top = height * 0.17 + (support % 4) * 40;
                    x = width * column;
                }
                w.X = x;
                w.Y = top + w.Height / 2;
                w.Rotation = 0;
                top += w.Height + 34;
                support++;
            }
        }

        static void LayoutTracking(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            double gap = 34;
            double total = 0;
            for (int i = 0; i < words.Count; i++) total += words[i].Width;
            total += gap * Math.Max(0, words.Count - 1);
            double maxWidth = width * 0.84;
            if (total > maxWidth)
            {
                double scale = maxWidth / total;
                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    w.FontSize *= scale;
                    Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                    w.Width = size.Width;
                    w.Height = size.Height;
                }
                total = 0;
                for (int i = 0; i < words.Count; i++) total += words[i].Width;
                total += gap * Math.Max(0, words.Count - 1);
            }
            double x = width * 0.5 - total / 2;
            double y = height * 0.49;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                w.X = x + w.Width / 2;
                w.Y = y + (i % 2 == 0 ? -1 : 1) * (w.Height * 0.16 + 6);
                w.Rotation = w.IsVertical ? 0 : -2.2;
                x += w.Width + gap;
            }
        }

        static void LayoutMask(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            var support = new List<MusicStageWord>();
            for (int i = 0; i < words.Count; i++) if (i != hero) support.Add(words[i]);
            var heroWord = words[hero];
            heroWord.X = width * 0.5;
            heroWord.Y = height * 0.5;
            heroWord.Rotation = 0;

            int rows = support.Count > 3 ? 3 : 2;
            for (int i = 0; i < support.Count; i++)
            {
                var w = support[i];
                int row = i % rows;
                double x = (i % 2 == 0) ? width * 0.17 : width * 0.83;
                double y = height * (0.2 + row * 0.28);
                w.X = x;
                w.Y = y;
                w.Rotation = 0;
            }
        }

        static void LayoutPoster(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            int count = words.Count;
            int cols = 2;
            int rows = Math.Max(1, (int)Math.Ceiling(count / (double)cols));
            double left = width * 0.18;
            double right = width * 0.82;
            double top = height * 0.2;
            double bottom = height * 0.75;
            double cellW = (right - left) / cols;
            double cellH = (bottom - top) / rows;
            for (int i = 0; i < count; i++)
            {
                var w = words[i];
                int row = i / cols;
                int col = i % cols;
                double cx = left + cellW * (col + 0.5);
                double cy = top + cellH * (row + 0.5);
                double scale = Math.Min(1, Math.Min((cellW - 30) / Math.Max(1, w.Width), (cellH - 24) / Math.Max(1, w.Height)));
                if (scale < 0.99)
                {
                    w.FontSize *= scale;
                    Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                    w.Width = size.Width;
                    w.Height = size.Height;
                }
                w.X = cx;
                w.Y = cy;
                w.Rotation = 0;
            }
        }

        static void LayoutQuiet(MusicStageShot shot, List<MusicStageWord> words, int hero, double width, double height, MusicStageRng rng)
        {
            double gap = 20;
            double total = 0;
            for (int i = 0; i < words.Count; i++) total += words[i].Height;
            total += gap * Math.Max(0, words.Count - 1);
            double maxHeight = height * 0.78;
            if (total > maxHeight && total > 1)
            {
                double scale = maxHeight / total;
                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    w.FontSize = Math.Max(10, w.FontSize * scale);
                    Size size = MusicStageText.Measure(w.Text, w.FontSize, w.IsBold, w.IsVertical);
                    w.Width = size.Width;
                    w.Height = size.Height;
                }
                total = 0;
                for (int i = 0; i < words.Count; i++) total += words[i].Height;
                total += gap * Math.Max(0, words.Count - 1);
            }
            double y = height * 0.5 - total / 2;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                w.X = width * 0.5 + (i % 2 == 0 ? -1 : 1) * Math.Min(18, width * 0.012);
                w.Y = y + w.Height / 2;
                w.Rotation = 0;
                y += w.Height + gap;
            }
        }
    }
}

