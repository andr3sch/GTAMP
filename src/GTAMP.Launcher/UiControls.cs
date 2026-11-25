using System.Drawing.Drawing2D;

namespace GTAMP.Launcher;

/// <summary>
/// Botao moderno com bordas arredondadas anti-aliased e hover suave (estilo FiveM).
/// </summary>
public sealed class RoundedButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public int CornerRadius { get; set; } = 10;
    public Color NormalColor { get; set; } = Color.FromArgb(43, 142, 255);
    public Color HoverColor { get; set; } = Color.FromArgb(63, 158, 255);
    public Color PressedColor { get; set; } = Color.FromArgb(32, 118, 225);
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleCenter;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    }

    public void SetPalette(Color normal)
    {
        NormalColor = normal;
        HoverColor = Lighten(normal, 18);
        PressedColor = Darken(normal, 18);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Color fill = !Enabled ? Color.FromArgb(50, 56, 70)
                   : _pressed ? PressedColor
                   : _hovered ? HoverColor
                   : NormalColor;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = CreateRoundedPath(rect, CornerRadius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);

        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (TextAlign == ContentAlignment.MiddleLeft) flags |= TextFormatFlags.Left;
        else flags |= TextFormatFlags.HorizontalCenter;

        var textRect = new Rectangle(Padding.Left, 0, Width - Padding.Left - Padding.Right, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : Color.Gray, flags);
    }

    public static GraphicsPath CreateRoundedPath(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Lighten(Color c, int amount) =>
        Color.FromArgb(c.A, Math.Min(255, c.R + amount), Math.Min(255, c.G + amount), Math.Min(255, c.B + amount));

    private static Color Darken(Color c, int amount) =>
        Color.FromArgb(c.A, Math.Max(0, c.R - amount), Math.Max(0, c.G - amount), Math.Max(0, c.B - amount));
}

/// <summary>
/// Card com cantos arredondados e borda sutil (estilo FiveM).
/// </summary>
public sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 12;
    public Color FillColor { get; set; } = Color.FromArgb(22, 27, 38);
    public Color BorderColor { get; set; } = Color.FromArgb(42, 50, 68);

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = RoundedButton.CreateRoundedPath(rect, CornerRadius);
        using var brush = new SolidBrush(FillColor);
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }
}
