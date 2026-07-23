using XiControl.Config;
using XiControl.Localization;

namespace XiControl.Ui.Settings;

/// <summary>Вкладка «Общие»: язык, автозапуск, комфорт-фичи, логирование.</summary>
public sealed class GeneralTab : SettingsPane
{
    public GeneralTab(SettingsToolkit ui, AppConfig cfg, SettingsActions act, Action rebuild) : base(ui)
    {
        ui.AddHeader(this, "settings.tab.general", "settings.general.sub");
        ui.AddRow(this, "settings.language", "settings.language.desc",
            ui.Combo(["Русский", "English", "中文"], (int)cfg.Language, i =>
            {
                act.SetLanguage((Lang)i);
                rebuild(); // пересобрать окно на новом языке (после выхода из обработчика)
            }, ui.Sc(150)));
        ui.AddRow(this, "settings.autostart", "settings.autostart.desc",
            ui.Toggle(act.GetAutoStart(), act.SetAutoStart));

        ui.AddGroup(this, "settings.general.comfort");
        ui.AddRow(this, "settings.profile.brightness", "settings.brightness.desc",
            ui.Toggle(cfg.RememberBrightness, act.SetRememberBrightness));
        ui.AddRow(this, "settings.owl.feature", "settings.owl.feature.desc",
            ui.Toggle(cfg.OwlMode, act.SetOwlFeature));
        ui.AddRow(this, "settings.touchpad.feature", "settings.touchpad.feature.desc",
            ui.Toggle(cfg.TouchpadFeature, on => { cfg.TouchpadFeature = on; cfg.Save(); }));
        ui.AddRow(this, "settings.touchscreen.feature", "settings.touchscreen.feature.desc",
            ui.Toggle(cfg.TouchscreenFeature, on => { cfg.TouchscreenFeature = on; cfg.Save(); }));
        ui.AddRow(this, "settings.log", "settings.log.desc",
            ui.Toggle(cfg.LogEnabled, on =>
            {
                cfg.LogEnabled = on;
                cfg.Save();
                if (on) { Log.Enabled = true; Log.Write("Логирование включено"); }
                else { Log.Write("Логирование выключено"); Log.Enabled = false; } // прощальная строчка — видно, что тишина намеренная
            }));
    }
}
