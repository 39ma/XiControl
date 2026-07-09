using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XiControl.SystemIntegration;

/// <summary>Действия для «оживления» спец-клавиш: проекция, настройки, Copilot.</summary>
public static class KeyActions
{
    private const byte VK_LWIN = 0x5B, VK_P = 0x50, VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x02;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Проекция экрана — эмулировать Win+P.</summary>
    public static void Projection() => WinCombo(VK_P);

    /// <summary>Нейропомощник — открыть Windows Copilot (Win+C).</summary>
    public static void Copilot() => WinCombo(VK_C);

    /// <summary>Открыть Параметры Windows (опция "SettingsKey": "settings").</summary>
    public static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Ex("KeyActions.OpenSettings", ex); /* URI-обработчик может отсутствовать */ }
    }

    /// <summary>Запустить произвольную программу/файл/URL (для настраиваемой AI-клавиши).</summary>
    public static void Launch(string path, string? args = null)
    {
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(args))
                psi.Arguments = Environment.ExpandEnvironmentVariables(args);
            if (File.Exists(path))
                psi.WorkingDirectory = Path.GetDirectoryName(path);
            Process.Start(psi);
        }
        catch (Exception ex) { Log.Ex("KeyActions.Launch", ex); }
    }

    private static void WinCombo(byte vk)
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
