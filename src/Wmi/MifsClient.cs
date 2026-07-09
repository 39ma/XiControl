using System.Management;

namespace XiControl.Wmi;

/// <summary>Результат вызова MiInterface.</summary>
public sealed class MifsResult
{
    public required bool Ok { get; init; }        // OUT[1] == 0x80
    public required byte[] Out { get; init; }
    public byte Val4 => Out.Length > 4 ? Out[4] : (byte)0;  // значение/эхо (perf mode здесь)
    public byte Val6 => Out.Length > 6 ? Out[6] : (byte)0;  // значение (charge статус здесь)
}

/// <summary>
/// Тонкая обёртка над WMI-методом MiCommonInterface.MiInterface.
/// Требует прав администратора (проверено). Бросает при отсутствии интерфейса.
/// </summary>
public sealed class MifsClient : IDisposable
{
    private readonly ManagementObject _inst;
    private readonly object _lock = new();   // сериализуем вызовы (UI + события питания)

    public MifsClient()
    {
        var scope = new ManagementScope(Mifs.Namespace);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(
            scope, new ObjectQuery($"SELECT * FROM {Mifs.ClassName}"));
        _inst = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"{Mifs.ClassName} не найден.");
    }

    /// <summary>Сырой вызов. op=OpGet/OpSet, cmd/arg/val раскладываются по offset 3/4/6.</summary>
    public MifsResult Invoke(byte op, byte cmd, byte arg = 0, byte val = 0)
    {
        var inData = new byte[Mifs.BufferSize];
        inData[1] = op;
        inData[3] = cmd;
        inData[4] = arg;
        inData[6] = val;

        lock (_lock)
        {
            using var pars = _inst.GetMethodParameters(Mifs.MethodName);
            pars["InData"] = inData;
            using var outParams = _inst.InvokeMethod(Mifs.MethodName, pars, null);
            var outData = outParams?["OutData"] as byte[] ?? Array.Empty<byte>();
            return new MifsResult { Ok = outData.Length > 1 && outData[1] == Mifs.StatusOk, Out = outData };
        }
    }

    public MifsResult Get(byte cmd, byte arg = 0) => Invoke(Mifs.OpGet, cmd, arg);
    public MifsResult Set(byte cmd, byte arg = 0, byte val = 0) => Invoke(Mifs.OpSet, cmd, arg, val);

    // ---- Режим производительности ----

    public PerfMode? GetPerfMode()
    {
        var r = Get(Mifs.CmdPerf);
        return r.Ok ? (PerfMode)r.Val4 : null;
    }

    /// <returns>true, если прошивка приняла режим.</returns>
    public bool SetPerfMode(PerfMode mode)
    {
        if (!Set(Mifs.CmdPerf, (byte)mode).Ok) return false;
        return GetPerfMode() == mode;
    }

    // ---- Защита заряда (беречь ~80% / полный 100%) ----

    public bool GetChargeCare()
    {
        var r = Get(Mifs.CmdCharge, Mifs.ChargeSubEnable);
        return r.Ok && r.Val6 != 0;
    }

    /// <summary>
    /// Включает/выключает «беречь батарею». При включении делает ре-арм (off→on),
    /// чтобы сбросить стейт-машину EC (как в референсе).
    /// </summary>
    public void SetChargeCare(bool care)
    {
        Set(Mifs.CmdCharge, Mifs.ChargeSubEnable, 0);
        if (care)
        {
            Thread.Sleep(80);
            Set(Mifs.CmdCharge, Mifs.ChargeSubEnable, 1);
        }
    }

    public void Dispose() => _inst.Dispose();
}
