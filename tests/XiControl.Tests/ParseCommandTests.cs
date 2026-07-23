using FluentAssertions;
using XiControl.SystemIntegration;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// KeyActions.ParseCommand — разбор командной строки клавиши в (path, args).
/// Чистый шов (KeyActions.cs): File.Exists заменён сидкой, побочный Launch снаружи.
/// </summary>
public sealed class ParseCommandTests
{
    // по умолчанию «файлов не существует» — ветка split по первому пробелу
    private static readonly Func<string, bool> NoFile = _ => false;

    [Fact]
    public void QuotedPath_WithArgs_SplitsAtClosingQuote()
    {
        var (path, args) = KeyActions.ParseCommand("\"C:\\Program Files\\App\\ai.exe\" --go now", NoFile);

        path.Should().Be(@"C:\Program Files\App\ai.exe");
        args.Should().Be("--go now");
    }

    [Fact]
    public void QuotedPath_NoArgs_YieldsNullArgs()
    {
        var (path, args) = KeyActions.ParseCommand("\"C:\\tool.exe\"", NoFile);

        path.Should().Be(@"C:\tool.exe");
        args.Should().BeNull();
    }

    [Fact]
    public void QuotedPath_Unterminated_StripsQuotesAndHasNoArgs()
    {
        var (path, args) = KeyActions.ParseCommand("\"C:\\tool", NoFile);

        path.Should().Be(@"C:\tool");
        args.Should().BeNull();
    }

    [Fact]
    public void Unquoted_WithSpace_NotAFile_SplitsOnFirstSpace()
    {
        var (path, args) = KeyActions.ParseCommand("notepad a b c", NoFile);

        path.Should().Be("notepad");
        args.Should().Be("a b c");   // делим только по первому пробелу
    }

    [Fact]
    public void Unquoted_WithSpace_IsExistingFile_TreatedAsWholePath()
    {
        // legacy AiKeyProgram без кавычек: путь с пробелами, но файл реально существует
        const string full = @"C:\Program Files\app.exe";

        var (path, args) = KeyActions.ParseCommand(full, s => s == full);

        path.Should().Be(full);
        args.Should().BeNull();
    }

    [Fact]
    public void Unquoted_NoSpace_IsWholePath()
    {
        var (path, args) = KeyActions.ParseCommand("notepad", NoFile);

        path.Should().Be("notepad");
        args.Should().BeNull();
    }

    [Fact]
    public void Url_TreatedAsWholePath()
    {
        var (path, args) = KeyActions.ParseCommand("https://example.com/x", NoFile);

        path.Should().Be("https://example.com/x");
        args.Should().BeNull();
    }

    [Fact]
    public void LeadingAndTrailingWhitespace_IsTrimmed()
    {
        var (path, args) = KeyActions.ParseCommand("   notepad foo   ", NoFile);

        path.Should().Be("notepad");
        args.Should().Be("foo");
    }

    [Fact]
    public void QuotedPath_WhitespaceOnlyAfterQuote_YieldsNullArgs()
    {
        var (path, args) = KeyActions.ParseCommand("\"C:\\tool.exe\"    ", NoFile);

        path.Should().Be(@"C:\tool.exe");
        args.Should().BeNull();
    }
}
