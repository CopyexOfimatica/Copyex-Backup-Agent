// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CopyexBackupAgent.UI
{
    internal static class Theme
    {
        // Rojo corporativo (el mismo del logotipo) y variaciones para estados.
        public static readonly Color Brand = Color.FromArgb(255, 0, 0);
        public static readonly Color Primary = Color.FromArgb(232, 0, 0);
        public static readonly Color PrimaryHover = Color.FromArgb(204, 0, 0);
        public static readonly Color PrimaryPressed = Color.FromArgb(170, 0, 0);
        public static readonly Color PrimarySoft = Color.FromArgb(255, 236, 236);
        public static readonly Color PrimaryDisabled = Color.FromArgb(244, 170, 170);

        public static readonly Color Text = Color.FromArgb(28, 28, 30);
        public static readonly Color Muted = Color.FromArgb(104, 104, 110);
        public static readonly Color Border = Color.FromArgb(224, 224, 228);
        public static readonly Color Background = Color.FromArgb(245, 245, 247);
        public static readonly Color Surface = Color.White;
        public static readonly Color Track = Color.FromArgb(234, 234, 238);

        public static readonly Color Sidebar = Color.FromArgb(26, 26, 28);
        public static readonly Color SidebarHover = Color.FromArgb(40, 40, 44);
        public static readonly Color SidebarSelected = Color.FromArgb(50, 50, 54);
        public static readonly Color SidebarText = Color.FromArgb(196, 196, 202);
        public static readonly Color SidebarMuted = Color.FromArgb(130, 130, 136);

        public static readonly Color Success = Color.FromArgb(18, 138, 72);
        public static readonly Color SuccessSoft = Color.FromArgb(228, 245, 235);
        public static readonly Color Warning = Color.FromArgb(196, 112, 0);
        public static readonly Color WarningSoft = Color.FromArgb(255, 243, 224);
        public static readonly Color Danger = Color.FromArgb(200, 16, 16);

        public static readonly Font Regular = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 18f);
        public static readonly Font Value = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 13f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);
        public static readonly Font IconFont = PickIconFont();

        private static Font PickIconFont()
        {
            // Windows 11 trae "Segoe Fluent Icons"; Windows 10 usa "Segoe MDL2 Assets".
            Font f = new Font("Segoe Fluent Icons", 12f);
            if (f.Name == "Segoe Fluent Icons") return f;
            f.Dispose();
            return new Font("Segoe MDL2 Assets", 12f);
        }

        private static Image _logo;

        public static Image Logo
        {
            get
            {
                if (_logo == null)
                {
                    try
                    {
                        Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CopyexBackupAgent.Logo.png");
                        if (s != null)
                            _logo = Image.FromStream(s);
                    }
                    catch
                    {
                    }
                }
                return _logo;
            }
        }

        public static Icon AppIcon
        {
            get
            {
                try
                {
                    return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch
                {
                    return SystemIcons.Application;
                }
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Label MakeLabel(string text, Font font, Color color, int x, int y)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.Text = text;
            l.Location = new Point(x, y);
            return l;
        }
    }

    internal enum ButtonKind
    {
        Primary,
        Secondary,
        Ghost
    }

    /// <summary>Botón plano con esquinas redondeadas.</summary>
    internal class FlatButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.Bold;
            Size = new Size(140, 36);
            Kind = ButtonKind.Secondary;
            UseVisualStyleBackColor = false;
        }

        public ButtonKind Kind { get; set; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, border, text;
            if (Kind == ButtonKind.Primary)
            {
                fill = !Enabled ? Theme.PrimaryDisabled : _pressed ? Theme.PrimaryPressed : _hover ? Theme.PrimaryHover : Theme.Primary;
                border = fill;
                text = Color.White;
            }
            else if (Kind == ButtonKind.Ghost)
            {
                fill = _pressed ? Color.FromArgb(235, 235, 238) : _hover ? Color.FromArgb(243, 243, 245) : (Parent != null ? Parent.BackColor : Color.White);
                border = fill;
                text = Enabled ? Theme.Primary : Color.FromArgb(170, 170, 175);
            }
            else
            {
                fill = !Enabled ? Color.FromArgb(246, 246, 248) : _pressed ? Color.FromArgb(232, 232, 236) : _hover ? Color.FromArgb(244, 244, 246) : Color.White;
                border = _hover && Enabled ? Color.FromArgb(196, 196, 202) : Theme.Border;
                text = Enabled ? Theme.Text : Color.FromArgb(165, 165, 170);
            }

            float radius = 6f * DeviceDpi / 96f;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, path);
                using (Pen p = new Pen(border))
                    g.DrawPath(p, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                Rectangle fr = Rectangle.Inflate(ClientRectangle, -4, -4);
                ControlPaint.DrawFocusRectangle(g, fr, text, fill);
            }
        }
    }

    /// <summary>Elemento del menú lateral.</summary>
    internal class NavButton : Control
    {
        private bool _hover;
        private bool _selected;

        public NavButton(string glyph, string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Glyph = glyph;
            Text = text;
            Font = Theme.Regular;
            Height = 44;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = text;
        }

        public string Glyph { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = DeviceDpi / 96f;
            Color back = _selected ? Theme.SidebarSelected : _hover ? Theme.SidebarHover : Theme.Sidebar;
            g.Clear(back);

            if (_selected)
            {
                using (SolidBrush b = new SolidBrush(Theme.Brand))
                    g.FillRectangle(b, 0, 0, (int)(4 * s), Height);
            }

            Color fore = _selected ? Color.White : Theme.SidebarText;
            Rectangle iconRect = new Rectangle((int)(20 * s), 0, (int)(24 * s), Height);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont, iconRect, _selected ? Theme.Brand : fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle textRect = new Rectangle((int)(56 * s), 0, Width - (int)(60 * s), Height);
            TextRenderer.DrawText(g, Text, _selected ? Theme.Bold : Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3), fore, back);
        }
    }

    /// <summary>Panel blanco con borde fino.</summary>
    internal class Card : Panel
    {
        public Card()
        {
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Contenedor con borde de 1 px para listas y registros.</summary>
    internal class BorderPanel : Panel
    {
        public BorderPanel(Control inner)
        {
            BackColor = Theme.Border;
            Padding = new Padding(1);
            inner.Dock = DockStyle.Fill;
            Controls.Add(inner);
        }
    }

    /// <summary>Barra de progreso roja, con modo "trabajando" animado.</summary>
    internal class RedProgressBar : Control
    {
        private double _value;
        private bool _marquee;
        private float _offset;
        private readonly Timer _timer;

        public RedProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 10;
            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { _offset += 0.012f; if (_offset > 1.3f) _offset = -0.3f; Invalidate(); };
        }

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(v - _value) < 0.0005) return;
                _value = v;
                Invalidate();
            }
        }

        public bool Marquee
        {
            get { return _marquee; }
            set
            {
                if (_marquee == value) return;
                _marquee = value;
                _offset = -0.3f;
                _timer.Enabled = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF track = new RectangleF(0, 0, Width - 1, Height - 1);
            float radius = Height / 2f;
            using (GraphicsPath tp = Theme.RoundRect(track, radius))
            {
                using (SolidBrush b = new SolidBrush(Theme.Track))
                    g.FillPath(b, tp);

                g.SetClip(tp);
                using (SolidBrush b = new SolidBrush(Theme.Primary))
                {
                    if (_marquee)
                    {
                        float w = track.Width * 0.3f;
                        float x = track.Width * _offset;
                        using (GraphicsPath fp = Theme.RoundRect(new RectangleF(x, 0, w, track.Height), radius))
                            g.FillPath(b, fp);
                    }
                    else if (_value > 0)
                    {
                        float w = Math.Max(Height, (float)(track.Width * _value));
                        using (GraphicsPath fp = Theme.RoundRect(new RectangleF(0, 0, w, track.Height), radius))
                            g.FillPath(b, fp);
                    }
                }
                g.ResetClip();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Botón de día de la semana que se marca en rojo.</summary>
    internal class DayToggle : CheckBox
    {
        public DayToggle(string text, DayOfWeek day)
        {
            Day = day;
            Text = text;
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            TextAlign = ContentAlignment.MiddleCenter;
            Font = Theme.Bold;
            Size = new Size(42, 34);
            Cursor = Cursors.Hand;
            FlatAppearance.BorderColor = Theme.Border;
            FlatAppearance.CheckedBackColor = Theme.Primary;
            FlatAppearance.MouseOverBackColor = Theme.PrimarySoft;
            FlatAppearance.MouseDownBackColor = Theme.PrimarySoft;
            UpdateColors();
        }

        public DayOfWeek Day { get; private set; }

        protected override void OnCheckedChanged(EventArgs e)
        {
            UpdateColors();
            base.OnCheckedChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            UpdateColors();
            base.OnEnabledChanged(e);
        }

        private void UpdateColors()
        {
            if (!Enabled)
            {
                BackColor = Color.FromArgb(242, 242, 244);
                ForeColor = Color.FromArgb(170, 170, 175);
                FlatAppearance.BorderColor = Theme.Border;
            }
            else if (Checked)
            {
                BackColor = Theme.Primary;
                ForeColor = Color.White;
                FlatAppearance.BorderColor = BackColor;
                FlatAppearance.MouseOverBackColor = Theme.PrimaryHover;
            }
            else
            {
                BackColor = Color.White;
                ForeColor = Theme.Text;
                FlatAppearance.BorderColor = Theme.Border;
                FlatAppearance.MouseOverBackColor = Theme.PrimarySoft;
            }
        }
    }
}
