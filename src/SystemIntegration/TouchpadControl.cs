using System.Runtime.InteropServices;
using System.Text;
using XiControl.Config;

namespace XiControl.SystemIntegration;

/// <summary>
/// Включение/выключение тачпада — программный аналог «Отключить устройство» из Диспетчера
/// устройств: чистый user-mode SetupAPI/CfgMgr32, без драйверов (нужны права администратора —
/// они у нас есть). Прошивочный путь мёртв: WMI 0x0C (TPLock) на TM2424 к железу не подключён.
///
/// Тачпад ищется по HID-коллекции Precision Touchpad (Usage Page 0x0D, Usage 0x05), но
/// отключается её РОДИТЕЛЬ (узел «HID на шине I2C»): у PTP две коллекции (мышь + панель),
/// и отключение одной лишь панели оставило бы курсор живым через мышиную.
///
/// Отключаем БЕЗ флага PERSIST: после перезагрузки тачпад всегда включён — «удалил приложение
/// с выключенным тачпадом» не превращается в проблему. Родительский ID запоминается в конфиге:
/// при выключенном тачпаде HID-коллекции исчезают из системы, и после перезапуска приложения
/// найти узел для включения иначе нечем.
/// </summary>
public sealed class TouchpadControl(AppConfig cfg)
{
    private const string CompatId = "HID_DEVICE_UP:000D_U:0005"; // Precision Touchpad TLC
    private const uint ProbDisabled = 22;                        // CM_PROB_DISABLED

    private readonly AppConfig _cfg = cfg;
    private uint? _devInst; // кэш узла-родителя (стабилен в рамках сессии, и когда отключён)

    /// <summary>Тачпад найден (сейчас или по запомненному ID)?</summary>
    public bool Available => Find() is not null;

    /// <summary>true/false — состояние узла; null — тачпад не найден.</summary>
    public bool? IsEnabled()
    {
        if (Find() is not uint inst) return null;
        if (CM_Get_DevNode_Status(out _, out uint problem, inst, 0) != 0) return null;
        return problem != ProbDisabled;
    }

    /// <summary>Переключить. Возвращает новое состояние, null — не нашли/не вышло.</summary>
    public bool? Toggle()
    {
        if (IsEnabled() is not bool on || Find() is not uint inst) return null;
        int cr = on ? CM_Disable_DevNode(inst, 0) : CM_Enable_DevNode(inst, 0);
        if (cr != 0) { Log.Write($"Touchpad: CM_{(on ? "Disable" : "Enable")}_DevNode → CR 0x{cr:X}"); return null; }
        return !on;
    }

    // Родитель тачпада: через живую HID-коллекцию, иначе — по запомненному ID (тачпад выключен).
    private uint? Find()
    {
        if (_devInst is uint cached) return cached;
        try
        {
            if (FindViaHid() is uint inst) { _devInst = inst; return inst; }
            if (!string.IsNullOrEmpty(_cfg.TouchpadDeviceId) &&
                CM_Locate_DevNode(out uint located, _cfg.TouchpadDeviceId, 0) == 0)
            {
                _devInst = located;
                return located;
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
                    }
                }
                return parent;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(uint devInst, StringBuilder buffer, int bufferLen, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);
}
