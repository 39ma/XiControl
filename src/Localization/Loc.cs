namespace XiControl.Localization;

public enum Lang { Ru = 0, En = 1 }

/// <summary>Простая локализация RU/EN. Ключ → [ru, en].</summary>
public static class Loc
{
    public static Lang Current = Lang.Ru;

    private static readonly Dictionary<string, string[]> S = new()
    {
        ["app.name"]        = ["Xi Control", "Xi Control"],
        ["menu.charge"]     = ["Беречь батарею (80%)", "Battery care (80%)"],
        ["menu.perf"]       = ["Режим производительности", "Performance mode"],
        ["mode.eco"]        = ["Эко", "Eco"],
        ["mode.quiet"]      = ["Тихий", "Quiet"],
        ["mode.turbo"]      = ["Турбо", "Turbo"],
        ["mode.full"]       = ["Полная мощность", "Full speed"],
        ["mode.auto"]       = ["Авто", "Auto"],
        ["menu.language"]   = ["Язык", "Language"],
        ["menu.autostart"]  = ["Запускать с Windows", "Start with Windows"],
        ["menu.exit"]       = ["Выход", "Exit"],

        ["panel.title"]     = ["Быстрые настройки", "Quick settings"],
        ["panel.charge"]    = ["Заряд батареи", "Battery charge"],

        ["osd.charging"]         = ["Зарядка", "Charging"],
        ["osd.charging.limited"] = ["Зарядка до {0}%", "Charging to {0}%"],
        ["osd.onbattery"]        = ["Работа от батареи", "On battery"],
        ["osd.level"]            = ["Заряд {0}%", "Battery {0}%"],
        ["osd.care.on"]          = ["Беречь батарею", "Battery care"],
        ["osd.care.off"]         = ["Заряд до 100%", "Charge to 100%"],
        ["osd.mic.on"]           = ["Микрофон включён", "Microphone on"],
        ["osd.mic.off"]          = ["Микрофон выключен", "Microphone off"],
        ["osd.backlight"]        = ["Подсветка клавиатуры", "Keyboard backlight"],
        ["osd.backlight.level"]  = ["{0}%", "{0}%"],
        ["osd.off"]              = ["Выключено", "Off"],
        ["osd.auto"]             = ["Авто", "Auto"],

        ["err.title"]   = ["Ошибка", "Error"],
        ["err.noiface"] = [
            "Интерфейс MIFS не найден. Нужны права администратора и совместимый ноутбук Xiaomi/Redmi.",
            "MIFS interface not found. Requires administrator rights and a compatible Xiaomi/Redmi laptop."],
    };

    public static string T(string key)
        => S.TryGetValue(key, out var v) ? v[(int)Current] : key;

    public static string T(string key, params object[] args)
        => string.Format(T(key), args);
}
