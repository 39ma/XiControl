namespace XiControl.Localization;

public enum Lang { Ru = 0, En = 1, Zh = 2 }

/// <summary>Простая локализация RU/EN/ZH. Ключ → [ru, en, zh].</summary>
public static class Loc
{
    public static Lang Current = Lang.Ru;

    private static readonly Dictionary<string, string[]> S = new()
    {
        ["app.name"]        = ["Xi Control", "Xi Control", "Xi Control"],
        ["menu.charge"]     = ["Беречь батарею (80%)", "Battery care (80%)", "电池保护 (80%)"],
        ["menu.perf"]       = ["Режим производительности", "Performance mode", "性能模式"],
        ["mode.eco"]        = ["Эко", "Eco", "节能"],
        ["mode.quiet"]      = ["Тихий", "Quiet", "静音"],
        ["mode.turbo"]      = ["Турбо", "Turbo", "高性能"],
        ["mode.full"]       = ["Полная мощность", "Full speed", "全速"],
        ["mode.auto"]       = ["Авто", "Auto", "智能"],
        ["menu.language"]   = ["Язык", "Language", "语言"],
        ["menu.autostart"]  = ["Запускать с Windows", "Start with Windows", "开机自启动"],
        ["menu.exit"]       = ["Выход", "Exit", "退出"],

        ["panel.title"]     = ["Быстрые настройки", "Quick settings", "快速设置"],
        ["panel.charge"]    = ["Заряд батареи", "Battery charge", "电池充电"],

        ["osd.charging"]         = ["Зарядка", "Charging", "充电中"],
        ["osd.charging.limited"] = ["Зарядка до {0}%", "Charging to {0}%", "充电至 {0}%"],
        ["osd.onbattery"]        = ["Работа от батареи", "On battery", "电池供电"],
        ["osd.level"]            = ["Заряд {0}%", "Battery {0}%", "电量 {0}%"],
        ["osd.care.on"]          = ["Беречь батарею", "Battery care", "电池保护"],
        ["osd.care.off"]         = ["Заряд до 100%", "Charge to 100%", "充电至 100%"],
        ["osd.mic.on"]           = ["Микрофон включён", "Microphone on", "麦克风已开启"],
        ["osd.mic.off"]          = ["Микрофон выключен", "Microphone off", "麦克风已静音"],
        ["osd.backlight"]        = ["Подсветка клавиатуры", "Keyboard backlight", "键盘背光"],
        ["osd.backlight.level"]  = ["{0}%", "{0}%", "{0}%"],
        ["osd.off"]              = ["Выключено", "Off", "已关闭"],
        ["osd.auto"]             = ["Авто", "Auto", "自动"],

        ["err.title"]   = ["Ошибка", "Error", "错误"],
        ["err.noiface"] = [
            "Интерфейс MIFS не найден. Нужны права администратора и совместимый ноутбук Xiaomi/Redmi.",
            "MIFS interface not found. Requires administrator rights and a compatible Xiaomi/Redmi laptop.",
            "未找到 MIFS 接口。需要管理员权限和兼容的小米/红米（Redmi）笔记本电脑。"],
    };

    public static string T(string key)
        => S.TryGetValue(key, out var v) ? v[(int)Current] : key;

    public static string T(string key, params object[] args)
        => string.Format(T(key), args);
}
