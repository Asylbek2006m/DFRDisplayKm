using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TouchBarStudio
{
    public class Tile
    {
        public BarItem Item;
        public Rectangle Rect;
    }

    /// <summary>
    /// Lays out and draws a page of items into a 2170 x 60 bitmap.
    /// The bitmap, graphics object and fonts are created once and reused for every frame.
    /// </summary>
    public class Renderer : IDisposable
    {
        public const int W = 2170;
        public const int H = 60;
        const int Gap = 8;
        const int Margin = 8;
        const int TileTop = 6;
        const int TileHeight = 48;
        const int IconSize = 32;

        readonly Bitmap _frame = new Bitmap(W, H, PixelFormat.Format32bppRgb);
        readonly Graphics _graphics;
        readonly Font _font = new Font("Segoe UI Semibold", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font _small = new Font("Segoe UI Semibold", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font _glyphFont = new Font("Segoe MDL2 Assets", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly StringFormat _left = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        readonly StringFormat _rightAligned = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        readonly StringFormat _format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        readonly Dictionary<string, Image> _icons = new Dictionary<string, Image>();

        public Renderer()
        {
            _graphics = Graphics.FromImage(_frame);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _graphics.PixelOffsetMode = PixelOffsetMode.HalfPixel;
            _graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>The last rendered frame. Owned by the renderer; do not dispose.</summary>
        public Bitmap Frame => _frame;

        public List<Tile> Layout(BarPage page)
        {
            var tiles = new List<Tile>();
            int x = Margin;

            foreach (var item in page.Items.Where(i => i.Align == ItemAlign.Left))
            {
                int w = Math.Max(20, item.Width);
                tiles.Add(new Tile { Item = item, Rect = new Rectangle(x, TileTop, w, TileHeight) });
                x += w + Gap;
            }

            var right = page.Items.Where(i => i.Align == ItemAlign.Right).ToList();
            int total = right.Sum(i => Math.Max(20, i.Width)) + Gap * Math.Max(0, right.Count - 1);
            int rx = W - Margin - total;
            foreach (var item in right)
            {
                int w = Math.Max(20, item.Width);
                tiles.Add(new Tile { Item = item, Rect = new Rectangle(rx, TileTop, w, TileHeight) });
                rx += w + Gap;
            }

            return tiles;
        }

        public static string ClockText(BarItem item)
        {
            try
            {
                return DateTime.Now.ToString(string.IsNullOrEmpty(item.ClockFormat) ? "HH:mm" : item.ClockFormat, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return "bad format";
            }
        }

        public Bitmap Render(AppConfig cfg, List<Tile> tiles, int pressedIndex)
        {
            _graphics.Clear(Color.FromArgb(255, cfg.BarBackground));
            for (int i = 0; i < tiles.Count; i++)
            {
                DrawTile(_graphics, tiles[i], i == pressedIndex);
            }

            return _frame;
        }

        void DrawTile(Graphics g, Tile tile, bool pressed)
        {
            var item = tile.Item;
            var rect = tile.Rect;

            var background = pressed ? Lighten(item.Background, 0.35f) : item.Background;
            if (item.Type == ItemType.MicMute && SystemState.MicMuted == 1)
            {
                background = Color.FromArgb(0xB0, 0x2A, 0x2A);
            }

            using (var path = RoundedRect(rect, 10))
            using (var brush = new SolidBrush(Color.FromArgb(255, background)))
            {
                g.FillPath(brush, path);
            }

            switch (item.Type)
            {
                case ItemType.Brightness:
                    DrawSlider(g, tile, "\uE706", SystemState.BrightnessSupported ? SystemState.BrightnessPct : -1,
                        Loc.T("Not supported"), string.IsNullOrEmpty(item.Label) ? Loc.T("Brightness") : item.Label);
                    return;
                case ItemType.Volume:
                    DrawSlider(g, tile, SystemState.Muted ? "\uE74F" : "\uE767", SystemState.VolumePct,
                        Loc.T("Not supported"), string.IsNullOrEmpty(item.Label) ? Loc.T("Volume") : item.Label);
                    return;
                case ItemType.Scrubber:
                    DrawScrubber(g, tile);
                    return;
            }

            string text = item.Type == ItemType.Clock ? ClockText(item) : item.Label;
            if (item.Type == ItemType.MicMute)
            {
                text = SystemState.MicMuted == 1 ? Loc.T("Mic off") : (string.IsNullOrEmpty(text) ? Loc.T("Mic") : text);
            }
            var textRect = new Rectangle(rect.X + 4, rect.Y, rect.Width - 8, rect.Height);

            var icon = GetIcon(item);
            if (icon != null)
            {
                int iy = rect.Y + (rect.Height - IconSize) / 2;
                if (string.IsNullOrEmpty(text))
                {
                    g.DrawImage(icon, rect.X + (rect.Width - IconSize) / 2, iy, IconSize, IconSize);
                }
                else
                {
                    g.DrawImage(icon, rect.X + 10, iy, IconSize, IconSize);
                    textRect = new Rectangle(rect.X + 10 + IconSize + 6, rect.Y, rect.Width - 10 - IconSize - 12, rect.Height);
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                using (var brush = new SolidBrush(item.ForeColor))
                {
                    g.DrawString(text, _font, brush, textRect, _format);
                }
            }
        }

        void DrawSlider(Graphics g, Tile tile, string glyph, int pct, string unavailable, string label)
        {
            var item = tile.Item;
            var rect = tile.Rect;
            using (var path = RoundedRect(rect, 10))
            {
                if (pct >= 0)
                {
                    int fill = (int)Math.Round(rect.Width * pct / 100.0);
                    g.SetClip(path, CombineMode.Replace);
                    using (var accent = new SolidBrush(item.AccentColor))
                    {
                        g.FillRectangle(accent, rect.X, rect.Y, fill, rect.Height);
                    }

                    g.ResetClip();
                    int knobX = Math.Max(rect.X + 4, Math.Min(rect.Right - 8, rect.X + fill - 3));
                    using (var knob = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                    using (var knobPath = RoundedRect(new Rectangle(knobX, rect.Y + 10, 5, rect.Height - 20), 2))
                    {
                        g.FillPath(knob, knobPath);
                    }
                }
            }

            using (var fore = new SolidBrush(item.ForeColor))
            {
                g.DrawString(glyph, _glyphFont, fore, new Rectangle(rect.X + 12, rect.Y, 34, rect.Height), _left);
                g.DrawString(pct >= 0 ? label : unavailable, _font, fore,
                    new Rectangle(rect.X + 52, rect.Y, rect.Width - 52 - 70, rect.Height), _left);
                if (pct >= 0)
                {
                    g.DrawString(pct + "%", _font, fore, new Rectangle(rect.Right - 80, rect.Y, 68, rect.Height), _rightAligned);
                }
            }
        }

        static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        void DrawScrubber(Graphics g, Tile tile)
        {
            var item = tile.Item;
            var rect = tile.Rect;
            var media = SystemState.Media;
            bool has = media != null && media.HasSession;
            double duration = has ? media.Duration : 0;
            double position = has ? media.Position : 0;
            if (SystemState.ScrubFraction >= 0 && duration > 0)
            {
                position = SystemState.ScrubFraction * duration;
            }

            if (duration > 0)
            {
                int fill = (int)Math.Round(rect.Width * Math.Min(1.0, position / duration));
                using (var path = RoundedRect(rect, 10))
                {
                    g.SetClip(path, CombineMode.Replace);
                    using (var accent = new SolidBrush(item.AccentColor))
                    {
                        g.FillRectangle(accent, rect.X, rect.Y, fill, rect.Height);
                    }

                    g.ResetClip();
                    int knobX = Math.Max(rect.X + 4, Math.Min(rect.Right - 8, rect.X + fill - 3));
                    using (var knob = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                    using (var knobPath = RoundedRect(new Rectangle(knobX, rect.Y + 10, 5, rect.Height - 20), 2))
                    {
                        g.FillPath(knob, knobPath);
                    }
                }
            }

            string title = Loc.T("Nothing is playing");
            if (has && !string.IsNullOrEmpty(media.Title))
            {
                title = string.IsNullOrEmpty(media.Artist) ? media.Title : media.Title + " \u2014 " + media.Artist;
            }

            string times = duration > 0 ? Clock(position) + " / " + Clock(duration) : "";
            int timeWidth = string.IsNullOrEmpty(times) ? 0 : 190;
            using (var fore = new SolidBrush(item.ForeColor))
            {
                g.DrawString(has && media.Playing ? "\uE769" : "\uE768", _glyphFont, fore, new Rectangle(rect.X + 12, rect.Y, 34, rect.Height), _left);
                g.DrawString(title, _font, fore, new Rectangle(rect.X + 52, rect.Y, rect.Width - 52 - timeWidth - 12, rect.Height), _left);
                if (timeWidth > 0)
                {
                    g.DrawString(times, _small, fore, new Rectangle(rect.Right - timeWidth - 8, rect.Y, timeWidth, rect.Height), _rightAligned);
                }
            }
        }

        /// <summary>Icon used for an item (its own icon file, or the program's icon). May be null.</summary>
        public Image IconFor(BarItem item) => GetIcon(item);

        Image GetIcon(BarItem item)
        {
            string path = item.IconPath;
            if (string.IsNullOrWhiteSpace(path) && item.Type == ItemType.App)
            {
                path = item.Path;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (_icons.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Image icon = null;
            try
            {
                string resolved = ResolvePath(path);
                if (resolved != null)
                {
                    string ext = System.IO.Path.GetExtension(resolved).ToLowerInvariant();
                    if (ext == ".exe" || ext == ".lnk" || ext == ".dll" || ext == ".ico")
                    {
                        using (var ico = Icon.ExtractAssociatedIcon(resolved))
                        {
                            icon = ico?.ToBitmap();
                        }
                    }
                    else
                    {
                        using (var tmp = Image.FromFile(resolved))
                        {
                            icon = new Bitmap(tmp);
                        }
                    }
                }
            }
            catch
            {
                icon = null;
            }

            _icons[path] = icon;
            return icon;
        }

        static string ResolvePath(string path)
        {
            if (File.Exists(path))
            {
                return path;
            }

            // Allow bare names such as notepad.exe: look in PATH and System32.
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Concat(new[] { Environment.SystemDirectory });
            foreach (var dir in dirs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var candidate = System.IO.Path.Combine(dir.Trim(), path);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(
                c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void ClearIconCache()
        {
            foreach (var icon in _icons.Values)
            {
                icon?.Dispose();
            }

            _icons.Clear();
        }

        public void Dispose()
        {
            ClearIconCache();
            _graphics.Dispose();
            _frame.Dispose();
            _font.Dispose();
            _small.Dispose();
            _glyphFont.Dispose();
            _left.Dispose();
            _rightAligned.Dispose();
            _format.Dispose();
        }
    }
}
