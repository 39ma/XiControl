using FluentAssertions;
using XiControl.Config;
using XiControl.Wmi;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// AppConfig.RememberMode — «бережём SSD»: пишем только если RestoreMode включён И
/// значение изменилось (AppConfig.cs:298). Здесь покрыты только guard-ветки с ранним
/// возвратом — они по построению кода НЕ доходят до Save() (диск не трогаем).
/// Ветка «значение изменилось → Save()» ждёт Фазы 1: с IConfigStore запись мокается,
/// а не пишется в реальный %APPDATA%\XiControl\config.json.
/// </summary>
public sealed class RememberModeTests
{
    [Fact]
    public void RestoreModeOff_DoesNotRememberMode()
    {
        var cfg = new AppConfig { RestoreMode = false, StartPerfMode = null };

        cfg.RememberMode(PerfMode.Turbo);

        // опция выключена → ранний возврат до Save, значение не трогаем
        cfg.StartPerfMode.Should().BeNull();
    }

    [Fact]
    public void SameMode_IsNoOp()
    {
        var cfg = new AppConfig { RestoreMode = true, StartPerfMode = PerfMode.Quiet };

        cfg.RememberMode(PerfMode.Quiet);

        // значение не изменилось → ранний возврат до Save
        cfg.StartPerfMode.Should().Be(PerfMode.Quiet);
    }
}
