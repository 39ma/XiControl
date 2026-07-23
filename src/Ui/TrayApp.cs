using Microsoft.Win32;
using XiControl.Config;
using XiControl.Input;
using XiControl.Localization;
using XiControl.SystemIntegration;
using XiControl.Wmi;

namespace XiControl.Ui;

/// <summary>Иконка в трее + всплывающее меню: заряд, режимы, язык, выход.</summary>
public sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly IMifsClient _mifs;
    private readonly AppConfig _cfg;
    private readonly TouchpadControl _touchpad;
    private readonly TouchscreenControl _touchscreen;
    private bool _dark = Theme.IsDark();
    private readonly ChargeGuard _guard;
    private readonly RefreshRateGuard _hzGuard;
    private readonly PowerProfileGuard _powerGuard;
    private readonly OsdForm _osd = new();
    private readonly IKeyEventSource _events;
    private readonly QuickPanelForm _panel;
    private bool _autoStart;   // кэш состояния автозапуска (не дёргаем schtasks на каждое меню)
    private bool _lastOnline;  // прошлое состояние питания (для OSD только на реальном переходе)

    // политика обновления значка (опрос/кэш/тема) — вынесена в TrayIconController
    private readonly TrayIconController _icon;

    // «В дорогу»: наблюдение за 100% вынесено в TravelChargeMonitor
    private readonly TravelChargeMonitor _travel;

    // Жесты Mi-кнопки и роутинг клавиш — вынесены в Input/ (MiButtonGesture, KeyRouter)
    private readonly MiButtonGesture _mi;
    private readonly KeyRouter _router;

    private static readonly (string key, PerfMode mode)[] Modes =
    [
        ("mode.eco",   PerfMode.Eco),
        ("mode.quiet", PerfMode.Quiet),
        ("mode.auto",  PerfMode.Auto),
        ("mode.turbo", PerfMode.Turbo),
        ("mode.full",  PerfMode.FullSpeed),
    ];

    // видимые режимы (Эко/Полная мощность скрываются в Настройках или config.json)
    private (string key, PerfMode mode)[] _modes = [];
    private PerfMode[] _cycle = []; // порядок цикла Mi-кнопки — по нарастанию мощности

    // Конструктор только сохраняет зависимости и монтирует UI-каркас (меню, значок,
    // панель, подписки). Стартовая бизнес-логика — в Start(), её зовёт Program.Main.
    public TrayApp(IMifsClient mifs, AppConfig cfg, IKeyEventSource events,
        ChargeGuard guard, RefreshRateGuard hzGuard, PowerProfileGuard powerGuard,
        TouchpadControl touchpad, TouchscreenControl touchscreen, TravelChargeMonitor travel,
        TrayIconController icon)
    {
        _mifs = mifs;
        _cfg = cfg;
        _events = events;
        _guard = guard;
        _hzGuard = hzGuard;
        _powerGuard = powerGuard;
        _touchpad = touchpad;
        _touchscreen = touchscreen;
        _travel = travel;
        _icon = icon;
        ApplyModeVisibility();

        _menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9F) };
        _menu.Opening += (_, _) => BuildMenu();
        ApplyMenuTheme();

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.ForMode(null, Theme.TaskbarIsLight()),
            Visible = true,
            Text = Loc.T("app.name"),
            ContextMenuStrip = _menu,
        };
        // контроллер решает «когда перерисовать», мы — «как» (рендер + NotifyIcon)
        _icon.Apply = (mode, light) => { try { _tray.Icon = TrayIcons.ForMode(mode, light); } catch { } };
        // Левый клик тоже открывает меню
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        // Профили питания: после применения режима обновить значок трея
        _powerGuard.ModeApplied = () =>
        {
            if (_osd.IsHandleCreated) _osd.BeginInvoke(new Action(() => _icon.Refresh()));
        };

        // Дозарядились до 100% «в дорогу» → OSD + джингл (сам опрос — в мониторе)
        _travel.Ready = () =>
        {
            _osd.Flash(OsdKind.Travel, Loc.T("osd.travel.ready"));
            if (_cfg.TravelSound) Sound.PlayTravelReady(_cfg.TravelSoundFile);
        };

        // OSD на смену питания
        _ = _osd.Handle; // форсируем создание хэндла для маршалинга событий в UI-поток
        _lastOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        SystemEvents.PowerModeChanged += OnPower;

        // Панель по Mi-кнопке + слушатель клавиш прошивки
        _panel = new QuickPanelForm(_mifs, _cfg, _touchpad, _touchscreen);
        _panel.Changed = () => _icon.Refresh();
        _panel.MonitorRequested = ShowMonitor;
        _panel.TravelChanged = _travel.Rearm; // панель сама переключила режим — перевзвести наблюдение
        // Жесты Mi: одинарный/двойной клик настраиваются (MiClickAction/MiDoubleAction);
        // двойной = "none" — жест отключён, одинарный срабатывает мгновенно; удержание → панель
        _mi = new MiButtonGesture
        {
            Click = () => _router!.Run(_cfg.MiClickAction, _cfg.MiClickCommand),
            DoubleClick = () => _router!.Run(_cfg.MiDoubleAction, _cfg.MiDoubleCommand),
            Hold = () => _panel.Toggle(),
            DoubleEnabled = () => !string.Equals(_cfg.MiDoubleAction, "none", StringComparison.OrdinalIgnoreCase),
        };
        // Роутинг клавиш: исполнители действий — пока методы TrayApp (позже — AppController)
        _router = new KeyRouter(_cfg, _mi)
        {
            CycleModes = CycleMode,
            ToggleCharge = ToggleCharge,
            TogglePanel = _panel.Toggle,
            ToggleOwl = ToggleAwake,
            ToggleMonitor = ToggleMonitor,
            ToggleTravel = () => SetTravel(!_cfg.TravelMode),
            ToggleTouchpad = ToggleTouchpadAction,
            ToggleTouchscreen = ToggleTouchscreenAction,
            Projection = KeyActions.Projection,
            OpenSettings = KeyActions.OpenSettings,
            Copilot = KeyActions.Copilot,
            Launch = KeyActions.LaunchCommand,
            MicKey = OnMicKey,
            BacklightKey = OnBacklightKey,
            FnLockKey = OnFnLockKey,
            PanelVisible = () => _panel.Visible,
        };
        _events.KeyPressed += OnKey;

        // Реакция на смену темы Windows
        SystemEvents.UserPreferenceChanged += OnUserPref;
    }

    /// <summary>
    /// Стартовая бизнес-логика (восстановление режима/заряда/совы, запуск слушателей).
    /// Отдельно от конструктора: граф объектов уже собран, порядок — как до Application.Run.
    /// </summary>
    public void Start()
    {
        // пока показываем состояние из конфига; реальное уточняем в фоне —
        // schtasks /query может блокировать до 10 с, старту это ни к чему
        _autoStart = _cfg.AutoStart;
        Task.Run(() =>
        {
            _autoStart = Safe(AutoStart.IsEnabled, _cfg.AutoStart);
            // самопочинка: после обновления/переноса exe задача указывает на пропавший путь
            // и молча не стартует — пересоздаём на текущий exe
            if (_autoStart) Safe(() => { AutoStart.RepairIfBroken(); return true; }, true);
        });

        // Страж заряда: применить желаемое состояние на старте
        _guard.Reapply();

        // Авто-герцовка: частота экрана по текущему питанию
        _hzGuard.Reapply();

        // «В дорогу»: следим за достижением 100%. Если стартовали посреди режима — на зарядке
        // продолжаем ждать, иначе (уже отключены) сбрасываем: режим живёт только на зарядке.
        if (_cfg.TravelMode)
        {
            if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online) _travel.Rearm();
            else { _cfg.TravelMode = false; _cfg.Save(); }
        }

        // Режим при старте (прошивка сбрасывает его на ребуте):
        //  • PowerProfiles → применить профиль текущего питания (режим + яркость);
        //  • иначе RestoreMode → восстановить последний выбранный (если он ещё видим), иначе Auto;
        //  • иначе, если задан ForceStartMode (только правкой конфига) → принудительно его.
        if (_cfg.PowerProfiles)
        {
            _powerGuard.Reapply();
        }
        else if (_cfg.RestoreMode)
        {
            if (_cfg.StartPerfMode is PerfMode saved)
                ApplyStartMode(_modes.Any(m => m.mode == saved) ? saved : PerfMode.Auto);
        }
        else if (_cfg.ForceStartMode is PerfMode forced)
        {
            ApplyStartMode(forced);
        }

        // «Запоминать яркость» — самостоятельная опция (без профилей): применить яркость
        // текущего питания на старте (при профилях это уже сделал _powerGuard.Reapply выше).
        if (_cfg.RememberBrightness && !_cfg.PowerProfiles) _powerGuard.Reapply();

        // «Режим совы»: восстановить после сбоя, включить заново, либо погасить, если фичу отключили
        if (_cfg.Awake && !_cfg.OwlMode) { AwakeMode.Disable(_cfg); _cfg.Awake = false; _cfg.Save(); }
        else if (_cfg.Awake) { AwakeMode.Enable(_cfg); _cfg.Save(); }
        else if (_cfg.AwakeSavedLidAc is not null) { AwakeMode.Disable(_cfg); _cfg.Save(); }

        // страховка «не залипает»: если тачпад/экран пришлось отключить персистентно,
        // после перезагрузки включаем их сами (в фоне — PnP-вызовы небыстрые)
        if (_cfg.TouchpadPersistOff)
            Task.Run(() => Safe(() => { _touchpad.RestoreAfterBoot(); return true; }, false));
        if (_cfg.TouchscreenPersistOff)
            Task.Run(() => Safe(() => { _touchscreen.RestoreAfterBoot(); return true; }, false));

        // слушатель клавиш прошивки + значок по реальному режиму (и его лёгкий опрос)
        _events.Start();
        _icon.Start();

        // «Прогрев» трей-меню: без него самый первый клик по значку проглатывается —
        // первый показ ContextMenuStrip после старта инициализирует ленивые ресурсы
        // (хэндл меню, первый WMI-вызов в BuildMenu, передний план приложения), и до
        // этого показ закрывается сразу. Делаем это сами на первом холостом ходу цикла
        // сообщений, за экраном и с мгновенным закрытием — пользователь ничего не видит.
        _osd.BeginInvoke(new Action(PrimeMenu));
    }

    private void PrimeMenu()
    {
        try { _menu.Show(new Point(-32000, -32000)); _menu.Close(); }
        catch (Exception ex) { Log.Ex(nameof(PrimeMenu), ex); }
    }

    private void ApplyModeVisibility()
    {
        _modes = Modes.Where(m =>
            (_cfg.EcoMode || m.mode != PerfMode.Eco) &&
            (_cfg.FullSpeedMode || m.mode != PerfMode.FullSpeed)).ToArray();
        _cycle = _modes.Select(m => m.mode).ToArray();
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
        bool dark = Theme.IsDark();
        if (dark != _dark) { _dark = dark; ApplyMenuTheme(); }
        _icon.ThemeChanged(); // перечитает цвет панели задач и перерисует принудительно
    }

    private void OnKey(byte code, byte value)
    {
        if (!_panel.IsHandleCreated) return;
        _panel.BeginInvoke(() => _router.Handle(code, value)); // все события — в UI-поток
    }

    // Действие «тачпад вкл/выкл»: CM-вызовы небыстрые (отключение узла — сотни мс) — в фоне;
    // затем OSD (или обновление панели, если она открыта — там своя ячейка).
    private void ToggleTouchpadAction() => Task.Run(() =>
    {
        bool? on = Safe<bool?>(() => _touchpad.Toggle(), null);
        if (on is not bool b) return;
        _osd.BeginInvoke(new Action(() =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else _osd.Flash(b ? OsdKind.TouchpadOn : OsdKind.TouchpadOff,
                            Loc.T(b ? "osd.touchpad.on" : "osd.touchpad.off"));
        }));
    });

    // Действие «сенсорный экран вкл/выкл» — то же самое, что тачпад, но для дигитайзера экрана.
    private void ToggleTouchscreenAction() => Task.Run(() =>
    {
        bool? on = Safe<bool?>(() => _touchscreen.Toggle(), null);
        if (on is not bool b) return;
        _osd.BeginInvoke(new Action(() =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else _osd.Flash(b ? OsdKind.TouchscreenOn : OsdKind.TouchscreenOff,
                            Loc.T(b ? "osd.touchscreen.on" : "osd.touchscreen.off"));
        }));
    });

    // Переключить лимит заряда на противоположный (OSD/панель — внутри ToggleCare)
    private void ToggleCharge()
        => ToggleCare(!Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare));

    private void OnMicKey(byte value)
    {
        bool mute = value == 0; // лампа горит (0) = замьютить системный микрофон
        using (var mic = new MicControl())
            if (mic.Available) mic.SetMute(mute);
        _osd.Flash(mute ? OsdKind.MicOff : OsdKind.MicOn, Loc.T(mute ? "osd.mic.off" : "osd.mic.on"));
    }

    // Fn-Lock переключает сама прошивка, событие сообщает новое состояние — показываем OSD.
    // value=1 (замок закрыт) = классические F1–F12, мультимедиа отключены (проверено на TM2424).
    private void OnFnLockKey(byte value)
    {
        bool on = value != 0;
        _osd.Flash(on ? OsdKind.FnLockOn : OsdKind.FnLockOff,
                   Loc.T("osd.fnlock"), Loc.T(on ? "osd.fnlock.on" : "osd.fnlock.off"));
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

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.StatusChange or PowerModes.Resume)) return;
        bool online = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        if (online == _lastOnline) return;   // только реальный переход AC↔батарея
        _lastOnline = online;

        // «В дорогу»: отключили зарядник — режим сбрасывается (следующее подключение снова 80%);
        // подключили при активном режиме — заново ждём 100%.
        if (_cfg.TravelMode)
        {
            if (!online) DisableTravel();
            else _travel.Rearm(); // подключили при активном режиме — заново ждём 100%
        }

        if (_osd.IsHandleCreated) _osd.BeginInvoke(() => ShowPowerOsd(online));
        else ShowPowerOsd(online);
    }

    private void ShowPowerOsd(bool online)
    {
        var ps = SystemInformation.PowerStatus;
        float f = ps.BatteryLifePercent;
        string? sub = (f >= 0f && f <= 1f) ? Loc.T("osd.level", (int)Math.Round(f * 100)) : null;

        // авто-герцовка включена — дописываем фактическую частоту (ближайшую поддерживаемую;
        // сам переход сделает RefreshRateGuard после дебаунса)
        if (_cfg.AutoRefreshRate &&
            RefreshRate.Resolve(online ? _cfg.AcRefreshRate : _cfg.BatteryRefreshRate) is int real)
        {
            string hz = Loc.T("osd.hz", real);
            sub = sub is null ? hz : $"{sub} • {hz}";
        }

        if (online)
        {
            // мощность адаптера задаёт бейдж-оверлей качества зарядника и подпись; сам заряд
            // (лимит 80/100 или «В дорогу») — это база иконки. Два независимых измерения.
            // ADPW=0 → не-PD БП, мощность неизвестна (серый «?»); >0 и ниже порога → медленный («!»).
            int watts = Safe(() => _mifs.GetAdapterWatts(), 0);
            var badge = ChargeBadge.None;
            string? note = null;
            if (_cfg.ChargerWattsOsd)
            {
                if (watts == 0) { badge = ChargeBadge.NoPd; note = Loc.T("osd.charger.nopd"); }
                else
                {
                    note = Loc.T("osd.charger.watts", watts);
                    if (_cfg.WeakChargerWatts > 0 && watts < _cfg.WeakChargerWatts) badge = ChargeBadge.Slow;
                }
            }

            if (_cfg.TravelMode)
                _osd.Flash(OsdKind.Travel, Loc.T("osd.travel"), Append(Loc.T("osd.travel.sub"), note), badge);
            else if (_cfg.ChargeCare)
                _osd.Flash(OsdKind.ChargingLimited, Loc.T("osd.charging.limited", Mifs.ChargeThresholdPercent), Append(sub, note), badge);
            else
                _osd.Flash(OsdKind.Charging, Loc.T("osd.charging"), Append(sub, note), badge);
        }
        else
        {
            _osd.Flash(OsdKind.OnBattery, Loc.T("osd.onbattery"), sub);
        }
    }

    // Склейка строк подписи OSD через « • » (любая часть может быть null).
    private static string? Append(string? a, string? b) => a is null ? b : b is null ? a : $"{a} • {b}";

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

        // размер иконок меню под текущий DPI (сейчас иконка только у пункта «Язык»)
        int imgSz = (int)Math.Round(16 * _menu.DeviceDpi / 96.0);
        _menu.ImageScalingSize = new Size(imgSz, imgSz);

        // --- Заряд ---
        bool care = Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare);
        var charge = new ToolStripMenuItem(Loc.T("menu.charge")) { Checked = care };
        // состояние читаем в момент клика: пока меню висело, его мог сменить ChargeGuard
        charge.Click += (_, _) => ToggleCare(!Safe(() => _mifs.GetChargeCare(), _cfg.ChargeCare));
        _menu.Items.Add(charge);

        // «В дорогу»: временный заряд до 100% поверх «беречь 80%» (неактивно при постоянном 100%)
        var travel = new ToolStripMenuItem(Loc.T("menu.travel")) { Checked = _cfg.TravelMode, Enabled = _cfg.ChargeCare };
        travel.Click += (_, _) => SetTravel(!_cfg.TravelMode);
        _menu.Items.Add(travel);

        // --- Режим совы (не спать) — если фича не скрыта конфигом ---
        if (_cfg.OwlMode)
        {
            var owl = new ToolStripMenuItem(Loc.T("menu.owl")) { Checked = _cfg.Awake };
            owl.Click += (_, _) => ToggleAwake();
            _menu.Items.Add(owl);
        }

        // --- Авто-герцовка (частота экрана по питанию) ---
        var hz = new ToolStripMenuItem(Loc.T("menu.hz", _cfg.AcRefreshRate, _cfg.BatteryRefreshRate))
        { Checked = _cfg.AutoRefreshRate };
        hz.Click += (_, _) => ToggleAutoHz(!_cfg.AutoRefreshRate);
        _menu.Items.Add(hz);

        // --- Монитор (Вт / CPU / RAM) ---
        var monitor = new ToolStripMenuItem(Loc.T("menu.monitor")) { Checked = _monitor?.Visible == true };
        monitor.Click += (_, _) => ShowMonitor();
        _menu.Items.Add(monitor);

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

        // --- Настройки (отдельное окно в стиле Win11: все опции по группам) ---
        var settings = new ToolStripMenuItem(Loc.T("menu.settings") + "…");
        settings.Click += (_, _) => OpenSettings();
        _menu.Items.Add(settings);

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

    private SettingsForm? _settings;

    // Открыть окно настроек (лениво создаётся, дальше — из спрятанного состояния).
    private void OpenSettings()
    {
        if (_settings is null || _settings.IsDisposed)
        {
            var act = new SettingsActions
            {
                GetAutoStart = () => _autoStart,
                SetAutoStart = ToggleAutoStart,
                SetLanguage = SetLanguage,
                SetModeVisibility = ToggleModeVisibility,
                SetStartStrategy = SetStartStrategy,
                SetProfileMode = SetProfileMode,
                SetRememberBrightness = SetRememberBrightnessTo,
                SetAutoHz = ToggleAutoHz,
                SetRefreshRates = SetRefreshRates,
                SetOwlFeature = ToggleOwlFeature,
                GetBatteryReport = () =>
                {
                    var r = SystemIntegration.BatteryInfo.Read(); // штатные WMI-классы (циклы, ёмкость, износ)
                    // здоровье WMI нет → падаем на SOH1 из прошивки (MIFS), это тоже оценка ёмкости
                    if (r.HealthPercent is null && Safe(() => _mifs.GetBatteryHealth(), (int?)null) is int soh && soh > 0)
                        r = r with { HealthPercent = soh };
                    return r;
                },
            };
            _settings = new SettingsForm(_cfg, act);
        }
        _settings.Popup();
    }

    // Стратегия режима при старте (radio в окне настроек) → в существующую взаимоисключающую логику.
    private void SetStartStrategy(StartStrategy s)
    {
        switch (s)
        {
            case StartStrategy.None:
                _cfg.RestoreMode = false; _cfg.ForceStartMode = null; _cfg.PowerProfiles = false; _cfg.Save();
                break;
            case StartStrategy.Restore:
                SetStartRestore(true);
                break;
            case StartStrategy.Pin:
                if (_cfg.ForceStartMode is null) PinCurrentStartMode(); // закрепить текущий (Авто закрепить нельзя)
                else { _cfg.RestoreMode = false; _cfg.PowerProfiles = false; _cfg.Save(); }
                break;
            case StartStrategy.Profiles:
                SetPowerProfiles(true);
                break;
        }
    }

    // Частоты авто-герцовки из окна настроек: сохранить и, если режим включён, применить сейчас.
    private void SetRefreshRates(int ac, int batt)
    {
        _cfg.AcRefreshRate = ac;
        _cfg.BatteryRefreshRate = batt;
        _cfg.Save();
        if (_cfg.AutoRefreshRate) _hzGuard.Reapply();
    }

    // Явная установка «запоминать яркость» (окно даёт тумблер, а не переключатель).
    private void SetRememberBrightnessTo(bool on)
    {
        if (_cfg.RememberBrightness != on) ToggleRememberBrightness();
    }

    // Смена языка (из окна настроек): применяется сразу; окно само пересоберёт свои подписи.
    private void SetLanguage(Lang lang)
    {
        _cfg.Language = lang;
        Loc.Current = lang;
        _cfg.Save();
        _tray.Text = Loc.T("app.name");
    }

    private void ToggleCare(bool on)
    {
        // ручная смена лимита заряда отменяет временный режим «В дорогу»
        if (_cfg.TravelMode) { _cfg.TravelMode = false; _travel.Rearm(); }
        Safe(() => { _mifs.SetChargeCare(on); return true; }, false);
        _cfg.ChargeCare = on;
        _cfg.Save();
        if (_panel.Visible)
            _panel.RefreshUi(); // панель открыта: пилюля 80/100 переключается в ней, OSD не нужен
        else
            _osd.Flash(on ? OsdKind.CareOn : OsdKind.CareOff,
                       on ? Loc.T("osd.care.on") : Loc.T("osd.care.off"));
    }

    // «В дорогу» (пункт меню): временный заряд до 100% поверх «беречь 80%».
    // Доступно только при базовом ChargeCare=true (при постоянном 100% смысла нет).
    private void SetTravel(bool on)
    {
        if (on && !_cfg.ChargeCare) return;
        _cfg.TravelMode = on;
        _cfg.Save();
        // on → снять защиту (заряд до 100); off → вернуть базовый режим заряда
        Safe(() => { _mifs.SetChargeCare(on ? false : _cfg.ChargeCare); return true; }, false);
        _travel.Rearm();
        if (_panel.Visible) _panel.RefreshUi();
        else if (on) _osd.Flash(OsdKind.Travel, Loc.T("osd.travel"), Loc.T("osd.travel.sub"));
        else _osd.Flash(OsdKind.TravelOff, Loc.T("osd.travel.off"));
    }

    // Сброс «В дорогу» без OSD (отключили зарядник): ChargeGuard сам вернёт «беречь 80%».
    private void DisableTravel()
    {
        _cfg.TravelMode = false;
        _cfg.Save();
        _travel.Rearm();
        if (_panel.Visible) _panel.RefreshUi();
    }

    private void SetMode(PerfMode mode, string key)
    {
        Safe(() => _mifs.SetPerfMode(mode), false);
        _cfg.RememberMode(mode);
        _osd.Flash(ModeKind(mode), Loc.T(key));
        _icon.Refresh();
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
        _cfg.RememberMode(next);
        if (_panel.Visible)
            _panel.RefreshUi(); // панель открыта: выбор «перелистывается» в ней, OSD не нужен
        else
            _osd.Flash(ModeKind(next), Loc.T(ModeKey(next) ?? "mode.auto"));
        _icon.Refresh();
    }

    private void ToggleModeVisibility(bool eco, bool full)
    {
        _cfg.EcoMode = eco;
        _cfg.FullSpeedMode = full;
        _cfg.Save();
        ApplyModeVisibility();
        _panel.ReloadModes();
    }

    // Применить желаемый стартовый режим; если прошивка не приняла (напр. Full-speed на батарее) — Auto.
    private void ApplyStartMode(PerfMode mode)
    {
        if (!Safe(() => _mifs.SetPerfMode(mode), false))
            Safe(() => _mifs.SetPerfMode(PerfMode.Auto), false);
    }

    // «Восстанавливать последний» (взаимоисключающе с «закрепить»). При первом включении
    // (StartPerfMode ещё пуст) запоминаем текущий режим сразу — чтобы было что восстанавливать;
    // при повторном значение не трогаем, поэтому вернётся всё как было до отключения.
    private void SetStartRestore(bool on)
    {
        _cfg.RestoreMode = on;
        if (on)
        {
            _cfg.ForceStartMode = null;   // включили восстановление — снимаем закреп
            _cfg.PowerProfiles = false;   // …и профили питания (три стратегии взаимоисключающи)
            if (_cfg.StartPerfMode is null)
                _cfg.StartPerfMode = Safe<PerfMode?>(() => _mifs.GetPerfMode(), null);
        }
        _cfg.Save();
    }

    // «Закрепить текущий режим» — переключатель: уже закреплён → снять (обе галки пустые);
    // не закреплён → закрепить текущий (Авто/не прочитался — закреплять нечего). Закрепление
    // взаимоисключающе гасит «восстанавливать последний».
    private void PinCurrentStartMode()
    {
        if (_cfg.ForceStartMode is not null)
        {
            _cfg.ForceStartMode = null; // снять закреп
        }
        else if (Safe<PerfMode?>(() => _mifs.GetPerfMode(), null) is PerfMode m && m != PerfMode.Auto)
        {
            _cfg.ForceStartMode = m;
            _cfg.RestoreMode = false;
            _cfg.PowerProfiles = false; // закрепили режим — профили питания выключаем
        }
        _cfg.Save();
    }

    // «Профили питания» (взаимоисключающе с «восстанавливать»/«закрепить»): включаем —
    // засеваем текущую яркость в слот текущего питания (чтоб было что вспоминать) и применяем.
    private void SetPowerProfiles(bool on)
    {
        _cfg.PowerProfiles = on;
        if (on)
        {
            _cfg.RestoreMode = false;
            _cfg.ForceStartMode = null;
            if (_cfg.RememberBrightness) SeedCurrentBrightness();
        }
        _cfg.Save();
        if (on) _powerGuard.Reapply();
    }

    // Выбор режима профиля (ac=true — сеть, иначе батарея; mode=null — «не менять»).
    // Если это профиль текущего питания — применяем сразу для мгновенной обратной связи.
    private void SetProfileMode(bool ac, PerfMode? mode)
    {
        if (ac) _cfg.AcPerfMode = mode; else _cfg.BatteryPerfMode = mode;
        _cfg.Save();
        bool online = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        if (mode is PerfMode m && ac == online)
        {
            if (!Safe(() => _mifs.SetPerfMode(m), false))
                Safe(() => _mifs.SetPerfMode(PerfMode.Auto), false);
            _icon.Refresh();
        }
    }

    private void ToggleRememberBrightness()
    {
        _cfg.RememberBrightness = !_cfg.RememberBrightness;
        if (_cfg.RememberBrightness) SeedCurrentBrightness();
        _cfg.Save();
    }

    // Запомнить текущую яркость в слот текущего питания (при включении опции — чтобы был старт).
    private void SeedCurrentBrightness()
    {
        if (Brightness.Get() is not int lvl) return;
        if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
            _cfg.AcBrightness = lvl;
        else
            _cfg.BatteryBrightness = lvl;
    }

    // Авто-герцовка: вкл — сразу применить частоту по текущему питанию, выкл — частоту не трогаем
    private void ToggleAutoHz(bool on)
    {
        _cfg.AutoRefreshRate = on;
        _cfg.Save();
        if (on) _hzGuard.Reapply();
        if (_panel.Visible)
            _panel.RefreshUi(); // панель открыта: ячейка герцовки переключается в ней, OSD не нужен
        else if (on)
            _osd.Flash(OsdKind.RefreshRate, Loc.T("osd.hz.on"),
                       Loc.T("osd.hz.on.sub", _cfg.AcRefreshRate, _cfg.BatteryRefreshRate));
        else
            _osd.Flash(OsdKind.RefreshRateOff, Loc.T("osd.hz.off"));
    }

    // Показ/скрытие «режима совы» как фичи; при скрытии активный режим гасится
    private void ToggleOwlFeature(bool on)
    {
        _cfg.OwlMode = on;
        if (!on && _cfg.Awake) { AwakeMode.Disable(_cfg); _cfg.Awake = false; }
        _cfg.Save();
        _panel.ReloadModes(); // перестроить раскладку панели (сова появляется/уходит)
    }

    private MonitorForm? _monitor;

    private void ShowMonitor()
    {
        _monitor ??= new MonitorForm(_cfg, _mifs);
        _monitor.Popup();
    }

    // Для действия клавиши "monitor": повторное нажатие прячет виджет
    private void ToggleMonitor()
    {
        if (_monitor?.Visible == true) _monitor.Hide();
        else ShowMonitor();
    }

    // «Режим совы»: включить/выключить «не спать» (панель обновится, если открыта)
    private void ToggleAwake()
    {
        if (_cfg.Awake) { AwakeMode.Disable(_cfg); _cfg.Awake = false; }
        else if (AwakeMode.Enable(_cfg)) { _cfg.Awake = true; }
        _cfg.Save();
        if (_panel.Visible) _panel.RefreshUi();
    }

    // schtasks может блокировать до 10 с (WaitForExit) — с UI-потока не зовём,
    // иначе окно настроек зависнет на клике по тумблеру
    private void ToggleAutoStart(bool on)
    {
        Task.Run(() =>
        {
            Safe(() => { AutoStart.Set(on); return true; }, false);
            _autoStart = Safe(AutoStart.IsEnabled, on);  // перечитать реальное состояние
            _cfg.AutoStart = _autoStart;
            _cfg.Save();
        });
    }

    private static T Safe<T>(Func<T> f, T fallback,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        try { return f(); }
        catch (Exception ex) { Log.Ex($"TrayApp.{caller}", ex); return fallback; }
    }

    public void Dispose()
    {
        // вернуть действие крышки; сам флаг Awake в конфиге не трогаем —
        // при следующем запуске режим включится снова
        if (_cfg.Awake) { AwakeMode.Disable(_cfg); _cfg.Save(); }

        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.UserPreferenceChanged -= OnUserPref;
        _mi.Dispose();
        // _events / guard-ы / IPowerEvents / IMifsClient диспоузит DI-провайдер
        // (в обратном порядке создания), TrayApp ими не владеет
        _osd.Dispose();
        _panel.Dispose();
        _settings?.Dispose();
        _monitor?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        TrayIcons.DisposeAll();
    }
}
