using System.Runtime.InteropServices;
using System.Text;
using XiControl.Config;

namespace XiControl.SystemIntegration;

/// <summary>
/// Включение/выключение тачпада — штатные user-mode SetupAPI/CfgMgr32, без драйверов
/// (нужны права администратора — они у нас есть). Прошивочный путь мёртв: WMI 0x0C (TPLock)
/// на TM2424 к железу не подключён.
///
/// Тачпад ищется по HID-коллекции Precision Touchpad (Usage Page 0x0D, Usage 0x05; признак
/// бывает и в hardware, и в compatible ids), но отключается её РОДИТЕЛЬ (узел «HID на шине
/// I2C»): у PTP две коллекции (мышь + панель), и отключение одной лишь панели оставило бы
/// курсор живым через мышиную. ID родителя запоминается в конфиге: у выключенного тачпада
/// HID-коллекции исчезают из системы, и после перезапуска приложения искать больше нечем.
///
/// Отключение двухступенчатое. Сначала «мягкое» CM_Disable_DevNode без PERSIST (после
/// перезагрузки тачпад вернётся сам) — но это query-remove, который другой софт с открытым
/// хэндлом может заветировать (на части моделей так и происходит). Тогда фолбэк — путь
/// Диспетчера устройств: SetupAPI DIF_PROPERTYCHANGE/DICS_DISABLE (персистентный, вето не
/// подвержен) + флаг TouchpadPersistOff в конфиге, по которому приложение на старте само
/// включает тачпад обратно — обещание «не залипает выключенным» сохраняется.
/// Каждый шаг логируется — по log.txt с чужой машины видно, где именно не срослось.
/// </summary>
public sealed class TouchpadControl(AppConfig cfg)
{
    private const string CompatId = "HID_DEVICE_UP:000D_U:0005"; // Precision Touchpad TLC
    private const uint ProbDisabled = 22;                        // CM_PROB_DISABLED
    private const int CrNoSuchDevnode = 0x0D;                    // CR_NO_SUCH_DEVNODE
    private const uint LocatePhantom = 0x1;                      // CM_LOCATE_DEVNODE_PHANTOM
    private const uint DisableUiNotOk = 0x4;                     // CM_DISABLE_UI_NOT_OK

    private readonly AppConfig _cfg = cfg;
    private uint? _devInst;      // кэш узла-родителя (валиден в сессии, в т.ч. фантомный)
    private bool _loggedMissing; // «не нашли» пишем в лог один раз, не спамим

    /// <summary>Тачпад найден (сейчас или по запомненному ID)?</summary>
    public bool Available => Find() is not null;

    /// <summary>true/false — состояние узла; null — тачпад не найден.
    /// Узел, удалённый мягким отключением, считается выключенным.</summary>
    public bool? IsEnabled()
    {
        if (Find() is not uint inst) return null;
        int cr = CM_Get_DevNode_Status(out _, out uint problem, inst, 0);
        if (cr == CrNoSuchDevnode) return false; // узел убран query-remove'ом — тачпад выключен
        if (cr != 0) return null;
        return problem != ProbDisabled;
    }

    /// <summary>Переключить. Возвращает новое состояние, null — не нашли/не вышло.</summary>
    public bool? Toggle()
    {
        if (IsEnabled() is not bool on) return null;
        bool ok = on ? Disable() : Enable();
        return ok ? !on : null;
    }

    /// <summary>
    /// Страховка на старте приложения: если в прошлый раз пришлось отключать персистентно,
    /// после перезагрузки включаем тачпад сами (мягкое отключение возвращается без нас).
    /// </summary>
    public void RestoreAfterBoot()
    {
        if (!_cfg.TouchpadPersistOff) return;
        Log.Write("Touchpad: включаю после перезагрузки (осталось персистентное отключение)");
        Enable();
    }

    // ---- Отключение: мягкое (не-persist), затем путь Диспетчера устройств ----

    private bool Disable()
    {
        if (Find() is not uint inst) return false;

        int cr = CM_Disable_DevNode(inst, DisableUiNotOk);
        if (cr != 0) Log.Write($"Touchpad: CM_Disable_DevNode → CR 0x{cr:X}");
        if (WaitState(enabled: false)) return true;

        // мягкое отключение заветировано/не сработало — отключаем как Диспетчер устройств
        // (персистентно; на старте приложения RestoreAfterBoot вернёт тачпад)
        Log.Write("Touchpad: мягкое отключение не сработало — пробую персистентное (SetupAPI)");
        if (_cfg.TouchpadDeviceId is not string id || !SetupDiChangeState(id, DICS_DISABLE))
            return false;
        if (!WaitState(enabled: false)) return false;
        _cfg.TouchpadPersistOff = true;
        _cfg.Save();
        return true;
    }

    // ---- Включение: CM_Enable → SetupAPI → полный rescan PnP, по нарастающей ----

