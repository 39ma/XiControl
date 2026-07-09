using System.Drawing.Drawing2D;

namespace XiControl.Ui;

public enum OsdKind { Charging, ChargingLimited, OnBattery, Eco, Quiet, Auto, Turbo, Full, CareOn, CareOff, MicOn, MicOff, Backlight, BacklightMid, BacklightOff, BacklightAuto }

/// <summary>
/// OSD-оверлей: тёмная скруглённая карточка по центру с иконкой и текстом,
/// плавно затухает. Не активируется и не перехватывает клики. Авто-размер под текст.
/// </summary>
public sealed class OsdForm : Form
{
    private static readonly Color Card = Color.FromArgb(28, 28, 30);
    private static readonly Color Border = Color.FromArgb(70, 70, 74);
    private static readonly Color TextCol = Color.FromArgb(240, 240, 240);
    private static readonly Color DimCol = Color.FromArgb(150, 150, 155);

    private readonly System.Windows.Forms.Timer _display = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 16 };
    private readonly Font _titleFont = new("Segoe UI Semibold", 12.5f);
    private readonly Font _subFont = new("Segoe UI", 9f);

    private OsdKind _kind;
    private string _title = "";
    private string? _sub;

    // все размеры — через Sc(): на HiDPI шрифты масштабируются системой,
    // и иконка с отступами должны расти вместе с ними
    private int Sc(float v) => (int)Math.Round(v * DeviceDpi / 96f);
    private int IconSize => Sc(64);
    private int PadX => Sc(28);
    private int PadTop => Sc(22);
    private int GapIcon => Sc(10);
    private int GapText => Sc(4);
    private int PadBottom => Sc(18);
    private int MinWidth => Sc(240);
    private int Corner => Sc(18);

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Card;
        Size = new Size(240, 152);

        _display.Tick += (_, _) => { _display.Stop(); _fade.Start(); };
        _fade.Tick += (_, _) =>
        {
            Opacity -= 0.08;
            if (Opacity <= 0.01) { _fade.Stop(); Hide(); }
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var old = Region;
        using var p = Draw.Rounded(new Rectangle(0, 0, Width, Height), Corner);
        Region = new Region(p);
        old?.Dispose(); // присваивание Region не освобождает прежний GDI-хэндл
    }

    private (int w, int h, int titleH, int subH) Measure()
    {
        var t = TextRenderer.MeasureText(_title, _titleFont);
        int subH = 0, subW = 0;
        if (!string.IsNullOrEmpty(_sub))
        {
            var s = TextRenderer.MeasureText(_sub, _subFont);
            subH = s.Height; subW = s.Width;
        }
        int content = Math.Max(IconSize, Math.Max(t.Width, subW));
        int w = Math.Max(MinWidth, content + PadX * 2);
        int h = PadTop + IconSize + GapIcon + t.Height + (subH > 0 ? GapText + subH : 0) + PadBottom;
        return (w, h, t.Height, subH);
    }

    /// <summary>Показать OSD (перезапускает таймер показа).</summary>
    public void Flash(OsdKind kind, string title, string? subtitle = null)
    {
        _kind = kind; _title = title; _sub = subtitle;

        var (w, h, _, _) = Measure();
        Size = new Size(w, h);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (int)(wa.Height * 0.60));

        _display.Stop(); _fade.Stop();
        Opacity = 1.0;
        Invalidate();
        if (!Visible) Show();
        else BringToFront();
        _display.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        g.Clear(Card);
        using (var pen = new Pen(Border))
        using (var path = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Corner))
            g.DrawPath(pen, path);

        var (_, _, titleH, subH) = Measure();
        int y = PadTop;
        DrawIcon(g, _kind, new RectangleF((Width - IconSize) / 2f, y, IconSize, IconSize));
        y += IconSize + GapIcon;

        TextRenderer.DrawText(g, _title, _titleFont,
            new Rectangle(0, y, Width, titleH), TextCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        y += titleH + GapText;

        if (subH > 0)
            TextRenderer.DrawText(g, _sub, _subFont,
                new Rectangle(0, y, Width, subH), DimCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }

    private static void DrawIcon(Graphics g, OsdKind kind, RectangleF r)
    {
        string name = kind switch
        {
            OsdKind.Charging        => SvgIcons.BatteryCharging,
            OsdKind.ChargingLimited => SvgIcons.BatterySaverOn,
            OsdKind.OnBattery       => SvgIcons.BatteryDischarge,
            OsdKind.CareOn          => SvgIcons.BatterySaverOn,
            OsdKind.CareOff         => SvgIcons.BatterySaverOff,
            OsdKind.Eco             => SvgIcons.PerfEco,
            OsdKind.Quiet           => SvgIcons.PerfQuiet,
            OsdKind.Auto            => SvgIcons.PerfAuto,
            OsdKind.Turbo           => SvgIcons.PerfTurbo,
            OsdKind.Full            => SvgIcons.PerfFull,
            OsdKind.MicOn           => SvgIcons.MicOn,
            OsdKind.MicOff          => SvgIcons.MicOff,
            OsdKind.Backlight       => SvgIcons.KeyboardBacklight,
            OsdKind.BacklightMid    => SvgIcons.KeyboardBacklight50,
            OsdKind.BacklightOff    => SvgIcons.KeyboardBacklightOff,
            OsdKind.BacklightAuto   => SvgIcons.KeyboardBacklightAuto,
            _ => SvgIcons.Settings,
        };
        SvgIcons.Draw(g, name, r);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _display.Dispose(); _fade.Dispose();
            _titleFont.Dispose(); _subFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
