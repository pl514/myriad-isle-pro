using System;
using System.Collections.Generic;
using System.Text;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 自己写的二维码编码器（字节模式 / 纠错等级 M / 版本 1-10）。
    //
    // 为什么要自己写：网易云只给"二维码内容"（https://music.163.com/login?codekey=xxx），
    // 不给二维码图片，想把它原生画进面板就得自己把字符串变成模块矩阵。
    // 项目没有 NuGet，也不想拖一个第三方 dll 进仓库，所以按 ISO/IEC 18004 实现一遍。
    //
    // 只做这一件事：Encode(text) → bool[,]（true = 黑块，尺寸 N×N）。
    // 不做的是：数字/字母数字模式（字节模式已经够用，UTF-8 直接塞）、版本 11-40
    // （二维码内容最长也就 70 来字节，版本 5 就够；留到版本 10 当余量）。
    //
    // 正确性用 Python 的 qrcode 库逐位比对过（tools\qr-selftest），改动后请重跑。
    // ---------------------------------------------------------------------
    internal static class QrEncoder
    {
        // ---- 版本表（纠错等级 M，版本 1-10）---------------------------------
        // 总码字数 / 每块纠错码字数 / 组1块数 / 组1每块数据码字 / 组2块数 / 组2每块数据码字
        static readonly int[] TotalCodewords = { 0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };
        static readonly int[] EcCodewords = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] Group1Blocks = { 0, 1, 1, 1, 2, 2, 4, 4, 2, 3, 4 };
        static readonly int[] Group1Data = { 0, 16, 28, 44, 32, 43, 27, 31, 38, 36, 43 };
        static readonly int[] Group2Blocks = { 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 1 };
        static readonly int[] Group2Data = { 0, 0, 0, 0, 0, 0, 0, 0, 39, 37, 44 };

        // 校正图形中心坐标（版本 1 没有）
        static readonly int[][] AlignmentPositions = new int[][]
        {
            new int[] { },
            new int[] { },
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 }
        };

        // 纠错等级 M 在格式信息里的编码是 00
        const int EccBitsM = 0;
        const int MinVersion = 1;
        const int MaxVersion = 10;

        // ------------------------------------------------------------ 对外

        /// <summary>把文本编成二维码矩阵。true = 黑块。自动选最小可用版本和最优掩码。</summary>
        internal static bool[,] Encode(string text)
        {
            return Encode(text, -1);
        }

        /// <summary>forceMask 传 0-7 可强制掩码，只给自检用；传 -1 走罚分自动选。</summary>
        internal static bool[,] Encode(string text, int forceMask)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text == null ? "" : text);
            int capacityBits;
            int version = ChooseVersion(payload.Length, out capacityBits);
            if (version < 0) throw new ArgumentException("内容太长，超出本编码器支持的范围（版本 1-10）。");

            List<bool> bits = BuildBitStream(payload, version, capacityBits);
            byte[] data = PackBits(bits);
            byte[] all = AddErrorCorrection(data, version);

            int size = 17 + version * 4;
            bool[,] matrix = new bool[size, size];
            bool[,] reserved = new bool[size, size];

            PlaceFinder(matrix, reserved, 0, 0);
            PlaceFinder(matrix, reserved, size - 7, 0);
            PlaceFinder(matrix, reserved, 0, size - 7);
            PlaceTiming(matrix, reserved, size);
            PlaceAlignment(matrix, reserved, version, size);
            ReserveFormat(matrix, reserved, size);
            if (version >= 7) ReserveVersion(matrix, reserved, version, size);

            PlaceData(matrix, reserved, all, size, version);

            int mask = forceMask;
            if (mask < 0)
            {
                int best = int.MaxValue;
                for (int candidate = 0; candidate < 8; candidate++)
                {
                    bool[,] trial = (bool[,])matrix.Clone();
                    ApplyMask(trial, reserved, candidate);
                    DrawFormat(trial, candidate, size);
                    int penalty = Penalty(trial);
                    if (penalty < best)
                    {
                        best = penalty;
                        mask = candidate;
                    }
                }
            }
            ApplyMask(matrix, reserved, mask);
            DrawFormat(matrix, mask, size);
            return matrix;
        }

        // ------------------------------------------------------------ 版本 / 比特流

        static int ChooseVersion(int byteLength, out int capacityBits)
        {
            for (int version = MinVersion; version <= MaxVersion; version++)
            {
                int dataCodewords = Group1Blocks[version] * Group1Data[version] + Group2Blocks[version] * Group2Data[version];
                int countBits = version >= 10 ? 16 : 8;
                int need = 4 + countBits + byteLength * 8;
                if (need <= dataCodewords * 8)
                {
                    capacityBits = dataCodewords * 8;
                    return version;
                }
            }
            capacityBits = 0;
            return -1;
        }

        static List<bool> BuildBitStream(byte[] payload, int version, int capacityBits)
        {
            var bits = new List<bool>();
            Append(bits, 0x4, 4);                       // 字节模式 0100
            Append(bits, payload.Length, version >= 10 ? 16 : 8);
            for (int i = 0; i < payload.Length; i++) Append(bits, payload[i], 8);

            int terminator = Math.Min(4, capacityBits - bits.Count);
            for (int i = 0; i < terminator; i++) bits.Add(false);

            while (bits.Count % 8 != 0) bits.Add(false);

            bool toggle = true;
            while (bits.Count < capacityBits)
            {
                Append(bits, toggle ? 0xEC : 0x11, 8);
                toggle = !toggle;
            }
            return bits;
        }

        static void Append(List<bool> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        static byte[] PackBits(List<bool> bits)
        {
            byte[] data = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i]) data[i / 8] |= (byte)(0x80 >> (i % 8));
            }
            return data;
        }

        // ------------------------------------------------------------ 纠错

        static byte[] AddErrorCorrection(byte[] data, int version)
        {
            int ecCount = EcCodewords[version];
            int g1 = Group1Blocks[version], g1d = Group1Data[version];
            int g2 = Group2Blocks[version], g2d = Group2Data[version];

            var blocks = new List<byte[]>();
            var blockEc = new List<byte[]>();
            byte[] generator = GeneratorPolynomial(ecCount);

            int offset = 0;
            for (int i = 0; i < g1; i++)
            {
                var block = new byte[g1d];
                Array.Copy(data, offset, block, 0, g1d);
                offset += g1d;
                blocks.Add(block);
                blockEc.Add(Remainder(block, generator, ecCount));
            }
            for (int i = 0; i < g2; i++)
            {
                var block = new byte[g2d];
                Array.Copy(data, offset, block, 0, g2d);
                offset += g2d;
                blocks.Add(block);
                blockEc.Add(Remainder(block, generator, ecCount));
            }

            var result = new List<byte>(TotalCodewords[version]);
            int maxData = Math.Max(g1d, g2d);
            for (int i = 0; i < maxData; i++)
            {
                for (int b = 0; b < blocks.Count; b++)
                {
                    if (i < blocks[b].Length) result.Add(blocks[b][i]);
                }
            }
            for (int i = 0; i < ecCount; i++)
            {
                for (int b = 0; b < blockEc.Count; b++) result.Add(blockEc[b][i]);
            }
            return result.ToArray();
        }

        // GF(256) 用的对数/反对数表（本原多项式 0x11D）
        static readonly byte[] GfExp = new byte[512];
        static readonly byte[] GfLog = new byte[256];

        static QrEncoder()
        {
            int value = 1;
            for (int i = 0; i < 255; i++)
            {
                GfExp[i] = (byte)value;
                GfLog[value] = (byte)i;
                value <<= 1;
                if (value >= 0x100) value ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) GfExp[i] = GfExp[i - 255];
        }

        static byte GfMul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return GfExp[GfLog[a] + GfLog[b]];
        }

        static byte[] GeneratorPolynomial(int degree)
        {
            byte[] poly = new byte[] { 1 };
            for (int i = 0; i < degree; i++)
            {
                var next = new byte[poly.Length + 1];
                for (int j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= GfMul(poly[j], GfExp[i]);
                }
                poly = next;
            }
            return poly;
        }

        static byte[] Remainder(byte[] block, byte[] generator, int ecCount)
        {
            var buffer = new byte[block.Length + ecCount];
            Array.Copy(block, buffer, block.Length);
            for (int i = 0; i < block.Length; i++)
            {
                byte factor = buffer[i];
                if (factor == 0) continue;
                for (int j = 0; j < generator.Length; j++)
                {
                    buffer[i + j] ^= GfMul(generator[j], factor);
                }
            }
            var ec = new byte[ecCount];
            Array.Copy(buffer, block.Length, ec, 0, ecCount);
            return ec;
        }

        // ------------------------------------------------------------ 放模块

        static void PlaceFinder(bool[,] matrix, bool[,] reserved, int left, int top)
        {
            int size = matrix.GetLength(0);
            for (int dy = -1; dy <= 7; dy++)
            {
                for (int dx = -1; dx <= 7; dx++)
                {
                    int y = top + dy, x = left + dx;
                    if (y < 0 || x < 0 || y >= size || x >= size) continue;
                    bool dark;
                    if (dy == -1 || dy == 7 || dx == -1 || dx == 7) dark = false;
                    else if (dy == 0 || dy == 6 || dx == 0 || dx == 6) dark = true;
                    else if (dy >= 2 && dy <= 4 && dx >= 2 && dx <= 4) dark = true;
                    else dark = false;
                    matrix[y, x] = dark;
                    reserved[y, x] = true;
                }
            }
        }

        static void PlaceTiming(bool[,] matrix, bool[,] reserved, int size)
        {
            for (int i = 8; i < size - 8; i++)
            {
                bool dark = i % 2 == 0;
                if (!reserved[6, i]) { matrix[6, i] = dark; reserved[6, i] = true; }
                if (!reserved[i, 6]) { matrix[i, 6] = dark; reserved[i, 6] = true; }
            }
        }

        static void PlaceAlignment(bool[,] matrix, bool[,] reserved, int version, int size)
        {
            int[] positions = AlignmentPositions[version];
            for (int a = 0; a < positions.Length; a++)
            {
                for (int b = 0; b < positions.Length; b++)
                {
                    int cx = positions[a], cy = positions[b];
                    // 和三个定位图形重叠的位置不放
                    if ((cx == 6 && cy == 6) || (cx == 6 && cy == size - 7) || (cx == size - 7 && cy == 6)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            bool dark = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                            matrix[cy + dy, cx + dx] = dark;
                            reserved[cy + dy, cx + dx] = true;
                        }
                    }
                }
            }
        }

        static void ReserveFormat(bool[,] matrix, bool[,] reserved, int size)
        {
            for (int i = 0; i < 9; i++)
            {
                if (!reserved[8, i]) { reserved[8, i] = true; matrix[8, i] = false; }
                if (!reserved[i, 8]) { reserved[i, 8] = true; matrix[i, 8] = false; }
            }
            for (int i = 0; i < 8; i++)
            {
                reserved[8, size - 1 - i] = true;
                matrix[8, size - 1 - i] = false;
                reserved[size - 1 - i, 8] = true;
                matrix[size - 1 - i, 8] = false;
            }
            // 固定的深色模块
            matrix[size - 8, 8] = true;
            reserved[size - 8, 8] = true;
        }

        static void ReserveVersion(bool[,] matrix, bool[,] reserved, int version, int size)
        {
            int data = VersionBits(version);
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((data >> i) & 1) != 0;
                int a = size - 11 + (i % 3);
                int b = i / 3;
                matrix[b, a] = bit; reserved[b, a] = true;
                matrix[a, b] = bit; reserved[a, b] = true;
            }
        }

        static int VersionBits(int version)
        {
            // 版本信息：6 位版本号 + 12 位 BCH(18,6) 校验，生成多项式 0x1F25
            int data = version << 12;
            int remainder = data;
            for (int i = 17; i >= 12; i--)
            {
                if (((remainder >> i) & 1) != 0) remainder ^= 0x1F25 << (i - 12);
            }
            return data | (remainder & 0xFFF);
        }

        static void PlaceData(bool[,] matrix, bool[,] reserved, byte[] codewords, int size, int version)
        {
            int bitIndex = 0;
            int totalBits = codewords.Length * 8;
            bool upward = true;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;   // 跳过竖向计时图形那一列
                for (int step = 0; step < size; step++)
                {
                    int y = upward ? size - 1 - step : step;
                    for (int col = 0; col < 2; col++)
                    {
                        int x = right - col;
                        if (reserved[y, x]) continue;
                        bool dark = false;
                        if (bitIndex < totalBits)
                        {
                            dark = ((codewords[bitIndex / 8] >> (7 - (bitIndex % 8))) & 1) != 0;
                        }
                        matrix[y, x] = dark;
                        bitIndex++;
                    }
                }
                upward = !upward;
            }
        }

        // ------------------------------------------------------------ 掩码 / 格式信息

        static bool MaskBit(int mask, int y, int x)
        {
            switch (mask)
            {
                case 0: return (y + x) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (y + x) % 3 == 0;
                case 4: return (y / 2 + x / 3) % 2 == 0;
                case 5: return (y * x) % 2 + (y * x) % 3 == 0;
                case 6: return ((y * x) % 2 + (y * x) % 3) % 2 == 0;
                default: return ((y + x) % 2 + (y * x) % 3) % 2 == 0;
            }
        }

        static void ApplyMask(bool[,] matrix, bool[,] reserved, int mask)
        {
            int size = matrix.GetLength(0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (reserved[y, x]) continue;
                    if (MaskBit(mask, y, x)) matrix[y, x] = !matrix[y, x];
                }
            }
        }

        static void DrawFormat(bool[,] matrix, int mask, int size)
        {
            int data = (EccBitsM << 3) | mask;
            int bits = FormatBits(data);

            // 第一份（围着左上角定位图形）：注意这里是"列 8 / 行 i"，别写反
            for (int i = 0; i < 6; i++) matrix[i, 8] = ((bits >> i) & 1) != 0;
            matrix[7, 8] = ((bits >> 6) & 1) != 0;
            matrix[8, 8] = ((bits >> 7) & 1) != 0;
            matrix[8, 7] = ((bits >> 8) & 1) != 0;
            for (int i = 9; i < 15; i++) matrix[8, 14 - i] = ((bits >> i) & 1) != 0;

            // 第二份（右上角一行 + 左下角一列）
            for (int i = 0; i < 8; i++) matrix[8, size - 1 - i] = ((bits >> i) & 1) != 0;
            for (int i = 8; i < 15; i++) matrix[size - 15 + i, 8] = ((bits >> i) & 1) != 0;
        }

        static int FormatBits(int data)
        {
            int value = data << 10;
            for (int i = 14; i >= 10; i--)
            {
                if (((value >> i) & 1) != 0) value ^= 0x537 << (i - 10);
            }
            return ((data << 10) | value) ^ 0x5412;
        }

        // ------------------------------------------------------------ 罚分

        static int Penalty(bool[,] matrix)
        {
            int size = matrix.GetLength(0);
            int score = 0;

            // 规则 1：同色连续 5 个以上
            for (int y = 0; y < size; y++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int run = 1;
                    bool last = pass == 0 ? matrix[y, 0] : matrix[0, y];
                    for (int i = 1; i < size; i++)
                    {
                        bool current = pass == 0 ? matrix[y, i] : matrix[i, y];
                        if (current == last) run++;
                        else
                        {
                            if (run >= 5) score += 3 + (run - 5);
                            run = 1;
                            last = current;
                        }
                    }
                    if (run >= 5) score += 3 + (run - 5);
                }
            }

            // 规则 2：2×2 同色
            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    bool c = matrix[y, x];
                    if (c == matrix[y, x + 1] && c == matrix[y + 1, x] && c == matrix[y + 1, x + 1]) score += 3;
                }
            }

            // 规则 3：1:1:3:1:1 形态
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x <= size - 11; x++)
                {
                    if (MatchesFinderLike(matrix, y, x, true)) score += 40;
                }
            }
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y <= size - 11; y++)
                {
                    if (MatchesFinderLike(matrix, y, x, false)) score += 40;
                }
            }

            // 规则 4：深色比例偏离 50%
            int dark = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) if (matrix[y, x]) dark++;
            }
            int total = size * size;
            int percent = (dark * 100) / total;
            int deviation = Math.Abs(percent - 50);
            score += (deviation / 5) * 10;
            return score;
        }

        static bool MatchesFinderLike(bool[,] matrix, int y, int x, bool horizontal)
        {
            // 7 个模块必须是 1:1:3:1:1，并且左或右紧接着 4 个浅色模块（出界算浅色）
            int[] pattern = new int[] { 1, 0, 1, 1, 1, 0, 1 };
            for (int i = 0; i < 7; i++)
            {
                bool dark = horizontal ? matrix[y, x + i] : matrix[y + i, x];
                if (dark != (pattern[i] == 1)) return false;
            }

            bool beforeLight = true, afterLight = true;
            for (int i = 1; i <= 4; i++)
            {
                int before = (horizontal ? x : y) - i;
                if (before >= 0)
                {
                    bool dark = horizontal ? matrix[y, before] : matrix[before, x];
                    if (dark) { beforeLight = false; break; }
                }
                int after = (horizontal ? x : y) + 6 + i;
                int end = horizontal ? matrix.GetLength(1) : matrix.GetLength(0);
                if (after < end)
                {
                    bool dark = horizontal ? matrix[y, after] : matrix[after, x];
                    if (dark) { afterLight = false; break; }
                }
            }
            return beforeLight || afterLight;
        }
    }
}
