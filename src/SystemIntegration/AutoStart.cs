using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace XiControl.SystemIntegration;

/// <summary>
/// Автозапуск через Планировщик заданий — единственный корректный способ
/// стартовать elevated-приложение при входе БЕЗ UAC-запроса.
/// Задача создаётся с RunLevel=HighestAvailable и разрешением работы на батарее.
/// </summary>
public static class AutoStart
{
    private const string TaskName = "XiControl";

    private static string SchTasks =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    public static bool IsEnabled() => Run("/query", "/tn", TaskName) == 0;

    public static void Set(bool enabled)
    {
        if (enabled) Enable(); else Disable();
    }

    private static bool Enable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "XiControl_task.xml");
        try
        {
            // schtasks /xml требует файл в кодировке UTF-16
            File.WriteAllText(tmp, BuildXml(), Encoding.Unicode);
            return Run("/create", "/tn", TaskName, "/xml", tmp, "/f") == 0;
        }
        catch (Exception ex) { Log.Ex("AutoStart.Enable", ex); return false; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static bool Disable() => Run("/delete", "/tn", TaskName, "/f") == 0;

    private static int Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SchTasks,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return -1;
            _ = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (Exception ex) { Log.Ex("AutoStart.Run", ex); return -1; }
    }

    private static string BuildXml()
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        string user = WindowsIdentity.GetCurrent().Name; // DOMAIN\User
        string exeX = SecurityElement.Escape(exe)!;
        string userX = SecurityElement.Escape(user)!;

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Xi Control — автозапуск при входе</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{userX}</UserId>
      <Delay>PT5S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{userX}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exeX}</Command>
      <Arguments>--autostart</Arguments>
    </Exec>
  </Actions>
</Task>";
    }
}
