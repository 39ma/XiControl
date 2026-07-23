using Microsoft.Win32;
using XiControl.Config;

namespace XiControl.SystemIntegration;

/// <summary>
/// Авто-герцовка: держит частоту экрана в соответствии с питанием
/// (сеть → AcRefreshRate, батарея → BatteryRefreshRate), пока опция включена.
/// Срабатывает на смену питания и выход из сна; события идут пачкой — дебаунс.
/// </summary>
public sealed class RefreshRateGuard : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly IPowerEvents _power;
    private readonly System.Windows.Forms.Timer _debounce;

    public RefreshRateGuard(AppConfig cfg, IPowerEvents power)
    {
        _cfg = cfg;
        _power = power;

        _debounce = new System.Windows.Forms.Timer { Interval = 1500 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reapply(); };

        _power.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(PowerModes mode)
    {
        // Resume — выход из сна; StatusChange — смена питания AC↔батарея
        if (mode is PowerModes.Resume or PowerModes.StatusChange)
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }

    /// <summary>Применить частоту по текущему питанию прямо сейчас (старт/включение опции).</summary>
    public void Reapply() => RefreshRate.ApplyForPower(_cfg);

    public void Dispose()
    {
        _power.PowerModeChanged -= OnPowerModeChanged;
        _debounce.Dispose();
    }
}
