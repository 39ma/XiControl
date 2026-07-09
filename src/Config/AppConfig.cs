using System.Text.Json;
using System.Text.Json.Serialization;
using XiControl.Localization;

namespace XiControl.Config;

/// <summary>Настройки приложения. Хранятся в %APPDATA%\XiControl\config.json.</summary>
public sealed class AppConfig
{
    public Lang Language { get; set; } = Lang.Ru;
    public bool ChargeCare { get; set; } = false;
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Показывать скрытый режим Эко (0x0A) в меню, панели и цикле Mi-кнопки.
    /// Настраивается только правкой config.json (перезапуск).
    /// </summary>
    public bool EcoMode { get; set; } = true;

    /// <summary>
    /// Показывать режим «Полная мощность» (0x04). false — режим убирается из UI
    /// и включить его из приложения нельзя. Только правкой config.json (перезапуск).
    /// </summary>
    public bool FullSpeedMode { get; set; } = true;

    /// <summary>
    /// Что запускать AI-клавишей: путь к exe/файлу/URL (поддерживаются %ПЕРЕМЕННЫЕ%).
    /// Пусто → Copilot (Win+C). Настраивается только правкой config.json.
    /// </summary>
    public string? AiKeyProgram { get; set; }

    /// <summary>Аргументы командной строки для AiKeyProgram (опционально).</summary>
    public string? AiKeyArgs { get; set; }

    [JsonIgnore]
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XiControl");

    [JsonIgnore]
    private static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig();
        }
        catch (Exception ex) { Log.Ex("AppConfig.Load", ex); /* повреждённый конфиг → дефолт */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex) { Log.Ex("AppConfig.Save", ex); /* не критично */ }
    }
}
