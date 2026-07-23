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
    private readonly TrayMenuBuilder _menu;
    private readonly IMifsClient _mifs;
    private readonly AppConfig _cfg;
    private readonly OsdForm _osd = new();
    private readonly IKeyEventSource _events;
    private readonly QuickPanelForm _panel;
    private bool _lastOnline;  // прошлое состояние питания (для OSD только на реальном переходе)

    // командный слой: все Set*/Toggle* и стартовая логика — в AppController
    private readonly AppController _controller;

    // политика обновления значка (опрос/кэш/тема) — вынесена в TrayIconController
    private readonly TrayIconController _icon;

    // «В дорогу»: наблюдение за 100% вынесено в TravelChargeMonitor
    private readonly TravelChargeMonitor _travel;

    // Жесты Mi-кнопки и роутинг клавиш — вынесены в Input/ (MiButtonGesture, KeyRouter)
    private readonly MiButtonGesture _mi;
    private readonly KeyRouter _router;

    // Конструктор только сохраняет зависимости и монтирует UI-каркас (меню, значок,
    // панель, подписки, колбэки контроллера). Стартовая бизнес-логика — в Start().
    public TrayApp(IMifsClient mifs, AppConfig cfg, IKeyEventSource events,
        PowerProfileGuard powerGuard, TouchpadControl touchpad, TouchscreenControl touchscreen,
        TravelChargeMonitor travel, TrayIconController icon, AppController controller)
    {
        _mifs = mifs;
        _cfg = cfg;
        _events = events;
        _travel = travel;
        _icon = icon;
        _controller = controller;

        // меню трея: построение/тема/показ — в TrayMenuBuilder, окна и выход — наши колбэки
        _menu = new TrayMenuBuilder(cfg, mifs, controller)
        {
            ShowMonitor = ShowMonitor,
            OpenSettings = OpenSettings,
            ExitRequested = () => { _tray!.Visible = false; Application.Exit(); },
            MonitorVisible = () => _monitor?.Visible == true,
        };

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.ForMode(null, Theme.TaskbarIsLight()),
            Visible = true,
            Text = Loc.T("app.name"),
            ContextMenuStrip = _menu.Menu,
        };
        // контроллер решает «когда перерисовать», мы — «как» (рендер + NotifyIcon)
        _icon.Apply = (mode, light) => { try { _tray.Icon = TrayIcons.ForMode(mode, light); } catch { } };
        // Левый клик тоже открывает меню
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) _menu.Show(_tray); };

        // Профили питания: после применения режима обновить значок трея
        powerGuard.ModeApplied = () =>
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
        _panel = new QuickPanelForm(_mifs, _cfg, touchpad, touchscreen);
        _panel.Changed = () => _icon.Refresh();
        _panel.MonitorRequested = ShowMonitor;
        _panel.TravelChanged = _travel.Rearm; // панель сама переключила режим — перевзвести наблюдение

        // Уведомления контроллера → обратная связь UI: панель открыта — обновляется она,
        // иначе OSD; значок обновляем после смены режима. Сама логика — в AppController.
        _controller.CareChanged = on =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else _osd.Flash(on ? OsdKind.CareOn : OsdKind.CareOff,
                            on ? Loc.T("osd.care.on") : Loc.T("osd.care.off"));
        };
        _controller.TravelChanged = on =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else if (on) _osd.Flash(OsdKind.Travel, Loc.T("osd.travel"), Loc.T("osd.travel.sub"));
            else _osd.Flash(OsdKind.TravelOff, Loc.T("osd.travel.off"));
        };
        _controller.TravelCancelled = () => { if (_panel.Visible) _panel.RefreshUi(); };
        _controller.ModeSet = m =>
        {
            _osd.Flash(ModeUi.Kind(m), Loc.T(ModeUi.Key(m) ?? "mode.auto"));
            _icon.Refresh();
        };
        _controller.ModeCycled = m =>
        {
            if (_panel.Visible) _panel.RefreshUi(); // выбор «перелистывается» в панели, OSD не нужен
            else _osd.Flash(ModeUi.Kind(m), Loc.T(ModeUi.Key(m) ?? "mode.auto"));
            _icon.Refresh();
        };
        _controller.ProfileModeApplied = () => _icon.Refresh();
        _controller.ModesReloaded = _panel.ReloadModes;
        _controller.AutoHzChanged = on =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else if (on) _osd.Flash(OsdKind.RefreshRate, Loc.T("osd.hz.on"),
                                    Loc.T("osd.hz.on.sub", _cfg.AcRefreshRate, _cfg.BatteryRefreshRate));
            else _osd.Flash(OsdKind.RefreshRateOff, Loc.T("osd.hz.off"));
        };
        _controller.OwlFeatureChanged = _panel.ReloadModes; // сова появляется/уходит из раскладки
        _controller.AwakeChanged = () => { if (_panel.Visible) _panel.RefreshUi(); };
        _controller.LanguageChanged = () => _tray.Text = Loc.T("app.name");
        // тачпад/экран: колбэк приходит с фонового потока — маршалим в UI
        _controller.TouchpadToggled = b => _osd.BeginInvoke(new Action(() =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else _osd.Flash(b ? OsdKind.TouchpadOn : OsdKind.TouchpadOff,
                            Loc.T(b ? "osd.touchpad.on" : "osd.touchpad.off"));
        }));
        _controller.TouchscreenToggled = b => _osd.BeginInvoke(new Action(() =>
        {
            if (_panel.Visible) _panel.RefreshUi();
            else _osd.Flash(b ? OsdKind.TouchscreenOn : OsdKind.TouchscreenOff,
                            Loc.T(b ? "osd.touchscreen.on" : "osd.touchscreen.off"));
        }));
        // Жесты Mi: одинарный/двойной клик настраиваются (MiClickAction/MiDoubleAction);
        // двойной = "none" — жест отключён, одинарный срабатывает мгновенно; удержание → панель
        _mi = new MiButtonGesture
        {
            Click = () => _router!.Run(_cfg.MiClickAction, _cfg.MiClickCommand),
            DoubleClick = () => _router!.Run(_cfg.MiDoubleAction, _cfg.MiDoubleCommand),
            Hold = () => _panel.Toggle(),
            DoubleEnabled = () => !string.Equals(_cfg.MiDoubleAction, "none", StringComparison.OrdinalIgnoreCase),
        };
        // Роутинг клавиш: исполнители действий — командный слой (окна/панель — наши)
        _router = new KeyRouter(_cfg, _mi)
        {
            CycleModes = _controller.CycleMode,
            ToggleCharge = _controller.ToggleCharge,
            TogglePanel = _panel.Toggle,
            ToggleOwl = _controller.ToggleAwake,
            ToggleMonitor = ToggleMonitor,
            ToggleTravel = () => _controller.SetTravel(!_cfg.TravelMode),
            ToggleTouchpad = _controller.ToggleTouchpad,
            ToggleTouchscreen = _controller.ToggleTouchscreen,
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
    /// Старт приложения: бизнес-логика — в контроллере, здесь только запуск слушателей
    /// и прогрев UI. Порядок — как до Application.Run.
    /// </summary>
    public void Start()
    {
        _controller.Startup();

        // слушатель клавиш прошивки + значок по реальному режиму (и его лёгкий опрос)
        _events.Start();
        _icon.Start();

        // «Прогрев» трей-меню: без него самый первый клик по значку проглатывается —
        // первый показ ContextMenuStrip после старта инициализирует ленивые ресурсы
        // (хэндл меню, первый WMI-вызов в BuildMenu, передний план приложения), и до
        // этого показ закрывается сразу. Делаем это сами на первом холостом ходу цикла
        // сообщений, за экраном и с мгновенным закрытием — пользователь ничего не видит.
        _osd.BeginInvoke(new Action(_menu.Prime));
    }

    private void OnUserPref(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _menu.ThemeChanged(); // перекрасить меню, если тема реально сменилась
        _icon.ThemeChanged(); // перечитать цвет панели задач и перерисовать значок
    }

    private void OnKey(byte code, byte value)
    {
        if (!_panel.IsHandleCreated) return;
        _panel.BeginInvoke(() => _router.Handle(code, value)); // все события — в UI-поток
    }

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
            if (!online) _controller.DisableTravel();
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

    private SettingsForm? _settings;

    // Открыть окно настроек (лениво создаётся, дальше — из спрятанного состояния).
    private void OpenSettings()
    {
        if (_settings is null || _settings.IsDisposed)
        {
            var act = new SettingsActions
            {
                GetAutoStart = () => _controller.AutoStartEnabled,
                SetAutoStart = _controller.ToggleAutoStart,
                SetLanguage = _controller.SetLanguage,
                SetModeVisibility = _controller.ToggleModeVisibility,
                SetStartStrategy = _controller.SetStartStrategy,
                SetProfileMode = _controller.SetProfileMode,
                SetRememberBrightness = _controller.SetRememberBrightness,
                SetAutoHz = _controller.ToggleAutoHz,
                SetRefreshRates = _controller.SetRefreshRates,
                SetOwlFeature = _controller.ToggleOwlFeature,
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

    private static T Safe<T>(Func<T> f, T fallback,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        try { return f(); }
        catch (Exception ex) { Log.Ex($"TrayApp.{caller}", ex); return fallback; }
    }

    public void Dispose()
    {
        _controller.Shutdown(); // вернуть действие крышки (сова)

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
