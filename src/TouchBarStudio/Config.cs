using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchBarStudio
{
    public enum ItemType { App, Media, Hotkey, Command, Url, Clock, Text, Page, LockScreen, Brightness, Volume, Scrubber, MicMute, Battery, Clipboard, Calc, CalcDisplay, Wifi, Bluetooth }

    public enum MediaKey { PlayPause, Next, Previous, Mute, VolumeUp, VolumeDown }

    public enum ItemAlign { Left, Right }

    public class BarItem
    {
        [LocCategory("1. General"), LocDescription("What this item does when tapped.")]
        [LocName("Type")]
        public ItemType Type { get; set; } = ItemType.Text;

        [LocCategory("1. General"), LocDescription("Text shown on the item.")]
        [LocName("Label")]
        public string Label { get; set; } = "Button";

        [LocCategory("1. General"), LocDescription("Width in pixels. The whole bar is 2170 x 60.")]
        [LocName("Width")]
        public int Width { get; set; } = 150;

        [LocCategory("1. General"), LocDescription("Left items start at the left edge, Right items are packed to the right edge.")]
        [LocName("Align")]
        public ItemAlign Align { get; set; } = ItemAlign.Left;

        [LocCategory("2. Action"), LocDescription("Program to start (type App).")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(UITypeEditor))]
        [LocName("Path")]
        public string Path { get; set; } = "";

        [LocCategory("2. Action"), LocDescription("Command-line arguments for the program (type App).")]
        [LocName("Arguments")]
        public string Arguments { get; set; } = "";

        [LocCategory("2. Action"), LocDescription("Media key to press (type Media).")]
        [LocName("Media")]
        public MediaKey Media { get; set; } = MediaKey.PlayPause;

        [LocCategory("2. Action"), LocDescription("Keyboard shortcut (type Hotkey). Examples: ctrl+shift+esc, win+d, alt+f4, ctrl+c")]
        [LocName("Hotkey")]
        public string Hotkey { get; set; } = "";

        [LocCategory("2. Action"), LocDescription("Command line run with cmd.exe /c (type Command).")]
        [LocName("Command")]
        public string Command { get; set; } = "";

        [LocCategory("2. Action"), LocDescription("Address to open (type Url).")]
        [LocName("Url")]
        public string Url { get; set; } = "";

        [LocCategory("2. Action"), LocDescription("Name of the page to switch to (type Page).")]
        [LocName("TargetPage")]
        public string TargetPage { get; set; } = "";

        [LocCategory("2. Action"), LocDescription(".NET date format for type Clock, e.g. HH:mm or ddd d MMM HH:mm")]
        [LocName("ClockFormat")]
        public string ClockFormat { get; set; } = "HH:mm";

        [LocCategory("2. Action"), LocDescription("Which clipboard history entry the item shows: 0 is the newest (type Clipboard).")]
        [LocName("Slot")]
        public int Slot { get; set; }

        [LocCategory("3. Look")]
        [LocName("Background")]
        public Color Background { get; set; } = Color.FromArgb(0x38, 0x38, 0x40);

        [LocCategory("3. Look")]
        [LocName("ForeColor")]
        public Color ForeColor { get; set; } = Color.White;

        [LocCategory("3. Look"), LocDescription("Colour of the filled part of sliders and of the timeline.")]
        [LocName("AccentColor")]
        public Color AccentColor { get; set; } = Color.FromArgb(0x0A, 0x84, 0xFF);

        [LocCategory("3. Look"), LocDescription("Image (png, jpg, bmp, ico) or program file used as the icon. Optional; App items use the program's own icon.")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(UITypeEditor))]
        [LocName("IconPath")]
        public string IconPath { get; set; } = "";

        public override string ToString() => Type + ": " + Label;
    }

    public class BarPage
    {
        public string Name { get; set; } = "Main";
        public List<BarItem> Items { get; set; } = new List<BarItem>();

        /// <summary>Comma separated process names (without .exe). The page is shown while one of them is the active window.</summary>
        public string Processes { get; set; } = "";

        public override string ToString() => Name;
    }

    public class AppConfig
    {
        public List<BarPage> Pages { get; set; } = new List<BarPage>();
        public bool AutoStart { get; set; }
        public bool StartMinimized { get; set; }
        public ThemeMode Theme { get; set; } = ThemeMode.Auto;
        public string Language { get; set; } = "auto";
        public bool AutoSwitchPages { get; set; } = true;
        public Color BarBackground { get; set; } = Color.Black;

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new ColorJsonConverter(), new JsonStringEnumConverter() }
        };

        public static string Dir =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchBarStudio");

        public static string FilePath => System.IO.Path.Combine(Dir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options);
                    if (cfg != null && cfg.Pages != null && cfg.Pages.Count > 0)
                    {
                        return cfg;
                    }
                }
            }
            catch
            {
                // Broken config: fall back to defaults. The broken file is overwritten on the next save.
            }

            return CreateDefault();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch
            {
                // Not fatal; the UI keeps working with the in-memory configuration.
            }
        }

        static BarItem Media(MediaKey key, string label, int width = 100) =>
            new BarItem { Type = ItemType.Media, Media = key, Label = Loc.T(label), Width = width };

        static BarItem App(string label, string path) =>
            new BarItem { Type = ItemType.App, Label = label, Path = path, Width = 170 };

        static BarItem Key(string label, string combo, int width = 120) =>
            new BarItem { Type = ItemType.Hotkey, Label = Loc.T(label), Hotkey = combo, Width = width };

        static BarItem GoTo(string label, string page, int width = 100) =>
            new BarItem { Type = ItemType.Page, Label = Loc.T(label), TargetPage = page, Width = width, Background = Color.FromArgb(0x2D, 0x4A, 0x6B) };

        static BarItem Back(string page) => GoTo("Back", page, 100);

        public static AppConfig CreateDefault()
        {
            var cfg = new AppConfig();
            var main = new BarPage { Name = "Main" };
            main.Items.Add(Media(MediaKey.Previous, "Prev"));
            main.Items.Add(Media(MediaKey.PlayPause, "Play/Pause", 130));
            main.Items.Add(Media(MediaKey.Next, "Next"));
            main.Items.Add(Media(MediaKey.Mute, "Mute"));
            main.Items.Add(Media(MediaKey.VolumeDown, "Vol -"));
            main.Items.Add(Media(MediaKey.VolumeUp, "Vol +"));
            main.Items.Add(App("Explorer", "explorer.exe"));
            main.Items.Add(App("Notepad", "notepad.exe"));
            main.Items.Add(App("Calculator", "calc.exe"));
            main.Items.Add(GoTo("Media", "Media"));
            main.Items.Add(GoTo("Edit", "Edit"));
            main.Items.Add(GoTo("System", "System"));
            main.Items.Add(GoTo("More", "More"));
            main.Items.Add(new BarItem
            {
                Type = ItemType.Clock,
                Label = "",
                Width = 220,
                Align = ItemAlign.Right,
                ClockFormat = "ddd d MMM  HH:mm"
            });
            cfg.Pages.Add(main);
            cfg.AddPresetPages();
            return cfg;
        }

        /// <summary>Adds the Media, Edit and System pages when no page with that name exists yet. Returns how many were added.</summary>
        public int AddPresetPages()
        {
            int added = 0;
            bool Missing(string name) => !Pages.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (Missing("Media"))
            {
                var page = new BarPage { Name = "Media", Processes = "spotify,vlc,wmplayer" };
                page.Items.Add(Back("Main"));
                page.Items.Add(new BarItem { Type = ItemType.Scrubber, Label = "", Width = 1000 });
                page.Items.Add(Media(MediaKey.Previous, "Prev"));
                page.Items.Add(Media(MediaKey.PlayPause, "Play/Pause", 130));
                page.Items.Add(Media(MediaKey.Next, "Next"));
                page.Items.Add(new BarItem { Type = ItemType.Volume, Label = "", Width = 360 });
                Pages.Add(page);
                added++;
            }

            if (Missing("Edit"))
            {
                var page = new BarPage { Name = "Edit" };
                page.Items.Add(Back("Main"));
                page.Items.Add(Key("Undo", "ctrl+z"));
                page.Items.Add(Key("Redo", "ctrl+y"));
                page.Items.Add(Key("Cut", "ctrl+x"));
                page.Items.Add(Key("Copy", "ctrl+c"));
                page.Items.Add(Key("Paste", "ctrl+v"));
                page.Items.Add(Key("Select all", "ctrl+a", 150));
                page.Items.Add(Key("Clipboard history", "win+v", 230));
                Pages.Add(page);
                added++;
            }

            if (Missing("System"))
            {
                var page = new BarPage { Name = "System" };
                page.Items.Add(Back("Main"));
                page.Items.Add(new BarItem { Type = ItemType.Brightness, Label = "", Width = 380 });
                page.Items.Add(new BarItem { Type = ItemType.Volume, Label = "", Width = 380 });
                page.Items.Add(new BarItem { Type = ItemType.MicMute, Label = Loc.T("Mic"), Width = 150 });
                page.Items.Add(Key("Screenshot", "win+shift+s", 170));
                page.Items.Add(new BarItem { Type = ItemType.LockScreen, Label = Loc.T("Lock"), Width = 110 });
                page.Items.Add(Key("Task view", "win+tab", 150));
                page.Items.Add(Key("Show desktop", "win+d", 170));
                Pages.Add(page);
                added++;
            }

            if (Missing("Calculator"))
            {
                var page = new BarPage { Name = "Calculator" };
                page.Items.Add(Back("Main"));
                page.Items.Add(new BarItem { Type = ItemType.CalcDisplay, Label = "", Width = 300, Background = Color.FromArgb(0x1C, 0x1C, 0x22) });
                string[] keys = { "7", "8", "9", "\u00F7", "4", "5", "6", "\u00D7", "1", "2", "3", "-", "0", ".", "=", "+", "C", "\u232B", "%", "\u00B1" };
                foreach (var k in keys)
                {
                    bool op = "\u00F7\u00D7-+=".Contains(k);
                    page.Items.Add(new BarItem
                    {
                        Type = ItemType.Calc,
                        Label = k,
                        Width = 72,
                        Background = op ? Color.FromArgb(0xB3, 0x6A, 0x12) : (char.IsDigit(k[0]) || k == "." ? Color.FromArgb(0x38, 0x38, 0x40) : Color.FromArgb(0x55, 0x55, 0x5E))
                    });
                }

                Pages.Add(page);
                added++;
            }

            if (Missing("Clipboard"))
            {
                var page = new BarPage { Name = "Clipboard" };
                page.Items.Add(Back("Main"));
                for (int i = 0; i < 6; i++)
                {
                    page.Items.Add(new BarItem { Type = ItemType.Clipboard, Label = "", Slot = i, Width = 300 });
                }

                Pages.Add(page);
                added++;
            }

            if (Missing("Search"))
            {
                var page = new BarPage { Name = "Search" };
                page.Items.Add(Back("Main"));
                page.Items.Add(new BarItem { Type = ItemType.Url, Label = "Google: " + Loc.T("selection"), Url = "https://www.google.com/search?q={selection}", Width = 330 });
                page.Items.Add(new BarItem { Type = ItemType.Url, Label = "Google: " + Loc.T("clipboard"), Url = "https://www.google.com/search?q={clipboard}", Width = 330 });
                page.Items.Add(new BarItem { Type = ItemType.Url, Label = "Bing: " + Loc.T("selection"), Url = "https://www.bing.com/search?q={selection}", Width = 330 });
                page.Items.Add(new BarItem { Type = ItemType.Url, Label = "Bing: " + Loc.T("clipboard"), Url = "https://www.bing.com/search?q={clipboard}", Width = 330 });
                Pages.Add(page);
                added++;
            }

            if (Missing("Power"))
            {
                var page = new BarPage { Name = "Power" };
                page.Items.Add(Back("Main"));
                page.Items.Add(new BarItem { Type = ItemType.Battery, Label = "", Width = 260 });
                page.Items.Add(new BarItem { Type = ItemType.Wifi, Label = "", Width = 200 });
                page.Items.Add(new BarItem { Type = ItemType.Bluetooth, Label = "", Width = 230 });
                page.Items.Add(new BarItem { Type = ItemType.Command, Label = Loc.T("Sleep"), Command = "rundll32.exe powrprof.dll,SetSuspendState 0,1,0", Width = 150 });
                page.Items.Add(new BarItem { Type = ItemType.LockScreen, Label = Loc.T("Lock"), Width = 120 });
                Pages.Add(page);
                added++;
            }

            if (Missing("More"))
            {
                var page = new BarPage { Name = "More" };
                page.Items.Add(Back("Main"));
                page.Items.Add(GoTo("Calculator", "Calculator", 190));
                page.Items.Add(GoTo("Clipboard", "Clipboard", 190));
                page.Items.Add(GoTo("Search", "Search", 160));
                page.Items.Add(GoTo("Power", "Power", 150));
                Pages.Add(page);
                added++;
            }

            return added;
        }
    }

    public class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = (reader.GetString() ?? "#FF000000").TrimStart('#');
            uint value = Convert.ToUInt32(text, 16);
            if (text.Length <= 6)
            {
                value |= 0xFF000000;
            }

            return Color.FromArgb(unchecked((int)value));
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStringValue("#" + value.A.ToString("X2") + value.R.ToString("X2") + value.G.ToString("X2") + value.B.ToString("X2"));
        }
    }
}
