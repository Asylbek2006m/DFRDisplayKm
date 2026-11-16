using System.ComponentModel;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TouchBarStudio
{
    /// <summary>Flat rounded button with hover and pressed states. Accent = filled with the system accent color.</summary>
    public class ModernButton : Control
    {
        bool _hover;
        bool _down;
        bool _accent;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont;
            Height = Theme.S(32);
            Width = Theme.S(96);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Accent
        {
            get => _accent;
            set { _accent = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _down = true; Focus(); Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : p.Window);

            Color fill, text, border;
            if (_accent)
            {
                fill = _down ? p.AccentPressed : _hover ? p.AccentHover : p.Accent;
                text = p.AccentText;
                border = fill;
            }
            else
            {
                fill = _down ? p.Pressed : _hover ? p.Hover : p.SurfaceAlt;
                text = p.Text;
                border = p.Border;
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundedPath(rect, Theme.S(6)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused && ShowFocusCues ? p.Accent : border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>A rounded "card" surface. Children are inset by Padding so the corners stay visible.</summary>
    public class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(Theme.S(10));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundedPath(rect, Theme.S(8)))
            using (var brush = new SolidBrush(p.Surface))
            using (var pen = new Pen(p.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Shows the rendered bar scaled to fit. Maps mouse positions back to bar pixels.</summary>
    public class PreviewSurface : Control
    {
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap Frame { get; set; }

        public PreviewSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            Cursor = Cursors.Hand;
        }

        RectangleF FrameRect()
        {
            float scale = Math.Min((float)Width / Renderer.W, (float)Height / Renderer.H);
            float w = Renderer.W * scale, h = Renderer.H * scale;
            return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        }

        /// <summary>Bar X pixel under a control point, or -1 when outside the bar.</summary>
        public int ToBarX(Point point)
        {
            var r = FrameRect();
            if (r.Width <= 0 || point.X < r.Left || point.X >= r.Right) return -1;

            int x = (int)((point.X - r.Left) / r.Width * Renderer.W);
            return x < 0 || x >= Renderer.W ? -1 : x;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            if (Frame == null) return;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(Frame, FrameRect());
        }
    }
}
