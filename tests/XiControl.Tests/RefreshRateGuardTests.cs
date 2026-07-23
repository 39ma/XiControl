using FluentAssertions;
using Microsoft.Win32;
using XiControl.Config;
using XiControl.SystemIntegration;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// RefreshRateGuard — только дебаунс-логика: сам Apply (живой ChangeDisplaySettings)
/// юнитами не покрываем (план 3.3). AutoRefreshRate выключен → тик безопасно no-op.
/// </summary>
public sealed class RefreshRateGuardTests
{
    private readonly FakePowerEvents _power = new();
    private readonly FakeTimer _timer = new();

    [Theory]
    [InlineData(PowerModes.StatusChange)]
    [InlineData(PowerModes.Resume)]
    public void PowerChange_ArmsDebounce(PowerModes mode)
    {
        var cfg = new AppConfig { AutoRefreshRate = false };
        using var guard = new RefreshRateGuard(cfg, _power, _timer);

        _power.RaisePower(mode);

        _timer.Running.Should().BeTrue();
        _timer.Fire(); // AutoRefreshRate=false → ApplyForPower выходит сразу, экран не трогаем
        _timer.Running.Should().BeFalse();
    }

    [Fact]
    public void Suspend_DoesNotArmDebounce()
    {
        var cfg = new AppConfig { AutoRefreshRate = false };
        using var guard = new RefreshRateGuard(cfg, _power, _timer);

        _power.RaisePower(PowerModes.Suspend);

        _timer.Running.Should().BeFalse();
    }

    [Fact]
    public void Dispose_UnsubscribesFromPowerEvents()
    {
        var cfg = new AppConfig { AutoRefreshRate = false };
        var guard = new RefreshRateGuard(cfg, _power, _timer);
        guard.Dispose();

        _power.RaisePower(PowerModes.StatusChange);

        _timer.Running.Should().BeFalse();
    }
}
