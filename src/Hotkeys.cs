using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace IslandShot
{
    internal enum HotkeyAction
    {
        Toggle = 1,
        Region = 2,
        Window = 3,
        Screen = 4,
        ImagesToggle = 5,
        Record = 6
    }

    internal sealed class HotkeyBinding
    {
        public HotkeyAction Action;
        public uint Modifiers;
        public uint Key;

        public HotkeyBinding Clone()
        {
            return new HotkeyBinding { Action = Action, Modifiers = Modifiers, Key = Key };
        }

        public string Name
        {
            get
            {
                if (Action == HotkeyAction.Toggle) return "显示 / 收起";
                if (Action == HotkeyAction.Region) return "区域截图";
                if (Action == HotkeyAction.Window) return "窗口截图";
                if (Action == HotkeyAction.Screen) return "全屏截图";
                if (Action == HotkeyAction.ImagesToggle) return "图片收起";
                return "开始/停止录屏";
            }
        }

        public string Display
        {
            get
            {
                if (Key == 0) return "未设置";
                var text = new StringBuilder();
                if ((Modifiers & HotkeyStore.ModControl) != 0) text.Append("Ctrl+");
                if ((Modifiers & HotkeyStore.ModAlt) != 0) text.Append("Alt+");
                if ((Modifiers & HotkeyStore.ModShift) != 0) text.Append("Shift+");
                Key key = KeyInterop.KeyFromVirtualKey((int)Key);
                text.Append(key == System.Windows.Input.Key.None ? "0x" + Key.ToString("X") : key.ToString());
                return text.ToString();
            }
        }
    }

    internal static class HotkeyStore
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;

        static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "hotkeys.txt"); }
        }

        public static HotkeyBinding[] Defaults()
        {
            return new[]
            {
                new HotkeyBinding { Action = HotkeyAction.Toggle, Modifiers = ModControl | ModAlt | ModShift, Key = 0x20 },
                new HotkeyBinding { Action = HotkeyAction.Region, Modifiers = ModControl | ModAlt | ModShift, Key = 0x41 },
                new HotkeyBinding { Action = HotkeyAction.Window, Modifiers = ModControl | ModAlt | ModShift, Key = 0x32 },
                new HotkeyBinding { Action = HotkeyAction.Screen, Modifiers = ModControl | ModAlt | ModShift, Key = 0x33 },
                new HotkeyBinding { Action = HotkeyAction.ImagesToggle, Modifiers = ModControl | ModAlt | ModShift, Key = 0x48 },
                new HotkeyBinding { Action = HotkeyAction.Record, Modifiers = ModControl | ModAlt | ModShift, Key = 0x52 }
            };
        }

        public static HotkeyBinding[] Load()
        {
            HotkeyBinding[] defaults = Defaults();
            try
            {
                if (!File.Exists(FilePath)) return defaults;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 3) continue;
                    HotkeyAction action;
                    if (!Enum.TryParse(parts[0], out action)) continue;
                    uint modifiers, key;
                    if (!uint.TryParse(parts[1], out modifiers) || !uint.TryParse(parts[2], out key)) continue;
                    foreach (HotkeyBinding item in defaults)
                    {
                        if (item.Action == action) { item.Modifiers = modifiers; item.Key = key; }
                    }
                }
            }
            catch (Exception) { }
            return defaults;
        }

        public static void Save(HotkeyBinding[] bindings)
        {
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                foreach (HotkeyBinding item in bindings) lines.Add(item.Action + "|" + item.Modifiers + "|" + item.Key);
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception) { }
        }
    }
}
