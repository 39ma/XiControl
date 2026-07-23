using Microsoft.Win32;

namespace XiControl.SystemIntegration;

/// <summary>
/// События питания и текущее состояние — развязка guard-ов от статики
/// SystemEvents/SystemInformation (WinForms) ради тестов на фейках.
/// </summary>
public interface IPowerEvents : IDisposable
{
    /// <summary>Resume / Suspend / StatusChange (семантика SystemEvents.PowerModeChanged).</summary>
    event Action<PowerModes>? PowerModeChanged;

    /// <summary>Завершение сеанса (shutdown/restart/logoff) — последний шанс тронуть EC.</summary>
    event Action? SessionEnding;

    /// <summary>true — питание от сети (AC), false — батарея.</summary>
    bool IsOnline { get; }
}

/// <summary>Прод-реализация поверх статических событий WinForms.</summary>
public sealed class SystemPowerEvents : IPowerEvents
{
    public event Action<PowerModes>? PowerModeChanged;
    public event Action? SessionEnding;

    public bool IsOnline =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;

    public SystemPowerEvents()
    {
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionEnding += OnSession;
    }

    private void OnPower(object? s, PowerModeChangedEventArgs e) => PowerModeChanged?.Invoke(e.Mode);
    private void OnSession(object? s, SessionEndingEventArgs e) => SessionEnding?.Invoke();

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.SessionEnding -= OnSession;
    }
}
