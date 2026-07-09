using Microsoft.Win32;
using XiControl.Config;
using XiControl.Localization;
using XiControl.SystemIntegration;
using XiControl.Wmi;

namespace XiControl.Ui;

/// <summary>Иконка в трее + всплывающее меню: заряд, режимы, язык, выход.</summary>
public sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly MifsClient _mifs;
    private readonly AppConfig _cfg;
    private bool _dark = Theme.IsDark();
    private bool _lightTaskbar = Theme.TaskbarIsLight();
    private readonly ChargeGuard _guard;
    private readonly OsdForm _osd = new();
    private readonly MifsEventWatcher _events = new();
    private readonly QuickPanelForm _panel;
    private bool _autoStart;   // кэш состояния автозапуска (не дёргаем schtasks на каждое меню)
    private bool _lastOnline;  // прошлое состояние питания (для OSD только на реальном переходе)

    // редкий опрос: режим извне меняется только сном/EC, свои изменения обновляют значок сразу
    private readonly System.Windows.Forms.Timer _iconTimer = new() { Interval = 30000 };
    private PerfMode? _iconMode;
    private bool _iconInit;

    private readonly System.Windows.Forms.Timer _miHold = new() { Interval = 400 }; // порог «долгого» нажатия Mi
    private bool _miHandled;

    private static readonly (string key, PerfMode mode)[] Modes =
    [
        ("mode.eco",   PerfMode.Eco),
        ("mode.quiet", PerfMode.Quiet),
        ("mode.auto",  PerfMode.Auto),
        ("mode.turbo", PerfMode.Turbo),
        ("mode.full",  PerfMode.FullSpeed),
    ];

    // видимые режимы (Эко можно скрыть через config.json: "EcoMode": false)
    private readonly (string key, PerfMode mode)[] _modes;
    private readonly PerfMode[] _cycle; // порядок цикла Mi-кнопки — по нарастанию мощности

    public TrayApp(MifsClient mifs, AppConfig cfg)
    {
        _mifs = mifs;
        _cfg = cfg;
        _modes = cfg.EcoMode ? Modes : Modes.Where(m => m.mode != PerfMode.Eco).ToArray();
        _cycle = _modes.Select(m => m.mode).ToArray();

        // пока показываем состояние из конфига; реальное уточняем в фоне —
        // schtasks /query может блокировать до 10 с, старту это ни к чему
        _autoStart = cfg.AutoStart;
        Task.Run(() => _autoStart = Safe(AutoStart.IsEnabled, cfg.AutoStart));

        _menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9F) };
        _menu.Opening += (_, _) => BuildMenu();
        ApplyMenuTheme();

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.ForMode(null, _lightTaskbar),
            Visible = true,
            Text = Loc.T("app.name"),
            ContextMenuStrip = _menu,
        };
        // Левый клик тоже открывает меню
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        // Страж заряда: применяет желаемое состояние на старте и после сна/смены питания
        _guard = new ChargeGuard(_mifs, () => _cfg.ChargeCare);
        _guard.Reapply();

        // OSD на смену питания
        _ = _osd.Handle; // форсируем создание хэндла для маршалинга событий в UI-поток
        _lastOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        SystemEvents.PowerModeChanged += OnPower;

        // Панель по Mi-кнопке + слушатель клавиш прошивки
        _panel = new QuickPanelForm(_mifs, _cfg);
        _panel.Changed = () => UpdateTrayIcon();
        _miHold.Tick += (_, _) => OnMiHold();
        _events.KeyPressed += OnKey;
        _events.Start();

        // Значок трея по режиму + реакция на смену темы Windows
        SystemEvents.UserPreferenceChanged += OnUserPref;
        UpdateTrayIcon();

        // Лёгкий опрос: держим значок в соответствии с реальным режимом (любой источник)
        _iconTimer.Tick += (_, _) => UpdateTrayIcon();
        _iconTimer.Start();
    }

    private void UpdateTrayIcon(bool force = false)
    {
        var mode = Safe<PerfMode?>(() => _mifs.GetPerfMode(), null);
        if (!force && _iconInit && mode == _iconMode) return; // без изменений — не трогаем
        _iconInit = true;
        _iconMode = mode;
        try { _tray.Icon = TrayIcons.ForMode(mode, _lightTaskbar); } catch { }
    }

    private void ApplyMenuTheme()
    {
        if (_dark)
        {
            ToolStripManager.Renderer = new DarkMenuRenderer();
            _menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            _menu.BackColor = DarkPalette.Bg;
            _menu.ForeColor = DarkPalette.Text;
        }
        else
        {
            _menu.RenderMode = ToolStripRenderMode.System;
            _menu.ResetBackColor();
            _menu.ResetForeColor();
        }
    }

    private void OnUserPref(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _lightTaskbar = Theme.TaskbarIsLight();
        bool dark = Theme.IsDark();
        if (dark != _dark) { _dark = dark; ApplyMenuTheme(); }
        UpdateTrayIcon(force: true); // тема сменилась — перерисовать даже если режим тот же
    }

    private void OnKey(byte code, byte value)
    {
        if (!_panel.IsHandleCreated) return;
        _panel.BeginInvoke(() => HandleKey(code, value)); // все события — в UI-поток
    }

    private void HandleKey(byte code, byte value)
    {
        switch (code)
        {
            case Mifs.KeyMiDown: OnMiDown(); break;
            case Mifs.KeyMiUp: OnMiUp(); break;
            case Mifs.KeyProjection when value == 0: KeyActions.Projection(); break; // value 2 = слабый зарядник — пока пропуск
            case Mifs.KeySettings: OnSettingsKey(); break; // одиночное событие, удержание не ловится
            case Mifs.KeyAiDown: OnAiKey(); break;                                   // 0x24 (отпускание) игнорируем
            case Mifs.KeyMic: OnMicKey(value); break;
            case Mifs.KeyKbdBacklight: OnBacklightKey(value); break;
        }
    }

    // Клавиша «Настройки»: переключение лимита заряда 80% ↔ 100% (+OSD внутри ToggleCare)
    private void OnSettingsKey()
        => ToggleCare(!Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare));

    // AI-клавиша: своя программа из config.json (AiKeyProgram/AiKeyArgs), иначе Copilot
    private void OnAiKey()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.AiKeyProgram))
            KeyActions.Launch(_cfg.AiKeyProgram, _cfg.AiKeyArgs);
        else
            KeyActions.Copilot();
    }

    private void OnMicKey(byte value)
    {
        bool mute = value == 0; // лампа горит (0) = замьютить системный микрофон
        using (var mic = new MicControl())
            if (mic.Available) mic.SetMute(mute);
        _osd.Flash(mute ? OsdKind.MicOff : OsdKind.MicOn, Loc.T(mute ? "osd.mic.off" : "osd.mic.on"));
    }

    private void OnBacklightKey(byte value)
    {
        string sub = value switch
        {
            0x00 => Loc.T("osd.off"),
            0x80 => Loc.T("osd.auto"),
            _ => Loc.T("osd.backlight.level", value * 10), // уровни 0–10 → проценты (5→50%, 10→100%)
        };
        var kind = value switch
        {
            0x00 => OsdKind.BacklightOff,
            0x80 => OsdKind.BacklightAuto,
            <= 5 => OsdKind.BacklightMid,
            _ => OsdKind.Backlight,
        };
        _osd.Flash(kind, Loc.T("osd.backlight"), sub);
    }

    private void OnMiDown()
    {
        _miHandled = false;
        _miHold.Stop();
        _miHold.Start();
    }

    private void OnMiUp()
    {
        _miHold.Stop();
        if (_miHandled) return;
        _miHandled = true;
        CycleMode();           // короткое нажатие → следующий режим + OSD
    }

    private void OnMiHold()
    {
        _miHold.Stop();
        if (_miHandled) return;
        _miHandled = true;
        _panel.Toggle();       // удержание → панель
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.StatusChange or PowerModes.Resume)) return;
        bool online = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        if (online == _lastOnline) return;   // только реальный переход AC↔батарея
        _lastOnline = online;
        if (_osd.IsHandleCreated) _osd.BeginInvoke(() => ShowPowerOsd(online));
        else ShowPowerOsd(online);
    }

    private void ShowPowerOsd(bool online)
    {
        var ps = SystemInformation.PowerStatus;
        float f = ps.BatteryLifePercent;
        string? sub = (f >= 0f && f <= 1f) ? Loc.T("osd.level", (int)Math.Round(f * 100)) : null;

        if (online)
        {
            if (_cfg.ChargeCare)
                _osd.Flash(OsdKind.ChargingLimited, Loc.T("osd.charging.limited", Mifs.ChargeThresholdPercent), sub);
            else
                _osd.Flash(OsdKind.Charging, Loc.T("osd.charging"), sub);
        }
        else
        {
            _osd.Flash(OsdKind.OnBattery, Loc.T("osd.onbattery"), sub);
        }
    }

    private void ShowMenu()
    {
        // приватный метод показа меню трея (правильная позиция + авто-закрытие);
        // BuildMenu вызовется сам через событие Opening
        try
        {
            var mi = _tray.GetType().GetMethod("ShowContextMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi != null) { mi.Invoke(_tray, null); return; }
        }
        catch (Exception ex) { Log.Ex(nameof(ShowMenu), ex); }
        _menu.Show(Cursor.Position); // запасной путь, если приватный API исчезнет из WinForms
    }

    private void BuildMenu()
    {
        // Clear не диспозит старые пункты — освобождаем сами
        var stale = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var it in stale) it.Dispose();

        // --- Заряд ---
        bool care = Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare);
        var charge = new ToolStripMenuItem(Loc.T("menu.charge")) { Checked = care };
        // состояние читаем в момент клика: пока меню висело, его мог сменить ChargeGuard
        charge.Click += (_, _) => ToggleCare(!Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare));
        _menu.Items.Add(charge);

        // --- Режим (подменю) ---
        PerfMode? current = Safe<PerfMode?>(() => _mifs.GetPerfMode(), null);
        string currentName = current is PerfMode cm && ModeKey(cm) is string mk ? Loc.T(mk) : "—";
        var perf = new ToolStripMenuItem($"{Loc.T("menu.perf")}:  {currentName}");
        foreach (var (key, mode) in _modes)
        {
            var item = new ToolStripMenuItem(Loc.T(key)) { Checked = current == mode };
            item.Click += (_, _) => SetMode(mode, key);
            perf.DropDownItems.Add(item);
        }
        TintDropDown(perf);
        _menu.Items.Add(perf);

        _menu.Items.Add(new ToolStripSeparator());

        // --- Язык (подменю) ---
        var lang = new ToolStripMenuItem(Loc.T("menu.language"));
        lang.DropDownItems.Add(LangItem("Русский", Lang.Ru));
        lang.DropDownItems.Add(LangItem("English", Lang.En));
        lang.DropDownItems.Add(LangItem("中文", Lang.Zh));
        TintDropDown(lang);
        _menu.Items.Add(lang);

        // --- Автозапуск ---
        var autostart = new ToolStripMenuItem(Loc.T("menu.autostart")) { Checked = _autoStart };
        autostart.Click += (_, _) => ToggleAutoStart(!_autoStart);
        _menu.Items.Add(autostart);

        _menu.Items.Add(new ToolStripSeparator());

        // --- Выход ---
        var exit = new ToolStripMenuItem(Loc.T("menu.exit"));
        exit.Click += (_, _) => { _tray.Visible = false; Application.Exit(); };
        _menu.Items.Add(exit);
    }

    private void TintDropDown(ToolStripMenuItem parent)
    {
        if (!_dark) return;
        parent.DropDown.BackColor = DarkPalette.Bg;
        parent.DropDown.ForeColor = DarkPalette.Text;
    }

    private static string? ModeKey(PerfMode m) => m switch
    {
        PerfMode.Eco => "mode.eco",
        PerfMode.Quiet => "mode.quiet",
        PerfMode.Auto => "mode.auto",
        PerfMode.Turbo => "mode.turbo",
        PerfMode.FullSpeed => "mode.full",
        _ => null,
    };

    private ToolStripMenuItem LangItem(string title, Lang lang)
    {
        var item = new ToolStripMenuItem(title) { Checked = _cfg.Language == lang };
        item.Click += (_, _) =>
        {
            _cfg.Language = lang;
            Loc.Current = lang;
            _cfg.Save();
            _tray.Text = Loc.T("app.name");
        };
        return item;
    }

    private void ToggleCare(bool on)
    {
        Safe(() => { _mifs.SetChargeCare(on); return true; }, false);
        _cfg.ChargeCare = on;
        _cfg.Save();
        if (_panel.Visible)
            _panel.RefreshUi(); // панель открыта: пилюля 80/100 переключается в ней, OSD не нужен
        else
            _osd.Flash(on ? OsdKind.CareOn : OsdKind.CareOff,
                       on ? Loc.T("osd.care.on") : Loc.T("osd.care.off"));
    }

    private void SetMode(PerfMode mode, string key)
    {
        Safe(() => _mifs.SetPerfMode(mode), false);
        _osd.Flash(ModeKind(mode), Loc.T(key));
        UpdateTrayIcon();
    }

    private static OsdKind ModeKind(PerfMode m) => m switch
    {
        PerfMode.Eco => OsdKind.Eco,
        PerfMode.Quiet => OsdKind.Quiet,
        PerfMode.Auto => OsdKind.Auto,
        PerfMode.Turbo => OsdKind.Turbo,
        PerfMode.FullSpeed => OsdKind.Full,
        _ => OsdKind.Auto,
    };

    /// <summary>Переключить на следующий режим по кругу + OSD (для Fn+Mi / хоткея).</summary>
    private void CycleMode()
    {
        var cur = Safe<PerfMode?>(() => _mifs.GetPerfMode(), null) ?? PerfMode.Auto;
        int idx = Array.IndexOf(_cycle, cur);
        var next = _cycle[(idx < 0 ? 0 : idx + 1) % _cycle.Length];
        Safe(() => _mifs.SetPerfMode(next), false);
        if (_panel.Visible)
            _panel.RefreshUi(); // панель открыта: выбор «перелистывается» в ней, OSD не нужен
        else
            _osd.Flash(ModeKind(next), Loc.T(ModeKey(next) ?? "mode.auto"));
        UpdateTrayIcon();
    }

    private void ToggleAutoStart(bool on)
    {
        Safe(() => { AutoStart.Set(on); return true; }, false);
        _autoStart = Safe(AutoStart.IsEnabled, on);  // перечитать реальное состояние
        _cfg.AutoStart = _autoStart;
        _cfg.Save();
    }

    private static T Safe<T>(Func<T> f, T fallback,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        try { return f(); }
        catch (Exception ex) { Log.Ex($"TrayApp.{caller}", ex); return fallback; }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.UserPreferenceChanged -= OnUserPref;
        _iconTimer.Dispose();
        _miHold.Dispose();
        _events.Dispose();
        _guard.Dispose();
        _osd.Dispose();
        _panel.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        TrayIcons.DisposeAll();
    }
}
