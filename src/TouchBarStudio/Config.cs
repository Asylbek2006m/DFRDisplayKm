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
    public enum ItemType { App, Media, Hotkey, Command, Url, Clock, Text, Page, LockScreen }

    public enum MediaKey { PlayPause, Next, Previous, Mute, VolumeUp, VolumeDown }

    public enum ItemAlign { Left, Right }

    public class BarItem
    {
        [Category("1. General"), Description("What this item does when tapped.")]
        public ItemType Type { get; set; } = ItemType.Text;

        [Category("1. General"), Description("Text shown on the item.")]
        public string Label { get; set; } = "Button";

        [Category("1. General"), Description("Width in pixels. The whole bar is 2170 x 60.")]
        public int Width { get; set; } = 150;

        [Category("1. General"), Description("Left items start at the left edge, Right items are packed to the right edge.")]
        public ItemAlign Align { get; set; } = ItemAlign.Left;

        [Category("2. Action"), Description("Program to start (type App).")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(UITypeEditor))]
        public string Path { get; set; } = "";

        [Category("2. Action"), Description("Command-line arguments for the program (type App).")]
        public string Arguments { get; set; } = "";

        [Category("2. Action"), Description("Media key to press (type Media).")]
        public MediaKey Media { get; set; } = MediaKey.PlayPause;

        [Category("2. Action"), Description("Keyboard shortcut (type Hotkey). Examples: ctrl+shift+esc, win+d, alt+f4, ctrl+c")]
        public string Hotkey { get; set; } = "";

        [Category("2. Action"), Description("Command line run with cmd.exe /c (type Command).")]
        public string Command { get; set; } = "";

        [Category("2. Action"), Description("Address to open (type Url).")]
        public string Url { get; set; } = "";

        [Category("2. Action"), Description("Name of the page to switch to (type Page).")]
        public string TargetPage { get; set; } = "";

        [Category("2. Action"), Description(".NET date format for type Clock, e.g. HH:mm or ddd d MMM HH:mm")]
        public string ClockFormat { get; set; } = "HH:mm";

        [Category("3. Look")]
        public Color Background { get; set; } = Color.FromArgb(0x38, 0x38, 0x40);

        [Category("3. Look")]
        public Color ForeColor { get; set; } = Color.White;

        [Category("3. Look"), Description("Image (png, jpg, bmp, ico) or program file used as the icon. Optional; App items use the program's own icon.")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(UITypeEditor))]
        public string IconPath { get; set; } = "";

        public override string ToString() => Type + ": " + Label;
    }

    public class BarPage
    {
        public string Name { get; set; } = "Main";
        public List<BarItem> Items { get; set; } = new List<BarItem>();
        public override string ToString() => Name;
    }

    public class AppConfig
    {
        public List<BarPage> Pages { get; set; } = new List<BarPage>();
        public bool AutoStart { get; set; }
        public bool StartMinimized { get; set; }
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

        static BarItem Media(MediaKey key, string label) =>
            new BarItem { Type = ItemType.Media, Media = key, Label = label, Width = 120 };

        static BarItem App(string label, string path) =>
            new BarItem { Type = ItemType.App, Label = label, Path = path, Width = 190 };

        public static AppConfig CreateDefault()
        {
            var main = new BarPage { Name = "Main" };
            main.Items.Add(Media(MediaKey.Previous, "Prev"));
            main.Items.Add(Media(MediaKey.PlayPause, "Play/Pause"));
            main.Items.Add(Media(MediaKey.Next, "Next"));
            main.Items.Add(Media(MediaKey.Mute, "Mute"));
            main.Items.Add(Media(MediaKey.VolumeDown, "Vol -"));
            main.Items.Add(Media(MediaKey.VolumeUp, "Vol +"));
            main.Items.Add(App("Explorer", "explorer.exe"));
            main.Items.Add(App("Notepad", "notepad.exe"));
            main.Items.Add(App("Calculator", "calc.exe"));
            main.Items.Add(new BarItem
            {
                Type = ItemType.Clock,
                Label = "",
                Width = 220,
                Align = ItemAlign.Right,
                ClockFormat = "ddd d MMM  HH:mm"
            });

            var cfg = new AppConfig();
            cfg.Pages.Add(main);
            return cfg;
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
