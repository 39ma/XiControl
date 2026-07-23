using System.Text.RegularExpressions;
using FluentAssertions;
using XiControl.Localization;
using Xunit;

namespace XiControl.Tests;

/// <summary>
/// Страховка переводов (A.10): гарантирует, что все языки взаимозаменяемы. Контрибьютор
/// добавляет JSON-файл — этот тест ловит забытые/лишние ключи, пустые значения и рассинхрон
/// плейсхолдеров ({0}/{1}) раньше, чем это увидит пользователь. Ключи — единый контракт UI.
/// </summary>
public sealed class LocalizationTests
{
    // культура → (ключ → значение); загружено из встроенных JSON
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> All = Loc.All;

    [Fact]
    public void BaseLanguages_ArePresent()
    {
        All.Keys.Should().Contain(["ru", "en", "zh"]);
        Loc.Available.Should().OnlyContain(l => l.Culture.Length > 0 && l.Name.Length > 0);
    }

    [Fact]
    public void EveryLanguage_HasIdenticalKeySet()
    {
        var reference = new HashSet<string>(All["en"].Keys);
        foreach (var (culture, map) in All)
        {
            var keys = new HashSet<string>(map.Keys);
            keys.Except(reference).Should().BeEmpty($"в «{culture}» есть лишние ключи (нет в en)");
            reference.Except(keys).Should().BeEmpty($"в «{culture}» не хватает ключей (есть в en)");
        }
    }

    [Fact]
    public void NoValue_IsEmpty()
    {
        foreach (var (culture, map) in All)
            foreach (var (key, value) in map)
                value.Should().NotBeNullOrWhiteSpace($"«{culture}»: ключ {key} без перевода");
    }

    [Fact]
    public void Placeholders_AreConsistentAcrossLanguages()
    {
        // для каждого ключа набор индексов {N} должен совпадать во всех языках —
        // иначе перевод с потерянным {0} упадёт в string.Format у пользователя
        foreach (var key in All["en"].Keys)
        {
            var expected = Placeholders(All["en"][key]);
            foreach (var (culture, map) in All)
                Placeholders(map[key]).Should().BeEquivalentTo(expected,
                    $"«{culture}»: у ключа {key} другой набор плейсхолдеров, чем в en");
        }
    }

    [Fact]
    public void ManifestFilename_MatchesDeclaredCulture()
    {
        // lang.<культура>.json → внутри _culture должен совпадать с <культура> в имени файла
        var asm = typeof(Loc).Assembly;
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("lang.", StringComparison.Ordinal) || !res.EndsWith(".json", StringComparison.Ordinal)) continue;
            string culture = res["lang.".Length..^".json".Length];
            All.Keys.Should().Contain(culture, $"файл {res}: _culture внутри не совпадает с именем файла");
        }
    }

    private static HashSet<int> Placeholders(string s)
    {
        var set = new HashSet<int>();
        foreach (Match m in Regex.Matches(s, @"\{(\d+)\}"))
            set.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        return set;
    }
}
