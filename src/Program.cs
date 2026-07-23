using XiControl.Config;
using XiControl.Localization;
using XiControl.Ui;
using XiControl.Wmi;

namespace XiControl;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // единственный экземпляр
        using var mutex = new Mutex(true, @"Global\XiControlMutex", out bool created);
        if (!created) return;

        ApplicationConfiguration.Initialize();

        var cfg = new JsonConfigStore().Load();
        Log.Enabled = cfg.LogEnabled; // до этой строчки лог включён — ошибки старта не теряем
        Loc.Current = cfg.Language;

        MifsClient mifs;
        try
        {
            mifs = new MifsClient();
        }
        catch (Exception ex)
        {
            Log.Ex("Startup", ex);
            MessageBox.Show(
                Loc.T("err.noiface") + "\n\n" + ex.Message,
                Loc.T("err.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var tray = new TrayApp(mifs, cfg);
        Application.Run();
        mifs.Dispose();
    }
}
