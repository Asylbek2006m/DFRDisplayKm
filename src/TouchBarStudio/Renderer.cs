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

        readonly Bitmap _frame = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        readonly Graphics _graphics;
        readonly Font _font = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
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
            _graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
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
            using (var path = RoundedRect(rect, 10))
            using (var brush = new SolidBrush(Color.FromArgb(255, background)))
            {
                g.FillPath(brush, path);
            }

            string text = item.Type == ItemType.Clock ? ClockText(item) : item.Label;
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
            _format.Dispose();
        }
    }
}
