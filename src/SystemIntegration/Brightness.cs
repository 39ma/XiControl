using System.Management;

namespace XiControl.SystemIntegration;

/// <summary>
/// Яркость встроенного экрана через WMI (root\wmi): чтение WmiMonitorBrightness,
/// установка WmiMonitorBrightnessMethods.WmiSetBrightness. На внешних мониторах
/// (и редких панелях без ACPI-управления) классов нет — методы вернут null/false.
/// </summary>
public static class Brightness
{
    /// <summary>Текущая яркость в процентах (null — не прочитать).</summary>
    public static int? Get()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject o in s.Get())
                using (o) return (byte)o["CurrentBrightness"];
        }
        catch (Exception ex) { Log.Ex("Brightness.Get", ex); }
        return null;
    }

    /// <summary>Установить яркость в процентах. true — команда принята.</summary>
    public static bool Set(int percent)
    {
        try
        {
            percent = Math.Clamp(percent, 0, 100);
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
                    return true;
                }
        }
        catch (Exception ex) { Log.Ex("Brightness.Set", ex); }
        return false;
    }
}