    private bool Enable()
    {
        bool ok = false;
        if (Find() is uint inst)
        {
            int cr = CM_Enable_DevNode(inst, 0);
            if (cr != 0) Log.Write($"Touchpad: CM_Enable_DevNode → CR 0x{cr:X}");
            ok = WaitState(enabled: true);
        }
        if (!ok && _cfg.TouchpadDeviceId is string id)
        {
            Log.Write("Touchpad: CM_Enable не помог — пробую SetupAPI (DICS_ENABLE)");
            SetupDiChangeState(id, DICS_ENABLE);
            ok = WaitState(enabled: true);
        }
        if (!ok)
        {
            // узел убран query-remove'ом — вернёт только пересканирование шины
            Log.Write("Touchpad: включаю пересканированием устройств (как «Обновить конфигурацию»)");
            if (CM_Locate_DevNode(out uint root, null, 0) == 0) CM_Reenumerate_DevNode(root, 0);
            ok = WaitState(enabled: true);
        }
        if (ok && _cfg.TouchpadPersistOff) { _cfg.TouchpadPersistOff = false; _cfg.Save(); }
        return ok;
    }

    // PnP-операции асинхронны — даём устройству до ~1.5 с прийти в целевое состояние.
    // Для «выключен» удалённый узел (IsEnabled == null после потери кэша) тоже успех.
    private bool WaitState(bool enabled)
    {
        for (int i = 0; ; i++)
        {
            var st = IsEnabled();
            if (enabled ? st == true : st != true) return true;
            if (i >= 6) return false;
            Thread.Sleep(250);
        }
    }

    // ---- Поиск узла-родителя ----

    // Через живую HID-коллекцию; иначе — по запомненному ID (обычный или фантомный узел).
    private uint? Find()
    {
        if (_devInst is uint cached) return cached;
        try
        {
            if (FindViaHid() is uint inst) { _devInst = inst; return inst; }
            if (!string.IsNullOrEmpty(_cfg.TouchpadDeviceId))
            {
                if (CM_Locate_DevNode(out uint located, _cfg.TouchpadDeviceId, 0) == 0 ||
                    CM_Locate_DevNode(out located, _cfg.TouchpadDeviceId, LocatePhantom) == 0)
                {
                    _devInst = located;
                    return located;
                }
            }
            if (!_loggedMissing)
            {
                _loggedMissing = true;
                Log.Write("Touchpad: PTP-коллекция (UP:000D_U:0005) не найдена — " +
                          "вероятно, тачпад не Precision (другой стек драйвера) или отсутствует");
            }
        }
        catch (Exception ex) { Log.Ex("TouchpadControl.Find", ex); }
        return null;
    }

    private uint? FindViaHid()
    {
        const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        const uint SPDRP_HARDWAREID = 0x1, SPDRP_COMPATIBLEIDS = 0x2;

        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "HID", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1)) return null;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var buf = new byte[4096];
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                // признак HID_DEVICE_UP:000D_U:0005 встречается и в hardware ids (Bitland
                // кладёт его именно туда, compatible ids пусты), и в compatible — смотрим оба
                bool match = false;
                foreach (uint prop in (uint[])[SPDRP_HARDWAREID, SPDRP_COMPATIBLEIDS])
                {
                    if (!SetupDiGetDeviceRegistryProperty(set, ref info, prop,
                            out _, buf, (uint)buf.Length, out uint size)) continue;
                    if (Encoding.Unicode.GetString(buf, 0, (int)size)
                        .Contains(CompatId, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                }
                if (!match) continue;

                if (CM_Get_Parent(out uint parent, info.DevInst, 0) != 0) continue;

                // запомнить ID родителя — иначе после перезапуска выключенный тачпад не найти
                var sb = new StringBuilder(256);
                if (CM_Get_Device_ID(parent, sb, sb.Capacity, 0) == 0)
                {
                    string id = sb.ToString();
                    if (!string.Equals(_cfg.TouchpadDeviceId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _cfg.TouchpadDeviceId = id;
                        _cfg.Save();
                        Log.Write($"Touchpad: найден узел {id}");
                    }
                }
                return parent;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    // ---- Путь Диспетчера устройств: DIF_PROPERTYCHANGE / DICS_* (персистентный) ----

    private const uint DICS_ENABLE = 1, DICS_DISABLE = 2, DICS_FLAG_GLOBAL = 1;
    private const uint DIF_PROPERTYCHANGE = 0x12;

    private static bool SetupDiChangeState(string instanceId, uint stateChange)
    {
        IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == new IntPtr(-1)) return false;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0, ref info))
            { Log.Write($"Touchpad: SetupDiOpenDeviceInfo({instanceId}) не удалась"); return false; }

            var pcp = new SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                {
                    cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                    InstallFunction = DIF_PROPERTYCHANGE,
                },
                StateChange = stateChange,
                Scope = DICS_FLAG_GLOBAL,
                HwProfile = 0,
            };
            if (!SetupDiSetClassInstallParams(set, ref info, ref pcp, (uint)Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()) ||
                !SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref info))
            {
                Log.Write($"Touchpad: DIF_PROPERTYCHANGE({stateChange}) → Win32 0x{Marshal.GetLastWin32Error():X}");
                return false;
            }
            return true;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_CLASSINSTALL_HEADER { public uint cbSize; public uint InstallFunction; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll")]
    private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiOpenDeviceInfo(IntPtr deviceInfoSet, string deviceInstanceId,
        IntPtr hwndParent, uint openFlags, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, uint classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(uint devInst, StringBuilder buffer, int bufferLen, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint devInst, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Reenumerate_DevNode(uint devInst, uint flags);
}
