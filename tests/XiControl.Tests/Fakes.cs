using Microsoft.Win32;
using XiControl.SystemIntegration;
using XiControl.Wmi;

namespace XiControl.Tests;

/// <summary>Фейк прошивки: пишет вызовы, отвечает настроенными значениями.</summary>
internal sealed class FakeMifsClient : IMifsClient
{
    public readonly List<bool> ChargeCareCalls = [];
    public readonly List<PerfMode> PerfModeCalls = [];
    public bool SetPerfModeResult = true;
    public PerfMode? Mode;               // что вернёт GetPerfMode
    public bool ThrowOnGetPerfMode;      // симуляция недоступного железа
    public bool ThrowOnSetChargeCare;    // симуляция отказа прошивки на запись

    /// <summary>Сигнал «SetPerfMode вызван» — для ожидания асинхронных Apply (Task.Run в guard-ах).</summary>
    public readonly SemaphoreSlim PerfModeHit = new(0);

    public PerfMode? GetPerfMode() =>
        ThrowOnGetPerfMode ? throw new InvalidOperationException("нет железа") : Mode;

    public bool SetPerfMode(PerfMode mode)
    {
        PerfModeCalls.Add(mode);
        PerfModeHit.Release();
        return SetPerfModeResult;
    }

    public bool GetChargeCare() => false;

    public void SetChargeCare(bool care)
    {
        if (ThrowOnSetChargeCare) throw new InvalidOperationException("прошивка не ответила");
        ChargeCareCalls.Add(care);
    }

    public int GetAdapterWatts() => 0;
    public int? GetBatteryHealth() => null;
    public void Dispose() { }
}

/// <summary>Фейк питания: события поднимаются вручную, IsOnline настраивается.</summary>
internal sealed class FakePowerEvents : IPowerEvents
{
    public event Action<PowerModes>? PowerModeChanged;
    public event Action? SessionEnding;

    public bool IsOnline { get; set; } = true;
    public float BatteryLifePercent { get; set; } = 0.5f;

    public void RaisePower(PowerModes mode) => PowerModeChanged?.Invoke(mode);
    public void RaiseSession() => SessionEnding?.Invoke();
    public void Dispose() { }
}

/// <summary>Фейк таймера: Fire() тикает вручную (только если запущен — как настоящий).</summary>
internal sealed class FakeTimer : IAppTimer
{
    public bool Running { get; private set; }
    public int Interval { get; set; }

    public event Action? Tick;

    public void Start() => Running = true;
    public void Stop() => Running = false;

    public void Fire()
    {
        if (Running) Tick?.Invoke();
    }

    public void Dispose() { }
}
