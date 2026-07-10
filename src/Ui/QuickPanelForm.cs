using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using XiControl.Config;
using XiControl.Localization;
using XiControl.Wmi;

namespace XiControl.Ui;

/// <summary>
/// Интерактивная панель по Mi-кнопке: переключатель режимов (иконки),
/// сегмент заряда 80/100 и крестик. Закрывается по X, Esc и клику вне окна.
/// </summary>
public sealed class QuickPanelForm : Form
{
    private static readonly Color Card = Color.FromArgb(28, 28, 30);
    private static readonly Color Border = Color.FromArgb(70, 70, 74);
    private static readonly Color Cell = Color.FromArgb(42, 42, 45);
    private static readonly Color TextCol = Color.FromArgb(238, 238, 238);
    private static readonly Color DimCol = Color.FromArgb(150, 150, 155);
    private static readonly Color Green = Color.FromArgb(52, 199, 89);
    private static readonly Color Blue = Color.FromArgb(90, 170, 255);
    private static readonly Color Orange = Color.FromArgb(255, 149, 0);

    private static readonly (PerfMode mode, string key, Color accent)[] Modes =
    {
        (PerfMode.Eco,       "mode.eco",   Color.FromArgb(125, 160, 185)), // сизый
        (PerfMode.Quiet,     "mode.quiet", Green),
        (PerfMode.Auto,      "mode.auto",  Blue),
        (PerfMode.Turbo,     "mode.turbo", Orange),
        (PerfMode.FullSpeed, "mode.full",  Color.FromArgb(255, 82, 82)),  // красный

    };

    private readonly MifsClient _mifs;
    private readonly AppConfig _cfg;

    // видимые режимы (Эко/Полная мощность скрываются в Настройках или config.json)
    private (PerfMode mode, string key, Color accent)[] _modes = [];
    private Rectangle[] _modeRects = [];
    private Rectangle _care80, _care100, _close;

    private PerfMode? _mode;
    private bool _care;
    private int _hover = -1; // 0..N-1 режимы, 10=80, 11=100, 12=close

    // единый таймер анимаций (работает, пока панель видна): hover-проявление ячеек
    // (~120 мс на цикл) + время t для живых иконок (стрелка, лист, пламя, звёзды...)
    private const float HoverMs = 120f;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private float[] _hoverT = [];
    private float _gaugeT;

    // шрифты общие на всё время жизни (в OnPaint не создаём)
    private static readonly Font TitleFont = new("Segoe UI Semibold", 11f);
    private static readonly Font LabelFont = new("Segoe UI", 8.5f);
    private static readonly Font CapFont = new("Segoe UI", 9f);
    private static readonly Font PillFont = new("Segoe UI Semibold", 11f);

    /// <summary>Вызывается после смены режима из панели (трей обновляет значок).</summary>
    public Action? Changed;

    public QuickPanelForm(MifsClient mifs, AppConfig cfg)
    {
        _mifs = mifs;
        _cfg = cfg;
        ReloadModes();
        _anim.Tick += (_, _) =>
        {
            _gaugeT += 0.015f;
            StepHoverAnim();
            Invalidate();
        };

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Card;

        _ = Handle; // форсируем хэндл (нужен DeviceDpi и маршалинг)
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    private float S => DeviceDpi / 96f;
    private int Sc(float v) => (int)Math.Round(v * S);

    public void Toggle()
    {
        if (Visible) { Hide(); return; }
        RefreshState();
        DoLayout();
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(wa.Height * 0.58));
        Show();
        Activate();
    }

    private void RefreshState()
    {
        try { _mode = _mifs.GetPerfMode(); } catch { _mode = null; }
        try { _care = _mifs.GetChargeCare(); } catch { _care = _cfg.ChargeCare; }
    }

    /// <summary>Пересобрать набор видимых режимов из конфига (EcoMode/FullSpeedMode).</summary>
    public void ReloadModes()
    {
        _modes = Modes.Where(t =>
            (_cfg.EcoMode || t.mode != PerfMode.Eco) &&
            (_cfg.FullSpeedMode || t.mode != PerfMode.FullSpeed)).ToArray();
        _modeRects = new Rectangle[_modes.Length];
        _hoverT = new float[_modes.Length];
        _hover = -1;
        if (Visible) { RefreshState(); DoLayout(); Invalidate(); }
    }

