using System.Reflection;
using Svg;

namespace XiControl.Ui;

/// <summary>
/// Иконки из SVG-ассетов (assets/svg, встроены в сборку как ресурсы).
/// Цветные OSD-иконки рендерятся как есть; трейные (currentColor) — с перекраской
/// под тему. Растры кэшируются по (имя × размер × цвет).
/// </summary>
public static class SvgIcons
{
    // OSD (цветные, 128×128)
    public const string BatteryCharging = "battery-charging";
    public const string BatteryDischarge = "battery-discharge";
    public const string BatterySaverOn = "battery-saver-on";
    public const string BatterySaverOff = "battery-saver-off";
    public const string KeyboardBacklight = "keyboard-backlight";
    public const string KeyboardBacklightOff = "keyboard-backlight-off";
    public const string KeyboardBacklight50 = "keyboard-backlight-50";
    public const string KeyboardBacklightAuto = "keyboard-backlight-auto";
    public const string MicOn = "mic-on";
    public const string MicOff = "mic-off";
    public const string PerfAuto = "perf-auto";
    public const string PerfAutoDial = "perf-auto-dial";     // спидометр без стрелки
    public const string PerfAutoNeedle = "perf-auto-needle"; // стрелка, пивот в центре
    public const string PerfEco = "perf-eco";
    public const string PerfFull = "perf-full";
    public const string PerfQuiet = "perf-quiet";
    public const string PerfTurbo = "perf-turbo";
    public const string Settings = "settings";

    // Трей (монохром, currentColor, 24×24)
    public const string TrayPerfEco = "tray-perf-eco";
    public const string TrayPerfFull = "tray-perf-full";
    public const string TrayPerfQuiet = "tray-perf-quiet";
    public const string TrayPerfTurbo = "tray-perf-turbo";
    public const string TraySettings = "tray-settings";

    private static readonly Dictionary<string, string> _sources = new();       // имя → svg-текст
    private static readonly Dictionary<(string, int, int), Bitmap> _bitmaps = new(); // (имя, размер, argb-цвет|0)

    private static string Source(string name)
    {
        if (_sources.TryGetValue(name, out var src)) return src;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("svg." + name + ".svg")
            ?? throw new FileNotFoundException($"SVG-ресурс не найден: {name}");
        using var reader = new StreamReader(stream);
        src = reader.ReadToEnd();
        _sources[name] = src;
        return src;
    }

    /// <summary>Растр цветной иконки (цвета зашиты в SVG). Кэшируется, не Dispose-ить.</summary>
    public static Bitmap Render(string name, int size) => RenderCore(name, size, null);

    /// <summary>Растр монохромной иконки: currentColor → color. Кэшируется, не Dispose-ить.</summary>
    public static Bitmap Render(string name, int size, Color color) => RenderCore(name, size, color);

    private static Bitmap RenderCore(string name, int size, Color? color)
    {
        var key = (name, size, color?.ToArgb() ?? 0);
        if (_bitmaps.TryGetValue(key, out var bmp)) return bmp;

        string text = Source(name);
        if (color is Color c)
            text = text.Replace("currentColor", $"#{c.R:X2}{c.G:X2}{c.B:X2}");

        var doc = SvgDocument.FromSvg<SvgDocument>(text);
        doc.Width = size;
        doc.Height = size;

        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            doc.Draw(g);
        }
        _bitmaps[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Спидометр с поворачиваемой стрелкой: циферблат статично + стрелка под углом
    /// angleDeg (0 = как в исходной иконке) вокруг центра. Для анимации «настройки».
    /// </summary>
    public static void DrawGauge(Graphics g, RectangleF r, float angleDeg, float opacity = 1f)
    {
        Draw(g, PerfAutoDial, r, opacity);

        int size = (int)Math.Round(Math.Min(r.Width, r.Height));
        var needle = Render(PerfAutoNeedle, size);
        var state = g.Save();
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        g.RotateTransform(angleDeg);
        var dest = new Rectangle(-size / 2, -size / 2, size, size);
        if (opacity >= 0.999f)
        {
            g.DrawImage(needle, dest);
        }
        else
        {
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity });
            g.DrawImage(needle, dest, 0, 0, needle.Width, needle.Height, GraphicsUnit.Pixel, attrs);
        }
        g.Restore(state);
    }

    /// <summary>Нарисовать иконку в прямоугольник с заданной непрозрачностью (для неактивных состояний).</summary>
    public static void Draw(Graphics g, string name, RectangleF r, float opacity = 1f)
    {
        int size = (int)Math.Round(Math.Min(r.Width, r.Height));
        var bmp = Render(name, size);
        var dest = new Rectangle((int)(r.X + (r.Width - size) / 2), (int)(r.Y + (r.Height - size) / 2), size, size);

        if (opacity >= 0.999f)
        {
            g.DrawImage(bmp, dest);
            return;
        }
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity });
        g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
    }
}
