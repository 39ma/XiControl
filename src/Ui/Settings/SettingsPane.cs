namespace XiControl.Ui.Settings;

/// <summary>
/// Базовая панель вкладки настроек: вертикальный поток карточек с прокруткой.
/// Каждая вкладка — самостоятельный контрол, собирающий себя в конструкторе;
/// SettingsForm только хостит и переключает видимость.
/// </summary>
public abstract class SettingsPane : FlowLayoutPanel
{
    protected readonly SettingsToolkit Ui;

    protected SettingsPane(SettingsToolkit ui)
    {
        Ui = ui;
        Dock = DockStyle.Fill;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        BackColor = ui.T.WinBg;
        Padding = new Padding(ui.Sc(26), ui.Sc(18), ui.Sc(26), ui.Sc(24));
        Tag = "pane";
    }
}
