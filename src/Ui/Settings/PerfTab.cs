using System.Drawing.Drawing2D;
using XiControl.Config;
using XiControl.Localization;
using XiControl.Wmi;

namespace XiControl.Ui.Settings;

/// <summary>
/// Вкладка «Производительность»: видимость режимов, стратегия старта (радио-карточки),
/// профили питания. Текущая стратегия читается через act.GetStartStrategy — логика
/// взаимоисключений живёт в AppController, вкладка только рисует.
/// </summary>
public sealed class PerfTab : SettingsPane
{
    private readonly AppConfig _cfg;
    private readonly SettingsActions _act;

    // строки «профили питания» — прячем/показываем по выбору стратегии старта
    private readonly List<Control> _profileRows = [];
    private Panel[] _startCards = [];

    public PerfTab(SettingsToolkit ui, AppConfig cfg, SettingsActions act, Action rebuild) : base(ui)
    {
        _cfg = cfg;
        _act = act;

        ui.AddHeader(this, "settings.tab.perf", "settings.perf.sub");
        ui.AddGroup(this, "settings.perf.modes");
        // после смены видимости пересобираем окно (после выхода из обработчика):
        // комбо профилей ниже предлагают только видимые режимы
        ui.AddRow(this, "settings.show.eco", "settings.show.eco.desc",
            ui.Toggle(cfg.EcoMode, on => { act.SetModeVisibility(on, cfg.FullSpeedMode); rebuild(); }));
        ui.AddRow(this, "settings.show.full", "settings.show.full.desc",
            ui.Toggle(cfg.FullSpeedMode, on => { act.SetModeVisibility(cfg.EcoMode, on); rebuild(); }));

        ui.AddGroup(this, "settings.startmode");
        var strat = act.GetStartStrategy();
        _startCards =
        [
            RadioCard(StartStrategy.None, "settings.start.none", "settings.start.none.desc"),
            RadioCard(StartStrategy.Restore, "settings.start.restore", "settings.start.restore.desc"),
            RadioCard(StartStrategy.Pin, "settings.start.pin", "settings.start.pin.desc"),
            RadioCard(StartStrategy.Profiles, "settings.start.profiles", "settings.start.profiles.desc"),
        ];

        // под-опции профилей
        var acRow = ui.SubRow("settings.profile.ac", ProfileCombo(true));
        var batRow = ui.SubRow("settings.profile.battery", ProfileCombo(false));
        _profileRows.AddRange([acRow, batRow]);
        foreach (var r in _profileRows) { Controls.Add(r); r.Visible = strat == StartStrategy.Profiles; }
    }

    // Радио-карточка стратегии старта: выбранность читается из конфига при отрисовке (GetStartStrategy)
    private Panel RadioCard(StartStrategy s, string titleKey, string descKey)
    {
        var card = new Panel { Width = Ui.RowW, Height = Ui.Sc(56), BackColor = Ui.T.Card, Margin = new Padding(0, 0, 0, Ui.Sc(4)), Cursor = Cursors.Hand, Tag = s };
        card.Region = new Region(Draw.Rounded(new RectangleF(0, 0, Ui.RowW, Ui.Sc(56)), Ui.Sc(6)));
        card.Paint += (_, e) =>
        {
            Ui.PaintCardBorder(e.Graphics, Ui.RowW, Ui.Sc(56));
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on = (StartStrategy)card.Tag! == _act.GetStartStrategy();
            float d = Ui.Sc(18), x = Ui.Sc(18), y = (card.Height - d) / 2f;
            using var pen = new Pen(on ? Ui.T.Accent : (Ui.T.Dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(120, 120, 120)), 1.6f);
            g.DrawEllipse(pen, x, y, d, d);
            if (on) { using var b = new SolidBrush(Ui.T.Accent); g.FillEllipse(b, x + d * 0.28f, y + d * 0.28f, d * 0.44f, d * 0.44f); }
        };
        var t = new Label { Text = Loc.T(titleKey), AutoSize = true, ForeColor = Ui.T.Text, BackColor = Color.Transparent, Font = Ui.TitleFont, Location = new Point(Ui.Sc(48), Ui.Sc(8)) };
        var dsc = new Label { Text = Loc.T(descKey), Tag = "dim", AutoSize = true, MaximumSize = new Size(Ui.RowW - Ui.Sc(70), 0), ForeColor = Ui.T.Text2, BackColor = Color.Transparent, Font = Ui.DescFont, Location = new Point(Ui.Sc(48), Ui.Sc(28)) };
        void Pick(object? _, EventArgs __) { _act.SetStartStrategy(s); UpdateStartUi(); }
        card.Click += Pick; t.Click += Pick; dsc.Click += Pick;
        t.Cursor = dsc.Cursor = Cursors.Hand;
        card.Controls.Add(t); card.Controls.Add(dsc);
        Controls.Add(card);
        return card;
    }

    private void UpdateStartUi()
    {
        foreach (var c in _startCards) c.Invalidate();
        bool prof = _act.GetStartStrategy() == StartStrategy.Profiles;
        foreach (var r in _profileRows) r.Visible = prof;
    }

    // Только видимые режимы (скрытый Full-speed из приложения включить нельзя — контракт
    // AppConfig.FullSpeedMode); плюс текущий выбор, даже если его успели скрыть.
    private ComboBox ProfileCombo(bool ac)
    {
        var cur = ac ? _cfg.AcPerfMode : _cfg.BatteryPerfMode;
        var modes = AppController.AllModes.Where(m => m == cur
            || (m != PerfMode.Eco || _cfg.EcoMode) && (m != PerfMode.FullSpeed || _cfg.FullSpeedMode)).ToArray();
        var items = new List<string> { Loc.T("settings.profile.nochange") };
        items.AddRange(modes.Select(m => Loc.T(ModeUi.Key(m) ?? "mode.auto")));
        int idx = cur is PerfMode pm ? Array.FindIndex(modes, m => m == pm) + 1 : 0;
        return Ui.Combo([.. items], idx, i =>
            _act.SetProfileMode(ac, i == 0 ? null : modes[i - 1]), Ui.Sc(140));
    }
}