    /// <summary>Перечитать состояние и перерисовать (режим сменили извне, напр. Mi-кнопкой).</summary>
    public void RefreshUi()
    {
        RefreshState();
        Invalidate();
    }

    private void DoLayout()
    {
        int n = _modes.Length;
        int p = Sc(16), header = Sc(28), cellW = Sc(84), cellH = Sc(94), gap = Sc(8);
        int content = cellW * n + gap * (n - 1);
        int width = content + p * 2;

        int modeY = p + header + Sc(4);
        int capY = modeY + cellH + Sc(12);
        int pillsY = capY + Sc(20);
        int pillsH = Sc(42);
        int height = pillsY + pillsH + p;

        for (int i = 0; i < n; i++)
            _modeRects[i] = new Rectangle(p + i * (cellW + gap), modeY, cellW, cellH);

        int half = (content - gap) / 2;
        _care80 = new Rectangle(p, pillsY, half, pillsH);
        _care100 = new Rectangle(p + half + gap, pillsY, half, pillsH);
        _close = new Rectangle(width - p - Sc(22), p - Sc(2), Sc(22), Sc(22));

        Size = new Size(width, height);
        var old = Region;
        using var rgn = Draw.Rounded(new Rectangle(0, 0, width, height), Sc(18));
        Region = new Region(rgn);
        old?.Dispose(); // присваивание Region не освобождает прежний GDI-хэндл
    }

