using System.Drawing.Drawing2D;
using XiControl.Localization;

namespace XiControl.Ui.Settings;

/// <summary>Глифы навигации (рисуем сами — без иконочных шрифтов и ресурсов).</summary>
public enum NavGlyph { General, Battery, Display, Perf, Keys, About }

/// <summary>
/// Левая навигация окна настроек (кастомная отрисовка): подсветка hover/выбора,
/// акцентная полоска, «О программе» прижата вниз.
/// </summary>
public sealed class NavStrip : Panel
{
    private readonly SettingsToolkit _ui;
    public (string key, NavGlyph glyph)[] Tabs = [];
    public int Selected;
    public Action<int>? SelectedChanged;
    private int _hover = -1;

    public NavStrip(SettingsToolkit ui)
    {
        _ui = ui;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    private int ItemH => _ui.Sc(40);
    private int TopPad => _ui.Sc(12);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = HitTest(e.Y);
        if (i != _hover) { _hover = i; Invalidate(); Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = HitTest(e.Y);
        if (i >= 0 && i != Selected) SelectedChanged?.Invoke(i);
        base.OnMouseClick(e);
    }

    // «О программе» прижата вниз
    private bool IsBottom(int i) => i == Tabs.Length - 1;
    private int RowY(int i) => IsBottom(i) ? Height - _ui.Sc(12) - ItemH : TopPad + i * (ItemH + _ui.Sc(2));
    private int HitTest(int y)
    {
        for (int i = 0; i < Tabs.Length; i++)
            if (y >= RowY(i) && y < RowY(i) + ItemH) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_ui.T.NavBg);
        int pad = _ui.Sc(8);

        for (int i = 0; i < Tabs.Length; i++)
        {
            int y = RowY(i);
            var rect = new Rectangle(pad, y, Width - pad * 2, ItemH);
            bool sel = i == Selected, hov = i == _hover;
            if (sel || hov)
            {
                using var b = new SolidBrush(sel ? _ui.T.Sel : Color.FromArgb(_ui.T.Dark ? 22 : 14, _ui.T.Text));
                using var path = Draw.Rounded(rect, _ui.Sc(5));
                g.FillPath(b, path);
            }
            if (sel)
            {
                using var ab = new SolidBrush(_ui.T.Accent);
                using var bar = Draw.Rounded(new RectangleF(rect.X, rect.Y + ItemH * 0.28f, _ui.Sc(3), ItemH * 0.44f), _ui.Sc(1.5f));
                g.FillPath(ab, bar);
            }
            var gc = sel ? _ui.T.Accent : _ui.T.Text2;
            DrawGlyph(g, Tabs[i].glyph, new RectangleF(rect.X + _ui.Sc(12), rect.Y + (ItemH - _ui.Sc(18)) / 2f, _ui.Sc(18), _ui.Sc(18)), gc);
            TextRenderer.DrawText(g, Loc.T(Tabs[i].key), _ui.TitleFont,
                new Rectangle(rect.X + _ui.Sc(40), rect.Y, rect.Width - _ui.Sc(40), ItemH),
                _ui.T.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawGlyph(Graphics g, NavGlyph k, RectangleF r, Color c)
    {
        using var pen = new Pen(c, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (k)
        {
            case NavGlyph.General:
                g.DrawEllipse(pen, x + w * 0.35f, y + h * 0.35f, w * 0.3f, h * 0.3f);
                for (int a = 0; a < 8; a++)
                {
                    double ang = a * Math.PI / 4;
                    float cx = x + w / 2f, cy = y + h / 2f;
                    g.DrawLine(pen, cx + (float)Math.Cos(ang) * w * 0.32f, cy + (float)Math.Sin(ang) * h * 0.32f,
                        cx + (float)Math.Cos(ang) * w * 0.48f, cy + (float)Math.Sin(ang) * h * 0.48f);
                }
                break;
            case NavGlyph.Battery:
                g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.3f, w * 0.72f, h * 0.4f);
                g.DrawLine(pen, x + w * 0.86f, y + h * 0.42f, x + w * 0.86f, y + h * 0.58f);
                g.DrawLines(pen, [new PointF(x + w * 0.42f, y + h * 0.36f), new PointF(x + w * 0.32f, y + h * 0.52f), new PointF(x + w * 0.46f, y + h * 0.52f), new PointF(x + w * 0.36f, y + h * 0.66f)]);
                break;
            case NavGlyph.Display:
                g.DrawRectangle(pen, x + w * 0.1f, y + h * 0.2f, w * 0.8f, h * 0.5f);
                g.DrawLine(pen, x + w * 0.35f, y + h * 0.86f, x + w * 0.65f, y + h * 0.86f);
                g.DrawLine(pen, x + w * 0.5f, y + h * 0.7f, x + w * 0.5f, y + h * 0.86f);
                break;
            case NavGlyph.Perf:
                g.DrawArc(pen, x + w * 0.12f, y + h * 0.2f, w * 0.76f, h * 0.76f, 180, 180);
                g.DrawLine(pen, x + w / 2f, y + h * 0.58f, x + w * 0.72f, y + h * 0.32f);
                break;
            case NavGlyph.Keys:
                g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.28f, w * 0.84f, h * 0.44f);
                for (int d = 0; d < 4; d++) g.DrawLine(pen, x + w * (0.22f + d * 0.18f), y + h * 0.42f, x + w * (0.22f + d * 0.18f), y + h * 0.42f);
                g.DrawLine(pen, x + w * 0.32f, y + h * 0.58f, x + w * 0.68f, y + h * 0.58f);
                break;
            case NavGlyph.About:
                g.DrawEllipse(pen, x + w * 0.15f, y + h * 0.15f, w * 0.7f, h * 0.7f);
                g.DrawLine(pen, x + w / 2f, y + h * 0.45f, x + w / 2f, y + h * 0.68f);
                using (var dot = new SolidBrush(c))
                    g.FillEllipse(dot, x + w / 2f - _ui.Sc(1), y + h * 0.3f, _ui.Sc(2.2f), _ui.Sc(2.2f));
                break;
        }
    }
}
