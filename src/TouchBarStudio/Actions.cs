using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TouchBarStudio
{
    /// <summary>What happens when an item is tapped.</summary>
    public static class Actions
    {
        [DllImport("user32.dll")]
        static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        static extern short VkKeyScan(char ch);

        const uint KeyUp = 0x0002;
        const uint ExtendedKey = 0x0001;

        static readonly Dictionary<string, byte> Modifiers = new Dictionary<string, byte>
        {
            { "ctrl", 0x11 }, { "control", 0x11 }, { "alt", 0x12 }, { "shift", 0x10 },
            { "win", 0x5B }, { "windows", 0x5B }
        };

        static readonly Dictionary<string, byte> NamedKeys = new Dictionary<string, byte>
        {
            { "esc", 0x1B }, { "escape", 0x1B }, { "enter", 0x0D }, { "return", 0x0D },
            { "tab", 0x09 }, { "space", 0x20 }, { "backspace", 0x08 },
            { "delete", 0x2E }, { "del", 0x2E }, { "insert", 0x2D },
            { "home", 0x24 }, { "end", 0x23 }, { "pageup", 0x21 }, { "pagedown", 0x22 },
            { "left", 0x25 }, { "up", 0x26 }, { "right", 0x27 }, { "down", 0x28 },
            { "printscreen", 0x2C }, { "prtsc", 0x2C }
        };

        public static void Run(BarItem item)
        {
            switch (item.Type)
            {
                case ItemType.App:
                    StartProgram(item.Path, item.Arguments);
                    break;
                case ItemType.Media:
                    PressMedia(item.Media);
                    break;
                case ItemType.Hotkey:
                    SendHotkey(item.Hotkey);
                    break;
                case ItemType.Command:
                    if (string.IsNullOrWhiteSpace(item.Command)) throw new InvalidOperationException("Command is empty");
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c " + item.Command)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    break;
                case ItemType.Url:
                    if (string.IsNullOrWhiteSpace(item.Url)) throw new InvalidOperationException("Url is empty");
                    Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
                    break;
                case ItemType.LockScreen:
                    LockWorkStation();
                    break;
                default:
                    // Text, Clock and Page items do nothing here (Page is handled by the main window).
                    break;
            }
        }

        static void StartProgram(string path, string arguments)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Path is empty");

            var info = new ProcessStartInfo(path) { UseShellExecute = true, Arguments = arguments ?? "" };
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    info.WorkingDirectory = dir;
                }
            }
            catch
            {
            }

            Process.Start(info);
        }

        static void PressMedia(MediaKey key)
        {
            byte vk;
            switch (key)
            {
                case MediaKey.Next: vk = 0xB0; break;
                case MediaKey.Previous: vk = 0xB1; break;
                case MediaKey.PlayPause: vk = 0xB3; break;
                case MediaKey.Mute: vk = 0xAD; break;
                case MediaKey.VolumeDown: vk = 0xAE; break;
                default: vk = 0xAF; break; // VolumeUp
            }

            keybd_event(vk, 0, ExtendedKey, UIntPtr.Zero);
            keybd_event(vk, 0, ExtendedKey | KeyUp, UIntPtr.Zero);
        }

        static void SendHotkey(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) throw new InvalidOperationException("Hotkey is empty");

            var tokens = combo.Split('+').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();
            var mods = tokens.Where(t => Modifiers.ContainsKey(t)).Select(t => Modifiers[t]).Distinct().ToList();
            string keyName = tokens.LastOrDefault(t => !Modifiers.ContainsKey(t));
            if (keyName == null) throw new InvalidOperationException("Hotkey has no key: " + combo);

            byte key = ResolveKey(keyName);

            foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyUp, UIntPtr.Zero);
            for (int i = mods.Count - 1; i >= 0; i--) keybd_event(mods[i], 0, KeyUp, UIntPtr.Zero);
        }

        static byte ResolveKey(string name)
        {
            if (NamedKeys.TryGetValue(name, out byte named))
            {
                return named;
            }

            if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name.Substring(1), out int fn) && fn >= 1 && fn <= 24)
            {
                return (byte)(0x6F + fn);
            }

            if (name.Length == 1)
            {
                char c = name[0];
                if (c >= 'a' && c <= 'z') return (byte)char.ToUpperInvariant(c);
                if (c >= '0' && c <= '9') return (byte)c;
                short scan = VkKeyScan(c);
                if (scan != -1) return (byte)(scan & 0xFF);
            }

            throw new InvalidOperationException("Unknown key: " + name);
        }
    }
}
