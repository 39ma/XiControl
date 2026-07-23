namespace XiControl.Wmi;

/// <summary>
/// Доступ к WMI-прошивке (MiCommonInterface) — семантическая поверхность без сырых
/// опкодов: они остаются деталью реализации (задел под профили моделей, Фаза 5).
/// Главный шов для тестов: guard-ы и UI мокают этот интерфейс вместо живого железа.
/// </summary>
public interface IMifsClient : IDisposable
{
    /// <summary>Текущий режим производительности; null — прошивка не ответила.</summary>
    PerfMode? GetPerfMode();

    /// <returns>true, если прошивка приняла режим.</returns>
    bool SetPerfMode(PerfMode mode);

    /// <summary>Включена ли защита заряда («беречь ~80%»).</summary>
    bool GetChargeCare();

    /// <summary>Включает/выключает «беречь батарею» (с ре-армом off→on при включении).</summary>
    void SetChargeCare(bool care);

    /// <summary>Мощность адаптера в ваттах; 0 — не подключён или не-PD.</summary>
    int GetAdapterWatts();

    /// <summary>Здоровье батареи (SOH1), % от исходной ёмкости; null — не прочиталось.</summary>
    int? GetBatteryHealth();
}
