using System.Runtime.InteropServices;
using XiControl.Config;

namespace XiControl.SystemIntegration;

/// <summary>
/// Режим «Не спать»: пока включён — экран не гаснет и система не засыпает
/// (SetThreadExecutionState), а закрытие крышки НА ПИТАНИИ ОТ СЕТИ не уводит
/// в сон (действие крышки AC → «ничего не делать» через Power Management API).
/// На батарее крышка не трогается — там штатный сон.
/// Исходное действие крышки хранится в config.json (восстановление после сбоя).
/// </summary>
public static class AwakeMode
{
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

    private static Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347"); // GUID_SYSTEM_BUTTON_SUBGROUP
    private static Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");  // GUID_LIDCLOSE_ACTION
    private const uint LidDoNothing = 0;

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint esFlags);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);

    /// <summary>Включить. Вызывать с UI-потока (ES_CONTINUOUS привязан к потоку). Конфиг сохраняет вызывающий.</summary>
    public static bool Enable(AppConfig cfg)
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var pScheme) != 0)
                return false;
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(pScheme);

                // исходное действие крышки запоминаем один раз (если уже включены — не перетирать)
                if (cfg.AwakeSavedLidAc is null &&
                    PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, out uint cur) == 0)
                {
                    cfg.AwakeSavedLidAc = (int)cur;
                }

                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, LidDoNothing);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            }
            finally { LocalFree(pScheme); }

            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            return true;
        }
        catch (Exception ex) { Log.Ex("AwakeMode.Enable", ex); return false; }
    }

    /// <summary>Выключить: вернуть действие крышки и снять запрет сна/гашения экрана.</summary>
    public static void Disable(AppConfig cfg)
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS);

            if (cfg.AwakeSavedLidAc is int saved && PowerGetActiveScheme(IntPtr.Zero, out var pScheme) == 0)
            {
                try
                {
                    var scheme = Marshal.PtrToStructure<Guid>(pScheme);
                    PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, (uint)saved);
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                }
                finally { LocalFree(pScheme); }
                cfg.AwakeSavedLidAc = null;
            }
        }
        catch (Exception ex) { Log.Ex("AwakeMode.Disable", ex); }
    }
}
