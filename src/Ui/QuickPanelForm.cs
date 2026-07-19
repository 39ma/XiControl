using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using XiControl.Config;
using XiControl.Localization;
using XiControl.SystemIntegration;
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
    private Rectangle _care80, _care100, _travelCell, _hzCell, _awake, _close, _monBtn;

    private PerfMode? _mode;
    private int _hover = -1; // 0..N-1 режимы, 10=80, 11=100, 12=close, 13=сова, 14=монитор, 15=герцовка, 16=в дорогу

    // единый таймер анимаций (работает, пока панель видна): hover-проявление ячеек
    // (~120 мс на цикл) + время t для живых иконок (стрелка, лист, пламя, звёзды...)
    private const float HoverMs = 120f;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private float[] _hoverT = [];
    private float _gaugeT;

    // шрифты — из кэша ScaledFonts под текущий DPI (в OnPaint не создаём):
    // пропорции с геометрией Sc не разъезжаются после смены разрешения/масштаба
    private Font TitleFont => ScaledFonts.Get(DeviceDpi, "Segoe UI Semibold", 11f);
    private Font LabelFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 8.5f);
    private Font CapFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 9f);
    private Font PillFont => ScaledFonts.Get(DeviceDpi, "Segoe UI Semibold", 11f);

    /// <summary>Вызывается после смены режима из панели (трей обновляет значок).</summary>
    public Action? Changed;

    /// <summary>Кнопка-график слева от крестика: открыть окно «Монитор» (владелец — трей).</summary>
    public Action? MonitorRequested;

    /// <summary>Панель переключила режим «В дорогу» — трей запускает/останавливает наблюдение за 100%.</summary>
    public Action? TravelChanged;

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

        // ряд заряда: [В дорогу] [80%] [100%] … [авто-герцовка] [Не спать]
        int owlW = _cfg.OwlMode ? Sc(56) : 0;
        int hzW = Sc(56);
        int travelW = Sc(46);
        int pillsW = content - travelW - gap - hzW - gap - (_cfg.OwlMode ? owlW + gap : 0);
        int half = (pillsW - gap) / 2;
        _travelCell = new Rectangle(p, pillsY, travelW, pillsH);
        _care80 = new Rectangle(_travelCell.Right + gap, pillsY, half, pillsH);
        _care100 = new Rectangle(_care80.Right + gap, pillsY, half, pillsH);
        _hzCell = new Rectangle(_care100.Right + gap, pillsY, hzW, pillsH);
        _awake = _cfg.OwlMode ? new Rectangle(_hzCell.Right + gap, pillsY, owlW, pillsH) : Rectangle.Empty;
        _close = new Rectangle(width - p - Sc(22), p - Sc(2), Sc(22), Sc(22));
        _monBtn = new Rectangle(_close.X - Sc(28), _close.Y, Sc(22), Sc(22));

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

    // Глобальный хук мыши: закрывать панель по клику вне её габаритов, не полагаясь на
    // активацию окна. OnDeactivate у borderless topmost tool-window ненадёжен (панель не
    // всегда получает фокус — та же причина, по которой Esc сделан через RegisterHotKey),
    // и после наведения/анимации внешний клик переставал её закрывать.
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // POINT (два int) в начале структуры разложены полями ptX/ptY — layout идентичен
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int ptX; public int ptY; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private IntPtr _mouseHook;
    private HookProc? _mouseProc; // держим делегат живым — иначе GC соберёт его и колбэк упадёт

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _gaugeT = 0f;
            _anim.Start();
            if (!RegisterHotKey(Handle, HkEscId, 0, VK_ESCAPE))
                Log.Write("QuickPanel: RegisterHotKey(Esc) не удалась — Esc занят другим приложением");
            InstallMouseHook();
        }
        else
        {
            _anim.Stop();
            UnregisterHotKey(Handle, HkEscId);
            RemoveMouseHook();
        }
    }

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseProc = MouseHookProc; // ссылка в поле — защита от сборки делегата
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
            Log.Write("QuickPanel: SetWindowsHookEx(WH_MOUSE_LL) не удалась — панель закроется только по деактивации");
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
        _mouseProc = null;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var h = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // клик не проглатываем — просто прячем панель, если он вне её габаритов
            if (Visible && !Bounds.Contains(h.ptX, h.ptY))
                BeginInvoke(new Action(Hide));
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
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
        if (h == 14) { MonitorRequested?.Invoke(); return; }
        if (h >= 0 && h < _modes.Length)
        {
            try { _mifs.SetPerfMode(_modes[h].mode); } catch { }
            _cfg.RememberMode(_modes[h].mode);
            RefreshState();
            Invalidate();
            Changed?.Invoke();
        }
        else if (h == 10 || h == 11)
        {
            bool on = h == 10;
            bool wasTravel = _cfg.TravelMode;
            _cfg.TravelMode = false; // явный выбор лимита отменяет «В дорогу»
            try { _mifs.SetChargeCare(on); } catch { }
            _cfg.ChargeCare = on; _cfg.Save();
            RefreshState();
            Invalidate();
            if (wasTravel) TravelChanged?.Invoke(); // трей остановит наблюдение за 100%
        }
        else if (h == 16)
        {
            if (!_cfg.ChargeCare) return; // при постоянном 100% ячейка неактивна
            _cfg.TravelMode = !_cfg.TravelMode;
            // вкл → снять защиту (заряд до 100); выкл → вернуть базовый режим (беречь 80)
            try { _mifs.SetChargeCare(_cfg.TravelMode ? false : _cfg.ChargeCare); } catch { }
            _cfg.Save();
            RefreshState();
            Invalidate();
            TravelChanged?.Invoke();
        }
        else if (h == 13)
        {
            // «Не спать»: экран/сон + крышка на AC (см. AwakeMode)
            if (_cfg.Awake) { AwakeMode.Disable(_cfg); _cfg.Awake = false; }
            else if (AwakeMode.Enable(_cfg)) { _cfg.Awake = true; }
            _cfg.Save();
            Invalidate();
        }
        else if (h == 15)
        {
            // авто-герцовка: вкл — сразу применить частоту по текущему питанию
            _cfg.AutoRefreshRate = !_cfg.AutoRefreshRate;
            _cfg.Save();
            RefreshRate.ApplyForPower(_cfg);
            Invalidate();
        }
    }

    private int HitTest(Point pt)
    {
        if (_close.Contains(pt)) return 12;
        if (_monBtn.Contains(pt)) return 14;
        for (int i = 0; i < _modes.Length; i++) if (_modeRects[i].Contains(pt)) return i;
        if (_travelCell.Contains(pt)) return 16;
        if (_care80.Contains(pt)) return 10;
        if (_care100.Contains(pt)) return 11;
        if (_hzCell.Contains(pt)) return 15;
        if (!_awake.IsEmpty && _awake.Contains(pt)) return 13;
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

        // крестик и кнопка «Монитор» слева от него
        Draw.CloseButton(g, _close, _hover == 12);
        Draw.MonitorButton(g, _monBtn, _hover == 14);

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

        // заряд (заголовок слева) + «Не спать» (заголовок справа, над совой)
        TextRenderer.DrawText(g, Loc.T("panel.charge"), CapFont,
            new Rectangle(Sc(16), _travelCell.Y - Sc(20), Width, Sc(18)), DimCol, TextFormatFlags.Left | TextFormatFlags.Top);

        // «В дорогу»: активна = TravelMode; неактивна (серая), когда базово стоит постоянный 100%.
        // Пилюли 80/100 показывают базовую настройку (ChargeCare), «В дорогу» — временный оверрайд.
        bool travelEnabled = _cfg.ChargeCare;
        DrawCell(g, _travelCell, _cfg.TravelMode, travelEnabled && _hover == 16, Orange, Sc(10));
        float trIcon = Math.Min(_travelCell.Width, _travelCell.Height) - Sc(8);
        float trOp = !travelEnabled ? 0.28f : (_cfg.TravelMode || _hover == 16 ? 1f : 0.6f);
        var trRect = new RectangleF(_travelCell.X + (_travelCell.Width - trIcon) / 2f, _travelCell.Y + (_travelCell.Height - trIcon) / 2f, trIcon, trIcon);
        if (_cfg.TravelMode)
            SvgIcons.DrawTravelPulse(g, trRect, _gaugeT, trOp); // молния мигает, когда режим активен
        else
            SvgIcons.Draw(g, SvgIcons.TravelOff, trRect, trOp);

        DrawPill(g, _care80, "80%", _cfg.ChargeCare, _hover == 10, Green, PillFont);
        DrawPill(g, _care100, "100%", !_cfg.ChargeCare, _hover == 11, Color.FromArgb(120, 120, 125), PillFont);

        // авто-герцовка: монитор с круговыми стрелками, активна при включённой опции
        DrawCell(g, _hzCell, _cfg.AutoRefreshRate, _hover == 15, Blue, Sc(10));
        float hzIcon = Math.Min(_hzCell.Width, _hzCell.Height) - Sc(8);
        SvgIcons.Draw(g,
            _cfg.AutoRefreshRate ? SvgIcons.RefreshRate : SvgIcons.RefreshRateOff,
            new RectangleF(_hzCell.X + (_hzCell.Width - hzIcon) / 2f, _hzCell.Y + (_hzCell.Height - hzIcon) / 2f, hzIcon, hzIcon),
            _cfg.AutoRefreshRate || _hover == 15 ? 1f : 0.6f);

        // сова: ячейка в стиле режимов, бодрая при включённом «Не спать»
        if (!_awake.IsEmpty)
        {
            TextRenderer.DrawText(g, Loc.T("panel.awake"), CapFont,
                new Rectangle(0, _awake.Y - Sc(20), _awake.Right, Sc(18)), DimCol, TextFormatFlags.Right | TextFormatFlags.Top);

            DrawCell(g, _awake, _cfg.Awake, _hover == 13, Blue, Sc(10));
            float owlIcon = Math.Min(_awake.Width, _awake.Height) - Sc(8);
            SvgIcons.Draw(g,
                _cfg.Awake ? SvgIcons.OwlAwake : SvgIcons.OwlAsleep,
                new RectangleF(_awake.X + (_awake.Width - owlIcon) / 2f, _awake.Y + (_awake.Height - owlIcon) / 2f, owlIcon, owlIcon),
                _cfg.Awake || _hover == 13 ? 1f : 0.6f);
        }
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


    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim.Dispose(); RemoveMouseHook(); }
        base.Dispose(disposing);
    }
}
