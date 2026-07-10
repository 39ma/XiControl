using System.Drawing.Drawing2D;

namespace XiControl.Ui;

/// <summary>Общие примитивы рисования.</summary>
public static class Draw
{
    /// <summary>Крестик закрытия окна (единый для панели/монитора): hover — красная плашка.</summary>
    public static void CloseButton(Graphics g, Rectangle r, bool hover)
    {
        if (hover)
        {
            using var b = new SolidBrush(Color.FromArgb(200, 60, 60));
            using var path = Rounded(r, r.Width * 0.23f);
            g.FillPath(b, path);
        }
        using var pen = new Pen(hover ? Color.White : Color.FromArgb(150, 150, 155), 1.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        float m = r.Width * 0.32f;
        g.DrawLine(pen, r.X + m, r.Y + m, r.Right - m, r.Bottom - m);
        g.DrawLine(pen, r.Right - m, r.Y + m, r.X + m, r.Bottom - m);
    }

    /// <summary>Скруглённый прямоугольник (путь надо Dispose-ить).</summary>
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Max(1f, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
