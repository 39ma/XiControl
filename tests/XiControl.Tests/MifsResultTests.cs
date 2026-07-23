using FluentAssertions;
using XiControl.Wmi;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// Декодинг ответа MIFS — чистая логика над byte[] (MifsClient.cs:6-12).
/// Val4 — эхо/perf-mode, Val6 — charge-статус; Ok — уже посчитан вызывающим (OUT[1]==0x80).
/// </summary>
public sealed class MifsResultTests
{
    [Fact]
    public void Val4_And_Val6_ReadExpectedByteOffsets()
    {
        var buf = new byte[] { 0x00, 0x80, 0x00, 0x08, 0x02, 0x00, 0x64, 0x00 };
        var r = new MifsResult { Ok = true, Out = buf };

        r.Val4.Should().Be(0x02);   // индекс 4
        r.Val6.Should().Be(0x64);   // индекс 6
        r.Ok.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]   // пустой
    [InlineData(4)]   // ровно до индекса 4 (нет [4])
    [InlineData(6)]   // есть [4], нет [6]
    public void ShortBuffers_DefaultToZero_WithoutThrowing(int length)
    {
        var r = new MifsResult { Ok = false, Out = new byte[length] };

        // короткий ответ прошивки не должен ронять декод — недостающие байты = 0
        if (length <= 4) r.Val4.Should().Be(0);
        if (length <= 6) r.Val6.Should().Be(0);
    }
}
