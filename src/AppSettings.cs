using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IslandShot
{
    /// <summary>
    /// 极简配置：`data\settings.txt`，一行一个 `key=value`。
    ///
    /// 目前只有卡通眼睛开关（配置项 `EyesEnabled`）。
    /// 以后要加选项，就在下面照着 `EyesEnabled` 写一个属性即可，读写/持久化都是现成的。
    /// </summary>
    internal static class AppSettings
    {
        static readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string filePath;

        /// <summary>卡通眼睛总开关（配置项 EyesEnabled）。默认开。</summary>
        public static bool EyesEnabled
        {
            get { return GetBool("EyesEnabled", true); }
            set { values["EyesEnabled"] = value ? "1" : "0"; }
        }

        /// <summary>低权限提示窗是否还显示（配置项 LowPrivilegeNotice）。默认显示。</summary>
        public static bool LowPrivilegeNotice
        {
            get { return GetBool("LowPrivilegeNotice", true); }
            set
            {
                values["LowPrivilegeNotice"] = value ? "1" : "0";
                Save();
            }
        }

        /// <summary>读配置。folder 是 data 目录（和 images.txt 同一个地方）。</summary>
        public static void Init(string folder)
        {
            try
            {
                filePath = Path.Combine(folder, "settings.txt");
                values.Clear();
                if (!File.Exists(filePath))
                {
                    // 第一次运行就把默认值落盘，方便用户直接改。
                    Save();
                    return;
                }
                foreach (string line in File.ReadAllLines(filePath))
                {
                    string text = line == null ? string.Empty : line.Trim();
                    if (text.Length == 0 || text.StartsWith("#") || text.StartsWith(";")) continue;
                    int split = text.IndexOf('=');
                    if (split <= 0) continue;
                    values[text.Substring(0, split).Trim()] = text.Substring(split + 1).Trim();
                }
            }
            catch (Exception exception)
            {
                DragNative.Log("settings load failed: " + exception.Message);
            }
        }

        /// <summary>写回配置（key 按字母序，方便手改）。</summary>
        public static void Save()
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                string folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var keys = new List<string>(values.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                var lines = new List<string>();
                lines.Add("# Capsule image Pro 配置（一行一个 key=value）");
                lines.Add("# EyesEnabled：卡通眼睛开关，1=开 0=关");
                foreach (string key in keys) lines.Add(key + "=" + values[key]);
                File.WriteAllLines(filePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                DragNative.Log("settings save failed: " + exception.Message);
            }
        }

        static bool GetBool(string key, bool fallback)
        {
            string raw;
            if (!values.TryGetValue(key, out raw) || string.IsNullOrEmpty(raw)) return fallback;
            raw = raw.Trim();
            if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }
}