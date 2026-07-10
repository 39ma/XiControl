namespace XiControl.Wmi;

/// <summary>
/// Константы протокола MIFS (Xiaomi/Redmi, ODM Bitland).
/// Расшифровано пробами на TM2424 — см. docs/01-wmi-protocol.md.
/// </summary>
public static class Mifs
{
    // WMI
    public const string Namespace = @"\\.\root\wmi";
    public const string ClassName = "MiCommonInterface";
    public const string MethodName = "MiInterface";
    public const string EventClass = "HID_EVENT20";
    public const int BufferSize = 32;

    // operation (offset 1)
    public const byte OpGet = 0xFA;
    public const byte OpSet = 0xFB;

    // статус ответа (OUT[1])
    public const byte StatusOk = 0x80;   // функция поддерживается
    // 0xE0 = не поддерживается

    // команды (offset 3)
    public const byte CmdPerf = 0x08;    // режим производительности
    public const byte CmdMic = 0x0A;     // микрофон
    public const byte CmdCharge = 0x10;  // защита заряда

    // под-функции заряда (offset 4)
    public const byte ChargeSubEnable = 0x02;  // val 1=беречь(~80%), 0=до 100%
    public const byte ChargeSubFlag = 0x03;    // индикатор зоны заряда

    // Mi-кнопка шлёт пару: 0x25 (нажатие) + 0x26 (отпускание).
    // Короткое нажатие = цикл режимов, долгое (удержание) = панель — логика в TrayApp.
    public const byte KeyMiDown = 0x25;
    public const byte KeyMiUp = 0x26;

    // Коды спец-клавиш HID_EVENT20 на TM2424 (см. docs/07-keymap.md)
    public const byte KeyMic = 0x21;         // микрофон: value 0=mute(лампа горит), 1=unmute
    public const byte KeyKbdBacklight = 0x05; // подсветка: value = уровень (0/5/10/0x80=Авто)
    public const byte KeyProjection = 0x01;   // проекция экрана (value 0)
    public const byte KeySettings = 0x1B;     // клавиша «Настройки»
    public const byte KeyAiDown = 0x23;       // нейропомощник (нажатие)
    public const byte KeyAiUp = 0x24;         // нейропомощник (отпускание)
    public const byte KeyFnLock = 0x07;       // Fn-Lock (Fn+Esc): value = новое состояние 0/1

    /// <summary>Фиксированный порог прошивки для «беречь батарею» на этой модели.</summary>
    public const int ChargeThresholdPercent = 80;
}

/// <summary>
/// Режимы производительности (cmd 0x08). На TM24 доступны все, кроме Balance.
/// </summary>
public enum PerfMode : byte
{
    Balance = 0x01,   // недоступен на TM24
    Quiet = 0x02,
    Turbo = 0x03,
    FullSpeed = 0x04, // требует DC-питания, не USB-C
    Auto = 0x09,      // «умный», только TM24
    Eco = 0x0A,       // скрытый: значение из EC-референса (рег. 0x68), прошивка TM24 принимает
}
