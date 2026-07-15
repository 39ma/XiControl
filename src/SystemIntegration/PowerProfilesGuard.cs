using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;
using XiControl.Config;
using XiControl.Wmi;

namespace XiControl.SystemIntegration;

/// <summary>
/// Профили питания: при смене источника (сеть↔батарея) применяет выбранный для него
/// режим производительности (AcPerfMode/BatteryPerfMode) и, если включена память
/// яркости, последнюю яркость экрана этого источника. Яркость запоминается сама —
/// слушаем WmiMonitorBrightnessEvent и пишем значение в слот активного источника.
/// Профили живут в config.json и переживают перезагрузку: Reapply() на старте
/// возвращает и режим (прошивка сбрасывает его на ребуте), и яркость.
/// </summary>
public sealed class PowerProfilesGuard : IDisposable
{
    private readonly MifsClient _mifs;
    private readonly AppConfig _cfg;
    private readonly Control _ui;  // маршалинг WMI-событий (приходят с фонового потока) в UI-поток
    private readonly System.Windows.Forms.Timer _debounce;  // события питания сыпятся пачкой
    private readonly System.Windows.Forms.Timer _saveDelay; // яркость: не писать конфиг на каждый шаг ползунка
    private ManagementEventWatcher? _brightnessWatcher;
    private bool _lastOnline;            // прошлый источник: реагируем только на реальный переход
    private long _muteBrightnessUntil;   // до этого тика (мс) яркость не запоминаем (переход/наш Set)

    private const long TransitionMuteMs = 4000; // покрывает дебаунс + применение + эхо-событие

    /// <summary>Вызывается после применения профиля (трей обновляет значок режима).</summary>
    public Action? Applied;

    public PowerProfilesGuard(MifsClient mifs, AppConfig cfg, Control ui)
    {
        _mifs = mifs;
        _cfg = cfg;
        _ui = ui;

        _debounce = new System.Windows.Forms.Timer { Interval = 1500 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reapply(); };
        _saveDelay = new System.Windows.Forms.Timer { Interval = 2000 };
        _saveDelay.Tick += (_, _) => { _saveDelay.Stop(); _cfg.Save(); };

        _lastOnline = Online;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        StartBrightnessWatcher();
    }

    private static bool Online => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;

    // Экономия заряда активна (SYSTEM_POWER_STATUS.SystemStatusFlag = 1): Windows гасит
    // экран сама — такие изменения яркости не пользовательские, их не запоминаем.
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    private static bool BatterySaverActive => GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // StatusChange сыплется и на каждый процент заряда — профиль применяем только на
        // реальном переходе AC↔батарея (как OSD в TrayApp), иначе он затирал бы ручной
        // выбор режима посреди работы. Resume — безусловно: сон/EC могли сбросить режим.
        bool flip = Online != _lastOnline;
        _lastOnline = Online;
        if (e.Mode is not (PowerModes.Resume or PowerModes.StatusChange)) return;
        if (e.Mode == PowerModes.StatusChange && !flip) return;

        // на переходе Windows сама крутит яркость (план питания/затемнение) — не запоминаем это
        _muteBrightnessUntil = Environment.TickCount64 + TransitionMuteMs;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Применить профиль текущего источника прямо сейчас (старт/смена питания/правка профиля).</summary>
    public void Reapply()
    {
        bool online = Online;

        if ((online ? _cfg.AcPerfMode : _cfg.BatteryPerfMode) is PerfMode mode)
        {
            // недоступный режим (напр. Полная мощность на батарее) → Авто, как при старте
            if (!Safe(() => _mifs.SetPerfMode(mode)))
                Safe(() => _mifs.SetPerfMode(PerfMode.Auto));
            Applied?.Invoke();
        }

        if (_cfg.RememberBrightness && (online ? _cfg.AcBrightness : _cfg.BatteryBrightness) is int pct)
        {
            _muteBrightnessUntil = Environment.TickCount64 + 1500; // эхо собственного Set не запоминаем
            Brightness.Set(pct);
        }
    }

    /// <summary>Запомнить текущую яркость в слот активного источника (при включении опции).</summary>
    public void SeedBrightness()
    {
        if (!_cfg.RememberBrightness || Brightness.Get() is not int pct) return;
        if (Remember(pct)) _cfg.Save();
    }

    private void StartBrightnessWatcher()
    {
        try
        {
            _brightnessWatcher = new ManagementEventWatcher(@"root\wmi",
                "SELECT * FROM WmiMonitorBrightnessEvent");
            _brightnessWatcher.EventArrived += (_, e) =>
            {
                byte pct = (byte)e.NewEvent["Brightness"];
                try { _ui.BeginInvoke(() => OnBrightnessChanged(pct)); }
                catch { /* хэндл уже уничтожен — приложение закрывается */ }
            };
            _brightnessWatcher.Start();
        }
        catch (Exception ex) { Log.Ex("PowerProfiles.BrightnessWatcher", ex); /* панель без WMI-яркости */ }
    }

    // UI-поток: запомнить яркость для активного источника; конфиг пишем с задержкой (бережём SSD).
    // Не запоминаем системные изменения: окно перехода питания/наш Set (mute) и экономию заряда.
    private void OnBrightnessChanged(int pct)
    {
        if (!_cfg.RememberBrightness) return;
        if (Environment.TickCount64 < _muteBrightnessUntil || BatterySaverActive) return;
        if (!Remember(pct)) return;
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private bool Remember(int pct)
    {
        if (Online)
        {
            if (_cfg.AcBrightness == pct) return false;
            _cfg.AcBrightness = pct;
        }
        else
        {
            if (_cfg.BatteryBrightness == pct) return false;
            _cfg.BatteryBrightness = pct;
        }
        return true;
    }

    private static bool Safe(Func<bool> f)
    {
        try { return f(); }
        catch (Exception ex) { Log.Ex("PowerProfiles", ex); return false; }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        try { _brightnessWatcher?.Stop(); } catch { /* уже остановлен */ }
        _brightnessWatcher?.Dispose();
        if (_saveDelay.Enabled) { _saveDelay.Stop(); _cfg.Save(); } // не потерять последнюю яркость
        _debounce.Dispose();
        _saveDelay.Dispose();
    }
}
