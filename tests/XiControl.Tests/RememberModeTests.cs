using FluentAssertions;
using XiControl.Config;
using XiControl.Wmi;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// AppConfig.RememberMode — «бережём SSD»: пишем только если RestoreMode включён И
/// значение изменилось. Persist-ветка проверяется фейковым IConfigStore — диск не трогаем.
/// </summary>
public sealed class RememberModeTests
{
    /// <summary>Store-шпион: считает вызовы Save вместо записи на диск.</summary>
    private sealed class SpyStore : IConfigStore
    {
        public int Saves;
        public AppConfig Load() => throw new NotSupportedException();
        public void Save(AppConfig cfg) => Saves++;
    }

    [Fact]
    public void ChangedMode_IsRememberedAndSaved()
    {
        var spy = new SpyStore();
        var cfg = new AppConfig { RestoreMode = true, StartPerfMode = PerfMode.Quiet, Store = spy };

        cfg.RememberMode(PerfMode.Turbo);

        cfg.StartPerfMode.Should().Be(PerfMode.Turbo);
        spy.Saves.Should().Be(1);
    }

    [Fact]
    public void GuardBranches_DoNotSave()
    {
        var spy = new SpyStore();
        var off = new AppConfig { RestoreMode = false, Store = spy };
        var same = new AppConfig { RestoreMode = true, StartPerfMode = PerfMode.Quiet, Store = spy };

        off.RememberMode(PerfMode.Turbo);
        same.RememberMode(PerfMode.Quiet);

        spy.Saves.Should().Be(0);
    }

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
