using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchBarStudio
{
    public enum ThemeMode { Auto, Light, Dark }

    public class Palette
    {
        public bool Dark;
        public Color Window, Surface, SurfaceAlt, Hover, Pressed, Border, Text, SubText;
        public Color Accent, AccentHover, AccentPressed, AccentText;
    }

    /// <summary>Windows 11 look: light/dark palette that follows the system, accent color, DWM window effects.</summary>
    public static class Theme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        public static Palette P { get; private set; } = Build(false, Color.FromArgb(0, 103, 192));
        public static event Action Changed;

        public static float ScaleFactor { get; private set; } = 1f;
        public static string FontName { get; private set; } = "Segoe UI";
        public static Font UiFont { get; private set; }

        public static int S(int value) => (int)Math.Round(value * ScaleFactor);

        public static Font MakeFont(float size = 9.5f, FontStyle style = FontStyle.Regular)
        {
            return new Font(FontName, size, style, GraphicsUnit.Point);
        }

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                ScaleFactor = g.DpiX / 96f;
            }

            bool hasVariable = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text");
            FontName = hasVariable ? "Segoe UI Variable Text" : "Segoe UI";
            UiFont = MakeFont();
        }

        public static void Apply(ThemeMode mode)
        {
            bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.Auto && !SystemUsesLightTheme());
            P = Build(dark, SystemAccent());
            Changed?.Invoke();
        }

        static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

        static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static Palette Build(bool dark, Color accent)
        {
            // A very dark accent is unreadable on a dark window: lighten it.
            if (dark && Luminance(accent) < 0.35)
            {
                accent = Mix(accent, Color.White, 0.35f);
            }

            var p = new Palette { Dark = dark, Accent = accent };
            if (dark)
            {
                p.Window = C(0x20, 0x20, 0x20);
                p.Surface = C(0x2B, 0x2B, 0x2B);
                p.SurfaceAlt = C(0x33, 0x33, 0x33);
                p.Hover = C(0x3A, 0x3A, 0x3A);
                p.Pressed = C(0x2D, 0x2D, 0x2D);
                p.Border = C(0x3E, 0x3E, 0x3E);
                p.Text = C(0xFF, 0xFF, 0xFF);
                p.SubText = C(0xA8, 0xA8, 0xA8);
                p.AccentHover = Mix(accent, Color.White, 0.12f);
                p.AccentPressed = Mix(accent, Color.Black, 0.15f);
            }
            else
            {
                p.Window = C(0xF3, 0xF3, 0xF3);
                p.Surface = C(0xFF, 0xFF, 0xFF);
                p.SurfaceAlt = C(0xFB, 0xFB, 0xFB);
                p.Hover = C(0xF0, 0xF0, 0xF0);
                p.Pressed = C(0xE6, 0xE6, 0xE6);
                p.Border = C(0xE0, 0xE0, 0xE0);
                p.Text = C(0x1A, 0x1A, 0x1A);
                p.SubText = C(0x60, 0x60, 0x60);
                p.AccentHover = Mix(accent, Color.Black, 0.10f);
                p.AccentPressed = Mix(accent, Color.Black, 0.22f);
            }

            p.AccentText = Luminance(accent) > 0.62 ? Color.Black : Color.White;
            return p;
        }

        static bool SystemUsesLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return !(key?.GetValue("AppsUseLightTheme") is int v) || v != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        static Color SystemAccent()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key?.GetValue("AccentColor") is int abgr)
                    {
                        return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    }
                }
            }
            catch
            {
            }

            return C(0, 103, 192);
        }

        /// <summary>Dark title bar, rounded corners and Mica backdrop. Ignored by systems that do not support them.</summary>
        public static void StyleWindow(Form form)
        {
            if (!form.IsHandleCreated) return;

            SetAttribute(form.Handle, 20, P.Dark ? 1 : 0); // immersive dark mode
            SetAttribute(form.Handle, 19, P.Dark ? 1 : 0); // same, older Windows 10 builds
            SetAttribute(form.Handle, 33, 2);              // rounded corners (Windows 11)
            SetAttribute(form.Handle, 38, 2);              // Mica backdrop (Windows 11 22H2+)
        }

        static void SetAttribute(IntPtr handle, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            catch
            {
            }
        }

        /// <summary>Dark scrollbars for standard controls (best effort).</summary>
        public static void StyleScrollbars(Control control)
        {
            try
            {
                if (control.IsHandleCreated)
                {
                    SetWindowTheme(control.Handle, P.Dark ? "DarkMode_Explorer" : "Explorer", null);
                }

                foreach (Control child in control.Controls)
                {
                    StyleScrollbars(child);
                }
            }
            catch
            {
            }
        }

        public static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void StyleMenu(ToolStripDropDown menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable()) { RoundedEdges = false };
            menu.BackColor = P.Surface;
            menu.ForeColor = P.Text;
            menu.Font = UiFont;
            if (menu is ToolStripDropDownMenu dropDownMenu)
            {
                dropDownMenu.ShowImageMargin = false;
            }
        }
    }

    class ThemeColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.P.Surface;
        public override Color MenuBorder => Theme.P.Border;
        public override Color MenuItemBorder => Theme.P.Hover;
        public override Color MenuItemSelected => Theme.P.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.P.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.P.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.P.Pressed;
        public override Color MenuItemPressedGradientEnd => Theme.P.Pressed;
        public override Color ImageMarginGradientBegin => Theme.P.Surface;
        public override Color ImageMarginGradientMiddle => Theme.P.Surface;
        public override Color ImageMarginGradientEnd => Theme.P.Surface;
        public override Color SeparatorDark => Theme.P.Border;
        public override Color SeparatorLight => Theme.P.Border;
        public override Color CheckBackground => Theme.P.Accent;
        public override Color CheckSelectedBackground => Theme.P.Accent;
        public override Color CheckPressedBackground => Theme.P.Accent;
    }
}
