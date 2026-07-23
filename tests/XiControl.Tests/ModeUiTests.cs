using FluentAssertions;
using XiControl.Ui;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// Полнота UI-маппинга режимов: панель и меню строятся из AppController.AllModes через ModeUi —
/// новый режим без ключа/акцента молча отрисуется «как Авто», тест это ловит.
/// </summary>
public sealed class ModeUiTests
{
    [Fact]
    public void EveryMode_HasDistinctKeyAndAccent()
    {
        foreach (var m in AppController.AllModes)
            ModeUi.Key(m).Should().NotBeNull($"режим {m} должен иметь ключ локализации");

        AppController.AllModes.Select(ModeUi.Key).Should().OnlyHaveUniqueItems();
        AppController.AllModes.Select(ModeUi.Accent).Should().OnlyHaveUniqueItems();
        AppController.AllModes.Select(ModeUi.Kind).Should().OnlyHaveUniqueItems();
    }
}
