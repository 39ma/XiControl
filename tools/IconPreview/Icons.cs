using System.Drawing.Drawing2D;

namespace XiControl.Ui;

/// <summary>
/// Векторные иконки. Единая система: все глифы центрированы, заполняют ~0.8 переданного
/// прямоугольника, толщина линии — общая формула Stroke(). Одинаково при любом размере/DPI.
/// </summary>
public static class Icons
{
    public static readonly Color BatteryStroke = Color.FromArgb(235, 235, 235);

    /// <summary>Единая толщина линии для всех иконок.</summary>
    private static float Stroke(RectangleF r) => Math.Max(1.4f, Math.Min(r.Width, r.Height) * 0.042f);

    private static Pen P(RectangleF r, Color c, float k = 1f) =>
        new(c, Stroke(r) * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

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

    // ---- режимы (появляются вместе в панели → одинаковый калибр ~0.82h) ----

    public static void Bolt(Graphics g, RectangleF r, Color color, bool fill)
    {
        float w = r.Width, h = r.Height, cx = r.X + w / 2, cy = r.Y + h / 2;
        PointF[] p =
        {
            new(cx + 0.10f * w, cy - 0.42f * h),
            new(cx - 0.22f * w, cy + 0.05f * h),
            new(cx - 0.03f * w, cy + 0.05f * h),
            new(cx - 0.10f * w, cy + 0.42f * h),
            new(cx + 0.22f * w, cy - 0.05f * h),
            new(cx + 0.03f * w, cy - 0.05f * h),
        };
        if (fill) { using var b = new SolidBrush(color); g.FillPolygon(b, p); }
        else { using var pen = P(r, color); g.DrawPolygon(pen, p); }
    }

    public static void DoubleBolt(Graphics g, RectangleF r, Color color)
    {
        float bw = r.Width * 0.60f;      // узкая молния
        float off = r.Width * 0.18f;     // разнос центров
        float x = r.X + r.Width / 2 - bw / 2;
        Bolt(g, new RectangleF(x - off, r.Y, bw, r.Height), color, true);
        Bolt(g, new RectangleF(x + off, r.Y, bw, r.Height), color, true);
    }

    public static void Leaf(Graphics g, RectangleF r, Color color)
    {
        float w = r.Width, h = r.Height, cx = r.X + w / 2, cy = r.Y + h / 2;
        using var pen = P(r, color);

        // асимметричный лист: пологая верхняя кромка, пухлое «брюшко» снизу,
        // острый кончик вверху-справа, скруглённое основание внизу-слева
        using var body = new GraphicsPath();
        body.AddBezier(cx + 0.36f * w, cy - 0.32f * h,   // кончик
                       cx + 0.00f * w, cy - 0.30f * h,   // верхняя кромка — пологая
                       cx - 0.34f * w, cy - 0.04f * h,
                       cx - 0.30f * w, cy + 0.30f * h);  // основание
        body.AddBezier(cx - 0.30f * w, cy + 0.30f * h,
                       cx - 0.12f * w, cy + 0.44f * h,   // «брюшко» снизу
                       cx + 0.34f * w, cy + 0.22f * h,
                       cx + 0.36f * w, cy - 0.32f * h);  // назад к кончику
        g.DrawPath(pen, body);

        // изогнутая центральная жилка от кончика к основанию
        using var vein = new GraphicsPath();
        vein.AddBezier(cx + 0.26f * w, cy - 0.22f * h,
                       cx + 0.02f * w, cy - 0.02f * h,
                       cx - 0.12f * w, cy + 0.10f * h,
                       cx - 0.22f * w, cy + 0.24f * h);
        g.DrawPath(pen, vein);
    }

    public static void Gauge(Graphics g, RectangleF r, Color color)
    {
        float size = 0.82f * Math.Min(r.Width, r.Height);
        var rr = new RectangleF(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2 + 0.04f * r.Height, size, size);
        using var pen = P(r, color);
        g.DrawArc(pen, rr, 140, 260);
        float cx = rr.X + rr.Width / 2, cy = rr.Y + rr.Height / 2;
        const double ang = 300 * Math.PI / 180;
        g.DrawLine(pen, cx, cy, cx + (float)(Math.Cos(ang) * rr.Width * 0.36), cy + (float)(Math.Sin(ang) * rr.Width * 0.36));
        using var b = new SolidBrush(color);
        float d = Stroke(r) * 2.4f;
        g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
    }

    // ---- батарея и наложения ----

    public static void Battery(Graphics g, RectangleF a, Color accent, float fill)
    {
        float w = a.Width * 0.82f, h = a.Height * 0.46f;
        var body = new RectangleF(a.X + (a.Width - w) / 2f, a.Y + (a.Height - h) / 2f, w - a.Width * 0.09f, h);
        using var pen = new Pen(BatteryStroke, Stroke(a)) { LineJoin = LineJoin.Round };
        using (var p = Rounded(body, h * 0.16f)) g.DrawPath(pen, p);

        var term = new RectangleF(body.Right + a.Width * 0.02f, body.Y + h * 0.3f, a.Width * 0.05f, h * 0.4f);
        using (var b = new SolidBrush(BatteryStroke)) g.FillRectangle(b, term);

        fill = Math.Clamp(fill, 0f, 1f);
        if (fill > 0)
        {
            float pad = h * 0.16f;
            var inner = new RectangleF(body.X + pad, body.Y + pad, (body.Width - 2 * pad) * fill, body.Height - 2 * pad);
            using var fb = new SolidBrush(accent);
            using var fp = Rounded(inner, pad * 0.6f);
            g.FillPath(fb, fp);
        }
    }

    public static void BoltOverlay(Graphics g, RectangleF a, Color color, Color outline)
    {
        float w = a.Width, h = a.Height, cx = a.X + w / 2, cy = a.Y + h / 2;
        PointF[] p =
        {
            new(cx + 0.05f * w, cy - 0.20f * h), new(cx - 0.12f * w, cy + 0.03f * h), new(cx - 0.017f * w, cy + 0.03f * h),
            new(cx - 0.05f * w, cy + 0.20f * h), new(cx + 0.12f * w, cy - 0.03f * h), new(cx + 0.017f * w, cy - 0.03f * h)
        };
        using (var pen = new Pen(outline, Stroke(a)) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, p);
        using (var b = new SolidBrush(color)) g.FillPolygon(b, p);
    }

    public static void LeafOverlay(Graphics g, RectangleF a, Color outline)
    {
        float w = a.Width, h = a.Height, cx = a.X + w / 2, cy = a.Y + h / 2;
        using var path = new GraphicsPath();
        path.AddBezier(cx - 0.09f * w, cy + 0.11f * h, cx - 0.16f * w, cy - 0.05f * h, cx - 0.03f * w, cy - 0.17f * h, cx + 0.13f * w, cy - 0.14f * h);
        path.AddBezier(cx + 0.13f * w, cy - 0.14f * h, cx + 0.08f * w, cy + 0.03f * h, cx - 0.017f * w, cy + 0.11f * h, cx - 0.09f * w, cy + 0.11f * h);
        using (var pen2 = new Pen(outline, Stroke(a) * 1.3f) { LineJoin = LineJoin.Round }) g.DrawPath(pen2, path);
        using (var pen = new Pen(Color.White, Stroke(a) * 0.9f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
    }

    public static void Slash(Graphics g, RectangleF a, Color red)
    {
        using var pen = new Pen(red, Stroke(a) * 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, a.X + 0.18f * a.Width, a.Y + 0.20f * a.Height, a.Right - 0.18f * a.Width, a.Bottom - 0.20f * a.Height);
    }

    // ---- микрофон ----

    public static void Mic(Graphics g, RectangleF r, Color color, bool muted)
    {
        float w = r.Width, h = r.Height, cx = r.X + w / 2;
        using var pen = P(r, color);
        using var b = new SolidBrush(color);

        float capW = w * 0.28f, capTop = r.Y + h * 0.12f, capH = h * 0.42f;
        var cap = new RectangleF(cx - capW / 2, capTop, capW, capH);
        using (var p = Rounded(cap, capW / 2)) g.FillPath(b, p);

        float arcR = w * 0.24f;
        var arc = new RectangleF(cx - arcR, capTop + capH * 0.28f, arcR * 2, arcR * 2);
        g.DrawArc(pen, arc, 20, 140);

        float baseY = r.Y + h * 0.88f;
        g.DrawLine(pen, cx, arc.Bottom - arcR * 0.2f, cx, baseY);
        g.DrawLine(pen, cx - w * 0.15f, baseY, cx + w * 0.15f, baseY);

        if (muted)
        {
            using var red = new Pen(Color.FromArgb(255, 90, 90), Stroke(r) * 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(red, r.X + 0.14f * w, r.Y + 0.14f * h, r.Right - 0.14f * w, r.Bottom - 0.14f * h);
        }
    }

    // ---- подсветка клавиатуры: клавиатура + «солнышко» с лучами ----

    public static void Keyboard(Graphics g, RectangleF r, Color color)
    {
        float w = r.Width, h = r.Height, cx = r.X + w / 2;
        using var pen = P(r, color);

        // корпус (нижняя половина)
        var body = new RectangleF(cx - w * 0.40f, r.Y + h * 0.52f, w * 0.80f, h * 0.32f);
        using (var p = Rounded(body, h * 0.06f)) g.DrawPath(pen, p);

        // два ряда клавиш — короткие штрихи (без мелких точек)
        float kx0 = body.X + body.Width * 0.16f, kx1 = body.Right - body.Width * 0.16f;
        g.DrawLine(pen, kx0, body.Y + body.Height * 0.36f, kx1, body.Y + body.Height * 0.36f);
        g.DrawLine(pen, body.X + body.Width * 0.30f, body.Y + body.Height * 0.68f, body.Right - body.Width * 0.30f, body.Y + body.Height * 0.68f);

        // «солнышко» — купол над клавиатурой
        float sunR = w * 0.14f, scy = body.Y - h * 0.05f;
        var dome = new RectangleF(cx - sunR, scy - sunR, sunR * 2, sunR * 2);
        g.DrawArc(pen, dome, 180, 180);
        g.DrawLine(pen, cx - sunR, scy, cx + sunR, scy);

        // лучи веером вверх
        float ri = sunR * 1.5f, ro = sunR * 2.3f;
        for (int deg = 205; deg <= 335; deg += 32)
        {
            double a = deg * Math.PI / 180.0;
            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
            g.DrawLine(pen, cx + dx * ri, scy + dy * ri, cx + dx * ro, scy + dy * ro);
        }
    }

    // ---- эмблема «два тумблера» (значок базы, финализирована владельцем) ----

    public static void Toggles(Graphics g, RectangleF r, Color color)
    {
        float w = r.Width, h = r.Height;
        float pillW = w * 0.74f;
        float pillH = h * 0.36f;
        float gap = h * 0.12f;
        float x = r.X + (w - pillW) / 2f;
        float total = pillH * 2 + gap;
        float y0 = r.Y + (h - total) / 2f;

        float stroke = Math.Max(1.3f, pillH / 6f);
        float inset = pillH * 0.20f;
        float thumbD = pillH - 2 * inset;

        using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round };
        using var b = new SolidBrush(color);

        var top = new RectangleF(x, y0, pillW, pillH);
        using (var p = Rounded(top, pillH / 2f)) g.DrawPath(pen, p);
        g.FillEllipse(b, top.Right - inset - thumbD, top.Y + inset, thumbD, thumbD);

        var bot = new RectangleF(x, y0 + pillH + gap, pillW, pillH);
        using (var p = Rounded(bot, pillH / 2f)) g.DrawPath(pen, p);
        g.FillEllipse(b, bot.X + inset, bot.Y + inset, thumbD, thumbD);
    }
}
