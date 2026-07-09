using System.Drawing.Drawing2D;

namespace XiControl.Ui;

/// <summary>Общие примитивы рисования.</summary>
public static class Draw
{
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
