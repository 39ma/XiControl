using System.Text.Json;
using XiControl.Localization;

namespace XiControl.Config;

/// <summary>JSON-хранилище конфига: %APPDATA%\XiControl\config.json (или явная папка — для тестов).</summary>
public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _dir;
    private string FilePath => Path.Combine(_dir, "config.json");

    /// <param name="dir">Папка хранения; null — стандартная %APPDATA%\XiControl.</param>
    public JsonConfigStore(string? dir = null) => _dir = dir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XiControl");

    public AppConfig Load()
    {
        AppConfig cfg;
        try
        {
            cfg = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? Fresh()
                : Fresh();
        }
        catch (Exception ex) { Log.Ex("JsonConfigStore.Load", ex); cfg = Fresh(); /* повреждённый конфиг → дефолт */ }
        cfg.MigrateKeyActions();
        cfg.Store = this; // теперь cfg.Save() пишет через этот store
        return cfg;
    }

    public void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(cfg, JsonOpts));
        }
        catch (Exception ex) { Log.Ex("JsonConfigStore.Save", ex); /* не критично */ }
    }

    /// <summary>Конфиг для первого старта (файла нет / повреждён): язык — по языку ОС.</summary>
    private static AppConfig Fresh() => new() { Language = DetectOsLanguage() };

    /// <summary>Язык интерфейса по языку Windows: ru→Ru, zh→Zh, всё остальное (вкл. en)→En.</summary>
    private static Lang DetectOsLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ru" => Lang.Ru,
            "zh" => Lang.Zh,
            _ => Lang.En,
        };
}