    // Esc как системный хоткей на время показа: панель открывается из события WMI-клавиши,
    // и Windows может не отдать ей фокус — обычный KeyDown тогда не приходит.
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WM_HOTKEY = 0x0312, HkEscId = 1;
    private const uint VK_ESCAPE = 0x1B;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _gaugeT = 0f;
            _anim.Start();
            if (!RegisterHotKey(Handle, HkEscId, 0, VK_ESCAPE))
                Log.Write("QuickPanel: RegisterHotKey(Esc) не удалась — Esc занят другим приложением");
        }
        else
        {
            _anim.Stop();
            UnregisterHotKey(Handle, HkEscId);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HkEscId) { Hide(); return; }
        base.WndProc(ref m);
    }

    // ---- закрытие ----
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    // ---- ввод ----
    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { if (_hover != -1) { _hover = -1; Invalidate(); } }

    // ведём прогресс каждой ячейки к цели (1 — под курсором, 0 — нет)
    private void StepHoverAnim()
    {
        const float step = 15f / HoverMs;
        for (int i = 0; i < _hoverT.Length; i++)
        {
            float target = _hover == i ? 1f : 0f;
            if (Math.Abs(_hoverT[i] - target) < 0.001f) continue;
            _hoverT[i] = Math.Clamp(_hoverT[i] + Math.Sign(target - _hoverT[i]) * step, 0f, 1f);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h == 12) { Hide(); return; }
        if (h >= 0 && h < _modes.Length)
        {
            try { _mifs.SetPerfMode(_modes[h].mode); } catch { }
            RefreshState();
            Invalidate();
            Changed?.Invoke();
        }
        else if (h == 10 || h == 11)
        {
            bool on = h == 10;
            try { _mifs.SetChargeCare(on); } catch { }
            _cfg.ChargeCare = on; _cfg.Save();
            RefreshState();
            Invalidate();
        }
    }

    private int HitTest(Point pt)
    {
        if (_close.Contains(pt)) return 12;
        for (int i = 0; i < _modes.Length; i++) if (_modeRects[i].Contains(pt)) return i;
        if (_care80.Contains(pt)) return 10;
        if (_care100.Contains(pt)) return 11;
        return -1;
    }

    // ---- отрисовка ----
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        g.Clear(Card);
        using (var pen = new Pen(Border))
        using (var path = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Sc(18)))
            g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Loc.T("panel.title"), TitleFont,
            new Rectangle(Sc(16), Sc(12), Width, Sc(22)), TextCol, TextFormatFlags.Left | TextFormatFlags.Top);

        // крестик
        DrawClose(g, _close, _hover == 12);

        // режимы
        for (int i = 0; i < _modes.Length; i++)
        {
            var r = _modeRects[i];
            bool active = _mode == _modes[i].mode;
            bool hover = _hover == i;
            DrawCell(g, r, active, hover, _modes[i].accent, Sc(10));

            // цветные SVG-иконки: активная — в полный цвет; hover плавно проявляет и подращивает
            float t = _hoverT[i];
            float grow = Sc(40) * 0.08f * t;
            var iconR = new RectangleF(
                r.X + (r.Width - Sc(40) - grow) / 2f, r.Y + Sc(9) - grow / 2f,
                Sc(40) + grow, Sc(40) + grow);
            float op = active ? 1f : 0.45f + 0.55f * t;
            // активная ячейка «живёт» всегда, остальные — по мере наведения
            float k = active ? 1f : t;
            if (_anim.Enabled && k > 0.01f)
                DrawModeIconAnimated(g, _modes[i].mode, iconR, op, k);
            else
                DrawModeIcon(g, _modes[i].mode, iconR, op);

            TextRenderer.DrawText(g, Loc.T(_modes[i].key), LabelFont,
                new Rectangle(r.X + Sc(3), r.Bottom - Sc(38), r.Width - Sc(6), Sc(36)),
                active ? TextCol : DimCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        // заряд
        TextRenderer.DrawText(g, Loc.T("panel.charge"), CapFont,
            new Rectangle(Sc(16), _care80.Y - Sc(20), Width, Sc(18)), DimCol, TextFormatFlags.Left | TextFormatFlags.Top);

        DrawPill(g, _care80, "80%", _care, _hover == 10, Green, PillFont);
        DrawPill(g, _care100, "100%", !_care, _hover == 11, Color.FromArgb(120, 120, 125), PillFont);
    }

    private static void DrawCell(Graphics g, Rectangle r, bool active, bool hover, Color accent, int corner)
    {
        using var bg = new SolidBrush(active ? Blend(Cell, accent, 0.18f) : (hover ? Color.FromArgb(52, 52, 56) : Cell));
        using var path = Draw.Rounded(r, corner);
        g.FillPath(bg, path);
        if (active)
        {
            using var pen = new Pen(accent, 1.6f);
            g.DrawPath(pen, path);
        }
    }

    // при наведении: та же иконка, но живая; k = прогресс hover (амплитуда вкатывается плавно)
    private void DrawModeIconAnimated(Graphics g, PerfMode m, RectangleF r, float opacity, float k)
    {
        switch (m)
        {
            case PerfMode.Eco:       SvgIcons.DrawMoonTwinkle(g, r, _gaugeT, k, opacity); break;
            case PerfMode.Quiet:     SvgIcons.DrawLeafSway(g, r, _gaugeT, k, opacity); break;
            case PerfMode.Auto:      SvgIcons.DrawGauge(g, r, k * OsdForm.SweepAngle(_gaugeT), opacity); break;
            case PerfMode.Turbo:     SvgIcons.DrawBoltPulse(g, r, _gaugeT, k, opacity); break;
            case PerfMode.FullSpeed: SvgIcons.DrawRocket(g, r, _gaugeT, k, opacity); break;
            default:                 DrawModeIcon(g, m, r, opacity); break;
        }
    }

    private static void DrawModeIcon(Graphics g, PerfMode m, RectangleF r, float opacity)
    {
        string name = m switch
        {
            PerfMode.Eco => SvgIcons.PerfEco,
            PerfMode.Quiet => SvgIcons.PerfQuiet,
            PerfMode.Auto => SvgIcons.PerfAuto,
            PerfMode.Turbo => SvgIcons.PerfTurbo,
            PerfMode.FullSpeed => SvgIcons.PerfFull,
            _ => SvgIcons.PerfAuto,
        };
        SvgIcons.Draw(g, name, r, opacity);
    }

    private void DrawPill(Graphics g, Rectangle r, string text, bool active, bool hover, Color accent, Font font)
    {
        Color bg = active ? accent : (hover ? Color.FromArgb(52, 52, 56) : Cell);
        using (var b = new SolidBrush(bg))
        using (var path = Draw.Rounded(r, r.Height / 2f))
            g.FillPath(b, path);

        TextRenderer.DrawText(g, text, font, r,
            active ? Color.White : DimCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawClose(Graphics g, Rectangle r, bool hover)
    {
        if (hover)
        {
            using var b = new SolidBrush(Color.FromArgb(200, 60, 60));
            using var path = Draw.Rounded(r, Sc(5));
            g.FillPath(b, path);
        }
        using var pen = new Pen(hover ? Color.White : DimCol, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float m = r.Width * 0.32f;
        g.DrawLine(pen, r.X + m, r.Y + m, r.Right - m, r.Bottom - m);
        g.DrawLine(pen, r.Right - m, r.Y + m, r.X + m, r.Bottom - m);
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}
