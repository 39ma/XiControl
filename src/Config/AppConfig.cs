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
