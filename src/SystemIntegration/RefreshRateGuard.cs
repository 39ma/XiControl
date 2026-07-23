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
    private readonly IAppTimer _debounce;

    public RefreshRateGuard(AppConfig cfg, IPowerEvents power, IAppTimer? debounce = null)
    {
        _cfg = cfg;
        _power = power;

        _debounce = debounce ?? new UiTimer();
        _debounce.Interval = 1500;
        _debounce.Tick += () => { _debounce.Stop(); Reapply(); };

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
