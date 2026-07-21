using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using XiControl.Config;
using XiControl.Localization;
using XiControl.Wmi;

namespace XiControl.Ui;

/// <summary>Какая стратегия режима при старте выбрана (взаимоисключающие).</summary>
public enum StartStrategy { None, Restore, Pin, Profiles }

/// <summary>
/// Колбэки в TrayApp: окно настроек не дублирует логику (взаимоисключения режимов старта,
/// переармливание гардов, применение профиля) — оно меняет то, что тривиально (config.json),
/// а «умные» операции делегирует сюда.
/// </summary>
public sealed class SettingsActions
{
    public Func<bool> GetAutoStart = () => false;
    public Action<bool> SetAutoStart = _ => { };
    public Action<Lang> SetLanguage = _ => { };
    public Action<bool, bool> SetModeVisibility = (_, _) => { };   // eco, full
    public Action<StartStrategy> SetStartStrategy = _ => { };
    public Action<bool, PerfMode?> SetProfileMode = (_, _) => { }; // ac, mode
    public Action<bool> SetRememberBrightness = _ => { };
    public Action<bool> SetAutoHz = _ => { };
    public Action<int, int> SetRefreshRates = (_, _) => { };       // ac, batt
    public Action<bool> SetOwlFeature = _ => { };
}

/// <summary>
/// Единое окно настроек в стиле Windows 11: слева навигация по группам, справа прокручиваемая
/// панель опций. Создаётся лениво, между открытиями прячется (не диспозится) — в фоне таймеров
/// нет, поэтому «живёт» только пока открыто. Тема (тёмная/светлая) берётся из системы при показе.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly SettingsActions _act;

    private static readonly (PerfMode mode, string key)[] AllModes =
    [
        (PerfMode.Eco, "mode.eco"), (PerfMode.Quiet, "mode.quiet"), (PerfMode.Auto, "mode.auto"),
        (PerfMode.Turbo, "mode.turbo"), (PerfMode.FullSpeed, "mode.full"),
    ];

    // шрифты — из кэша ScaledFonts под текущий DPI (Label шрифтом не владеет, в OnPaint
    // не создаём): пропорции с геометрией Sc держатся и после смены разрешения/масштаба
    private Font HeadFont => ScaledFonts.Get(DeviceDpi, "Segoe UI Semibold", 15f);
    private Font NameFont => ScaledFonts.Get(DeviceDpi, "Segoe UI Semibold", 14f);
    private Font GroupFont => ScaledFonts.Get(DeviceDpi, "Segoe UI Semibold", 9.5f);
    private Font TitleFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 10f);
    private Font DescFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 8.5f);
    private Font NoteFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 9f);
    private Font CtlFont => ScaledFonts.Get(DeviceDpi, "Segoe UI", 9.5f);

    // палитра (заполняется в ApplyTheme по системной теме)
    private bool _dark;
    private Color _winBg, _navBg, _card, _border, _text, _text2, _accent, _sel, _field;

    private NavStrip _nav = null!;
    private Panel _host = null!;
    private readonly List<Panel> _panes = [];
    private int _tab;

    // строки «профили питания» — прячем/показываем по выбору стратегии старта
    private readonly List<Control> _profileRows = [];
    private Panel[] _startCards = [];

    private float S => DeviceDpi / 96f;
    private int Sc(float v) => (int)Math.Round(v * S);

    // MifsClient сюда сознательно не передаётся: окно железо не трогает — все «умные»
    // операции идут через колбэки SettingsActions в TrayApp.
    public SettingsForm(AppConfig cfg, SettingsActions act)
    {
        _cfg = cfg;
        _act = act;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual; // позицию считаем сами в Popup (всегда центр)
        KeyPreview = true;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* иконки нет — не критично */ }

        _ = Handle; // форсируем хэндл (нужен DeviceDpi)
        ClientSize = new Size(Sc(824), Sc(700));

        BuildAll();
    }

    /// <summary>
    /// Открыть окно. Содержимое пересобирается при каждом открытии: настройки могли смениться
    /// из трея/панели, пока окно было спрятано (авто-герцовка и т.п.), тема — тоже. Пересборка
    /// дешёвая (несколько десятков контролов), а окно открывают редко.
    /// </summary>
    public void Popup()
    {
        BuildAll();
        // высота — чтобы вкладки влезали без прокрутки, но не выше рабочей области экрана;
        // окно каждый раз открывается по центру экрана с курсором
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int h = Math.Min(Sc(700), wa.Height - Sc(80));
        if (ClientSize.Height != h) ClientSize = new Size(Sc(824), h);
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            ActiveControl = null; // форсим Leave у текстовых полей — сохранить недописанный путь
            e.Cancel = true;
            Hide(); // прячем, не закрываем
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            ActiveControl = null; // как и при закрытии крестиком — коммит текстовых полей
            Hide();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    // ---- Тема ----

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetDwmDark(Theme.IsDark());
    }

    private void ApplyTheme()
    {
        bool dark = Theme.IsDark();
        _dark = dark;
        if (dark)
        {
            _winBg = Color.FromArgb(32, 32, 32); _navBg = Color.FromArgb(38, 38, 38);
            _card = Color.FromArgb(45, 45, 45); _border = Color.FromArgb(56, 56, 56);
            _text = Color.FromArgb(240, 240, 240); _text2 = Color.FromArgb(200, 200, 200);
            _accent = Color.FromArgb(96, 205, 255); _sel = Color.FromArgb(52, 52, 52);
            _field = Color.FromArgb(39, 39, 39);
        }
        else
        {
            _winBg = Color.FromArgb(243, 243, 243); _navBg = Color.FromArgb(234, 234, 234);
            _card = Color.White; _border = Color.FromArgb(229, 229, 229);
            _text = Color.FromArgb(26, 26, 26); _text2 = Color.FromArgb(95, 95, 95);
            _accent = Color.FromArgb(0, 95, 184); _sel = Color.FromArgb(233, 233, 233);
            _field = Color.FromArgb(251, 251, 251);
        }
        SetDwmDark(dark);
        Text = Loc.T("settings.title");
        BackColor = _winBg;
    }

    private void SetDwmDark(bool dark)
    {
        if (!IsHandleCreated) return;
        try { int v = dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, sizeof(int)); }
        catch { /* старая Windows — не критично */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    // WM_SETREDRAW: гасим перерисовку на время пересборки видимого окна — SuspendLayout
    // замораживает только компоновку, и без этого чайлды мигают белым (смена языка и т.п.)
    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- Построение ----

    private void BuildAll()
    {
        // окно видно (смена языка/видимости режимов) — гасим перерисовку целиком,
        // иначе пересборка мигает белым; в конце один Refresh
        bool live = IsHandleCreated && Visible;
        if (live) SendMessage(Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
        try
        {
            BuildAllCore();
        }
        finally
        {
            if (live) { SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); Refresh(); }
        }
    }

    private void BuildAllCore()
    {
        ApplyTheme();
        SuspendLayout();
        // Clear не диспозит старые контролы — освобождаем сами (хэндлы, Region'ы), как в BuildMenu
        var stale = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        foreach (var c in stale) c.Dispose();
        _panes.Clear();
        _profileRows.Clear();

        _host = new Panel { Dock = DockStyle.Fill, BackColor = _winBg, Tag = "host" };
        Controls.Add(_host);

        _nav = new NavStrip(this) { Dock = DockStyle.Left, Width = Sc(212) };
        _nav.Tabs =
        [
            ("settings.tab.general", NavGlyph.General),
            ("settings.tab.battery", NavGlyph.Battery),
            ("settings.tab.display", NavGlyph.Display),
            ("settings.tab.perf", NavGlyph.Perf),
            ("settings.tab.keys", NavGlyph.Keys),
            ("settings.tab.about", NavGlyph.About),
        ];
        _nav.Selected = _tab;
        _nav.SelectedChanged = SelectTab;
        Controls.Add(_nav); // Fill(_host) добавлен раньше → Left(_nav) резервирует левую полосу

        _panes.Add(BuildGeneral());
        _panes.Add(BuildBattery());
        _panes.Add(BuildDisplay());
        _panes.Add(BuildPerf());
        _panes.Add(BuildKeys());
        _panes.Add(BuildAbout());
        // хвостовой «воздух»: FlowLayoutPanel.AutoScroll не учитывает нижний Padding — без спейсера
        // последняя карточка обрезается при прокрутке
        foreach (var p in _panes)
        {
            p.Controls.Add(new Panel { Width = RowW, Height = Sc(20), BackColor = _winBg, Margin = new Padding(0) });
            p.Visible = false;
            _host.Controls.Add(p);
        }

        SelectTab(_tab);
        ResumeLayout();
    }

    private void SelectTab(int i)
    {
        _tab = i;
        for (int k = 0; k < _panes.Count; k++) _panes[k].Visible = k == i;
        _nav.Selected = i;
        _nav.Invalidate();
    }

    private Panel NewPane()
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = _winBg,
            Padding = new Padding(Sc(26), Sc(18), Sc(26), Sc(24)),
            Tag = "pane",
        };
        return p;
    }

    private int RowW => Sc(824) - Sc(212) - Sc(52) - Sc(16);

    // ---- Вкладки ----

    private Panel BuildGeneral()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.general", "settings.general.sub");
        AddRow(p, "settings.language", "settings.language.desc",
            Combo(["Русский", "English", "中文"], (int)_cfg.Language, i =>
            {
                _act.SetLanguage((Lang)i);
                BeginInvoke(new Action(BuildAll)); // пересобрать окно на новом языке (после выхода из обработчика)
            }, Sc(150)));
        AddRow(p, "settings.autostart", "settings.autostart.desc",
            Toggle(_act.GetAutoStart(), _act.SetAutoStart));

        AddGroup(p, "settings.general.comfort");
        AddRow(p, "settings.profile.brightness", "settings.brightness.desc",
            Toggle(_cfg.RememberBrightness, _act.SetRememberBrightness));
        AddRow(p, "settings.owl.feature", "settings.owl.feature.desc",
            Toggle(_cfg.OwlMode, _act.SetOwlFeature));
        AddRow(p, "settings.touchpad.feature", "settings.touchpad.feature.desc",
            Toggle(_cfg.TouchpadFeature, on => { _cfg.TouchpadFeature = on; _cfg.Save(); }));
        AddRow(p, "settings.touchscreen.feature", "settings.touchscreen.feature.desc",
            Toggle(_cfg.TouchscreenFeature, on => { _cfg.TouchscreenFeature = on; _cfg.Save(); }));
        AddRow(p, "settings.log", "settings.log.desc",
            Toggle(_cfg.LogEnabled, on =>
            {
                _cfg.LogEnabled = on;
                _cfg.Save();
                if (on) { Log.Enabled = true; Log.Write("Логирование включено"); }
                else { Log.Write("Логирование выключено"); Log.Enabled = false; } // прощальная строчка — видно, что тишина намеренная
            }));
        return p;
    }

    private Panel BuildBattery()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.battery", "settings.battery.sub");
        AddNote(p, "settings.battery.note");
        AddGroup(p, "settings.battery.travel");
        AddRow(p, "settings.travel.sound", "settings.travel.sound.desc",
            Toggle(_cfg.TravelSound, on => { _cfg.TravelSound = on; _cfg.Save(); }));

        var soundBox = TextField(_cfg.TravelSoundFile ?? "", Sc(230), s =>
        {
            _cfg.TravelSoundFile = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            _cfg.Save();
        });
        soundBox.PlaceholderText = "%USERPROFILE%\\Music\\ready.wav";
        var browse = LinkButton("settings.browse", () =>
        {
            using var d = new OpenFileDialog { Filter = "WAV|*.wav", CheckFileExists = true };
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                soundBox.Text = d.FileName;
                _cfg.TravelSoundFile = d.FileName; _cfg.Save();
            }
        });
        browse.AutoSize = false;
        browse.Size = new Size(Sc(92), Sc(28));
        AddRow(p, "settings.travel.file", "settings.travel.file.desc", Pair(soundBox, browse));
        return p;
    }

    private Panel BuildDisplay()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.display", "settings.display.sub");
        AddRow(p, "settings.hz.auto", "settings.hz.auto.desc",
            Toggle(_cfg.AutoRefreshRate, _act.SetAutoHz));
        AddGroup(p, "settings.hz.rates");
        AddRow(p, "settings.hz.ac", "settings.hz.ac.desc",
            HzCombo(_cfg.AcRefreshRate, hz => _act.SetRefreshRates(hz, _cfg.BatteryRefreshRate)));
        AddRow(p, "settings.hz.battery", "settings.hz.battery.desc",
            HzCombo(_cfg.BatteryRefreshRate, hz => _act.SetRefreshRates(_cfg.AcRefreshRate, hz)));
        AddNote(p, "settings.hz.note");
        return p;
    }

    // Комбо частоты: пресеты + текущее значение из config.json, если оно нестандартное
    // (вручную вписанные 165 Гц не должны отображаться как «144»)
    private ComboBox HzCombo(int current, Action<int> apply)
    {
        int[] presets = [144, 120, 90, 60, 48];
        int[] rates = presets.Contains(current) ? presets : [current, .. presets];
        return Combo([.. rates.Select(r => $"{r} " + Loc.T("settings.hz.unit"))],
            Array.IndexOf(rates, current), i => apply(rates[i]), Sc(110));
    }

    private Panel BuildPerf()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.perf", "settings.perf.sub");
        AddGroup(p, "settings.perf.modes");
        // после смены видимости пересобираем окно (BeginInvoke — после выхода из обработчика):
        // комбо профилей ниже предлагают только видимые режимы
        AddRow(p, "settings.show.eco", "settings.show.eco.desc",
            Toggle(_cfg.EcoMode, on =>
            { _act.SetModeVisibility(on, _cfg.FullSpeedMode); BeginInvoke(new Action(BuildAll)); }));
        AddRow(p, "settings.show.full", "settings.show.full.desc",
            Toggle(_cfg.FullSpeedMode, on =>
            { _act.SetModeVisibility(_cfg.EcoMode, on); BeginInvoke(new Action(BuildAll)); }));

        AddGroup(p, "settings.startmode");
        var strat = CurrentStrategy();
        _startCards =
        [
            RadioCard(p, StartStrategy.None, "settings.start.none", "settings.start.none.desc"),
            RadioCard(p, StartStrategy.Restore, "settings.start.restore", "settings.start.restore.desc"),
            RadioCard(p, StartStrategy.Pin, "settings.start.pin", "settings.start.pin.desc"),
            RadioCard(p, StartStrategy.Profiles, "settings.start.profiles", "settings.start.profiles.desc"),
        ];

        // под-опции профилей
        var acCombo = ProfileCombo(true);
        var batCombo = ProfileCombo(false);
        var acRow = SubRow("settings.profile.ac", acCombo);
        var batRow = SubRow("settings.profile.battery", batCombo);
        _profileRows.AddRange([acRow, batRow]);
        foreach (var r in _profileRows) { p.Controls.Add(r); r.Visible = strat == StartStrategy.Profiles; }
        return p;
    }

    // Общий список действий для всех клавиш; порядок = порядок в комбо
    private static readonly string[] KeyActionValues =
    [
        "modes", "charge", "panel", "owl", "monitor", "travel", "touchpad", "touchscreen",
        "projection", "settings", "copilot", "launch", "none",
    ];

    private Panel BuildKeys()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.keys", "settings.keys.sub");

        AddGroup(p, "settings.keys.mi");
        AddKeySlot(p, "settings.key.mi.click", "settings.key.mi.click.desc",
            () => _cfg.MiClickAction, v => _cfg.MiClickAction = v,
            () => _cfg.MiClickCommand, v => _cfg.MiClickCommand = v);
        AddKeySlot(p, "settings.key.mi.double", "settings.key.mi.double.desc",
            () => _cfg.MiDoubleAction, v => _cfg.MiDoubleAction = v,
            () => _cfg.MiDoubleCommand, v => _cfg.MiDoubleCommand = v);
        AddNote(p, "settings.keys.mi.hold");

        AddGroup(p, "settings.keys.other");
        AddKeySlot(p, "settings.key.settings", "settings.key.settings.desc",
            () => _cfg.SettingsKeyAction, v => _cfg.SettingsKeyAction = v,
            () => _cfg.SettingsKeyCommand, v => _cfg.SettingsKeyCommand = v);
        AddKeySlot(p, "settings.key.ai", "settings.key.ai.desc",
            () => _cfg.AiKeyAction, v => _cfg.AiKeyAction = v,
            () => _cfg.AiKeyCommand, v => _cfg.AiKeyCommand = v);
        AddKeySlot(p, "settings.key.proj", "settings.key.proj.desc",
            () => _cfg.ProjKeyAction, v => _cfg.ProjKeyAction = v,
            () => _cfg.ProjKeyCommand, v => _cfg.ProjKeyCommand = v);
        return p;
    }

    // Слот клавиши: комбо со всеми действиями; для «Запустить программу…» ниже появляется
    // поле команды (путь + аргументы). Смена действия пересобирает окно (поле показать/убрать).
    private void AddKeySlot(Panel p, string titleKey, string descKey,
        Func<string?> getAction, Action<string> setAction,
        Func<string?> getCommand, Action<string?> setCommand)
    {
        string cur = getAction() ?? "none";
        int idx = Array.IndexOf(KeyActionValues, cur);
        if (idx < 0) idx = Array.IndexOf(KeyActionValues, "none"); // неизвестное значение из конфига
        string prev = cur;
        AddRow(p, titleKey, descKey,
            Combo([.. KeyActionValues.Select(a => Loc.T("settings.act." + a))], idx, i =>
            {
                string val = KeyActionValues[i];
                setAction(val);
                _cfg.Save();
                // пересборка — только чтобы показать/убрать поле команды: на каждом шаге
                // выбора (колёсико, стрелки) она закрывала дропдаун и «сбрасывала» выбор
                bool rebuild = prev == "launch" || val == "launch";
                prev = val;
                if (rebuild) BeginInvoke(new Action(BuildAll));
            }, Sc(210)));
        if (cur == "launch")
        {
            var tf = TextField(getCommand() ?? "", Sc(300), s =>
            { setCommand(string.IsNullOrWhiteSpace(s) ? null : s.Trim()); _cfg.Save(); });
            tf.PlaceholderText = "\"C:\\Program Files\\App\\app.exe\" --flag";
            p.Controls.Add(SubRow("settings.key.command", tf));
        }
    }

    private Panel BuildAbout()
    {
        var p = NewPane();
        AddHeader(p, "settings.tab.about", "settings.about.sub");

        var hero = new Panel { Width = RowW, Height = Sc(74), BackColor = _winBg, Margin = new Padding(0, 0, 0, Sc(8)) };
        var ico = new PictureBox
        {
            Image = SvgIcons.Render("settings", Sc(52)),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Size = new Size(Sc(60), Sc(60)), Location = new Point(0, Sc(6)), BackColor = _sel,
        };
        ico.Region = new Region(Draw.Rounded(new RectangleF(0, 0, Sc(60), Sc(60)), Sc(12)));
        hero.Controls.Add(ico);
        var name = new Label { Text = "XiControl", Font = NameFont, AutoSize = true, ForeColor = _text, BackColor = Color.Transparent, Location = new Point(Sc(74), Sc(10)) };
        var ver = new Label { Text = $"{Loc.T("settings.version")} {AppVersion()}  ·  GPLv3", Tag = "dim", AutoSize = true, ForeColor = _text2, BackColor = Color.Transparent, Location = new Point(Sc(74), Sc(40)) };
        hero.Controls.Add(name); hero.Controls.Add(ver);
        p.Controls.Add(hero);

        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Width = RowW, Margin = new Padding(0, 0, 0, Sc(14)), BackColor = _winBg };
        links.Controls.Add(LinkButton("GitHub", () => Open("https://github.com/Oksion/XiControl")));
        links.Controls.Add(LinkButton("settings.about.forum", () => Open("https://4pda.to/forum/index.php?showtopic=1122287")));
        links.Controls.Add(LinkButton("settings.about.license", () => Open("https://github.com/Oksion/XiControl/blob/main/LICENSE")));
        links.Controls.Add(LinkButton("settings.about.updates", () => Open("https://github.com/Oksion/XiControl/releases")));
        p.Controls.Add(links);

        AddKv(p, "settings.about.model", SafeModel());
        AddKv(p, "settings.about.iface", "MiCommonInterface (MIFS)");
        AddKv(p, "settings.about.config", "%APPDATA%\\XiControl\\config.json");
        AddKv(p, "settings.about.log", "%APPDATA%\\XiControl\\log.txt");
        AddNote(p, "settings.about.note");
        return p;
    }

    // ---- Строки / контролы ----

    private void AddHeader(Panel p, string titleKey, string subKey)
    {
        p.Controls.Add(new Label
        {
            Text = Loc.T(titleKey), Font = HeadFont, AutoSize = true,
            ForeColor = _text, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, Sc(2)),
        });
        p.Controls.Add(new Label
        {
            Text = Loc.T(subKey), Tag = "dim", AutoSize = true, MaximumSize = new Size(RowW, 0),
            ForeColor = _text2, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, Sc(14)),
        });
    }

    private void AddGroup(Panel p, string key) => p.Controls.Add(new Label
    {
        Text = Loc.T(key), Font = GroupFont, AutoSize = true,
        ForeColor = _text2, BackColor = Color.Transparent, Margin = new Padding(2, Sc(14), 0, Sc(6)),
    });

    private void AddRow(Panel p, string titleKey, string descKey, Control ctl)
        => p.Controls.Add(Row(Loc.T(titleKey), Loc.T(descKey), ctl));

    private Panel Row(string title, string desc, Control ctl)
    {
        // ширина под текст, чтобы не залезть под контрол; описание меряем и растим карточку по факту
        int textW = Math.Max(Sc(120), RowW - ctl.Width - Sc(48));
        int descH = string.IsNullOrEmpty(desc)
            ? 0
            : TextRenderer.MeasureText(desc, DescFont, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
        int h = string.IsNullOrEmpty(desc) ? Sc(52) : Sc(29) + descH + Sc(14);

        var card = new Panel { Width = RowW, Height = h, BackColor = _card, Margin = new Padding(0, 0, 0, Sc(4)), Tag = "cardrow" };
        card.Region = new Region(Draw.Rounded(new RectangleF(0, 0, RowW, h), Sc(6)));
        card.Paint += (_, e) => PaintCardBorder(e.Graphics, RowW, h);

        var t = new Label { Text = title, AutoSize = false, Width = textW, Height = Sc(20), ForeColor = _text, BackColor = Color.Transparent, Font = TitleFont, Location = new Point(Sc(16), Sc(9)), AutoEllipsis = true };
        card.Controls.Add(t);
        if (!string.IsNullOrEmpty(desc))
        {
            var d = new Label { Text = desc, Tag = "dim", AutoSize = false, Width = textW, Height = descH + Sc(2), ForeColor = _text2, BackColor = Color.Transparent, Font = DescFont, Location = new Point(Sc(16), Sc(29)) };
            card.Controls.Add(d);
        }
        card.Controls.Add(ctl);
        ctl.Location = new Point(RowW - ctl.Width - Sc(16), (h - ctl.Height) / 2);
        ctl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        return card;
    }

    // Инфо-плашка: сглаженная скруглённая заливка + текст рисуем сами (без Region — иначе рваные
    // углы, и без дочернего Label — иначе прозрачность даёт «ореол» неверного фона).
    private void AddNote(Panel p, string key)
    {
        string text = Loc.T(key);
        int textW = RowW - Sc(28);
        int textH = TextRenderer.MeasureText(text, NoteFont, new Size(textW, 0), TextFormatFlags.WordBreak).Height;

        var note = new Panel { Width = RowW, Height = textH + Sc(26), BackColor = _winBg, Margin = new Padding(0, Sc(2), 0, Sc(4)) };
        note.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Draw.Rounded(new RectangleF(0.5f, 0.5f, RowW - 1.5f, note.Height - 1.5f), Sc(6)))
            using (var fill = new SolidBrush(_sel))
                g.FillPath(fill, path);
            TextRenderer.DrawText(g, text, NoteFont, new Rectangle(Sc(14), Sc(13), textW, note.Height - Sc(26)),
                _text2, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        };
        p.Controls.Add(note);
    }

    private void AddKv(Panel p, string key, string val)
    {
        var row = new Panel { Width = RowW, Height = Sc(34), BackColor = _winBg, Margin = new Padding(0) };
        row.Paint += (_, e) => { using var pen = new Pen(_border); e.Graphics.DrawLine(pen, 0, row.Height - 1, RowW, row.Height - 1); };
        row.Controls.Add(new Label { Text = Loc.T(key), Tag = "dim", AutoSize = true, ForeColor = _text2, BackColor = Color.Transparent, Location = new Point(Sc(2), Sc(9)) });
        row.Controls.Add(new Label { Text = val, AutoSize = true, ForeColor = _text, BackColor = Color.Transparent, Location = new Point(Sc(180), Sc(9)) });
        p.Controls.Add(row);
    }

    private Panel SubRow(string titleKey, Control ctl)
    {
        var row = new Panel { Width = RowW, Height = Sc(42), BackColor = _winBg, Margin = new Padding(Sc(30), 0, 0, 0) };
        row.Controls.Add(new Label { Text = Loc.T(titleKey), AutoSize = true, ForeColor = _text, BackColor = Color.Transparent, Font = CtlFont, Location = new Point(Sc(2), Sc(11)) });
        ctl.Location = new Point(RowW - ctl.Width - Sc(16) - Sc(30), (Sc(42) - ctl.Height) / 2);
        ctl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        row.Controls.Add(ctl);
        return row;
    }

    // Радио-карточка стратегии старта: выбранность читается из конфига при отрисовке (CurrentStrategy)
    private Panel RadioCard(Panel p, StartStrategy s, string titleKey, string descKey)
    {
        var card = new Panel { Width = RowW, Height = Sc(56), BackColor = _card, Margin = new Padding(0, 0, 0, Sc(4)), Cursor = Cursors.Hand, Tag = s };
        card.Region = new Region(Draw.Rounded(new RectangleF(0, 0, RowW, Sc(56)), Sc(6)));
        card.Paint += (_, e) =>
        {
            PaintCardBorder(e.Graphics, RowW, Sc(56));
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on = (StartStrategy)card.Tag! == CurrentStrategy();
            float d = Sc(18), x = Sc(18), y = (card.Height - d) / 2f;
            using var pen = new Pen(on ? _accent : (_dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(120, 120, 120)), 1.6f);
            g.DrawEllipse(pen, x, y, d, d);
            if (on) { using var b = new SolidBrush(_accent); g.FillEllipse(b, x + d * 0.28f, y + d * 0.28f, d * 0.44f, d * 0.44f); }
        };
        var t = new Label { Text = Loc.T(titleKey), AutoSize = true, ForeColor = _text, BackColor = Color.Transparent, Font = TitleFont, Location = new Point(Sc(48), Sc(8)) };
        var dsc = new Label { Text = Loc.T(descKey), Tag = "dim", AutoSize = true, MaximumSize = new Size(RowW - Sc(70), 0), ForeColor = _text2, BackColor = Color.Transparent, Font = DescFont, Location = new Point(Sc(48), Sc(28)) };
        void Pick(object? _, EventArgs __) { _act.SetStartStrategy(s); UpdateStartUi(); }
        card.Click += Pick; t.Click += Pick; dsc.Click += Pick;
        t.Cursor = dsc.Cursor = Cursors.Hand;
        card.Controls.Add(t); card.Controls.Add(dsc);
        p.Controls.Add(card);
        return card;
    }

    private void UpdateStartUi()
    {
        foreach (var c in _startCards) c.Invalidate();
        bool prof = CurrentStrategy() == StartStrategy.Profiles;
        foreach (var r in _profileRows) r.Visible = prof;
    }

    private StartStrategy CurrentStrategy()
    {
        if (_cfg.PowerProfiles) return StartStrategy.Profiles;
        if (_cfg.ForceStartMode is not null) return StartStrategy.Pin;
        if (_cfg.RestoreMode) return StartStrategy.Restore;
        return StartStrategy.None;
    }

    // Только видимые режимы (скрытый Full-speed из приложения включить нельзя — контракт
    // AppConfig.FullSpeedMode); плюс текущий выбор, даже если его успели скрыть.
    private ComboBox ProfileCombo(bool ac)
    {
        var cur = ac ? _cfg.AcPerfMode : _cfg.BatteryPerfMode;
        var modes = AllModes.Where(m => m.mode == cur
            || (m.mode != PerfMode.Eco || _cfg.EcoMode) && (m.mode != PerfMode.FullSpeed || _cfg.FullSpeedMode)).ToArray();
        var items = new List<string> { Loc.T("settings.profile.nochange") };
        items.AddRange(modes.Select(m => Loc.T(m.key)));
        int idx = cur is PerfMode pm ? Array.FindIndex(modes, m => m.mode == pm) + 1 : 0;
        return Combo([.. items], idx, i =>
            _act.SetProfileMode(ac, i == 0 ? null : modes[i - 1].mode), Sc(140));
    }

    private ToggleSwitch Toggle(bool on, Action<bool> changed)
    {
        var t = new ToggleSwitch
        {
            Size = new Size(Sc(40), Sc(20)), Checked = on, BackColor = _card, Accent = _accent,
            OnKnob = _dark ? Color.FromArgb(0, 45, 74) : Color.White,
            OffLine = _dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(120, 120, 120),
        };
        t.CheckedChanged += (_, _) => changed(t.Checked);
        return t;
    }

    private ComboBox Combo(string[] items, int index, Action<int> changed, int width)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Sc(22),
            Width = width, BackColor = _field, ForeColor = _text, Font = CtlFont,
        };
        cb.Items.AddRange(items);
        // тёмная тема: нативный combo игнорирует BackColor — рисуем сами (и закрытый бокс, и список)
        cb.DrawItem += (_, e) =>
        {
            // закрытое «поле» (ComboBoxEdit) всегда нейтральное; акцент — только для элементов списка
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = !edit && (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(sel ? _accent : _field);
            e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, cb.Items[e.Index]?.ToString() ?? "", cb.Font, e.Bounds,
                    sel ? (_dark ? Color.FromArgb(0, 45, 74) : Color.White) : _text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding);
        };
        if (index >= 0 && index < items.Length) cb.SelectedIndex = index;
        cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedIndex >= 0) changed(cb.SelectedIndex); };
        return cb;
    }

    private TextBox TextField(string val, int width, Action<string> changed)
    {
        var tb = new TextBox
        {
            Text = val, Width = width, BorderStyle = BorderStyle.FixedSingle,
            BackColor = _field, ForeColor = _text, Font = CtlFont,
        };
        tb.Leave += (_, _) => changed(tb.Text);
        return tb;
    }

    private Panel Pair(Control a, Control b)
    {
        var host = new Panel { Width = a.Width + b.Width + Sc(8), Height = Math.Max(a.Height, b.Height), BackColor = Color.Transparent };
        a.Location = new Point(0, (host.Height - a.Height) / 2);
        b.Location = new Point(a.Width + Sc(8), (host.Height - b.Height) / 2);
        host.Controls.Add(a); host.Controls.Add(b);
        return host;
    }

    private Button LinkButton(string keyOrText, Action click)
    {
        var b = new Button
        {
            Text = keyOrText.StartsWith("settings.") || keyOrText.StartsWith("app.") ? Loc.T(keyOrText) : keyOrText,
            AutoSize = false, Height = Sc(30), Width = Sc(0), Padding = new Padding(Sc(6), 0, Sc(6), 0),
            FlatStyle = FlatStyle.Flat, BackColor = _card, ForeColor = _text, Font = CtlFont,
            Margin = new Padding(0, 0, Sc(8), 0), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = _border;
        b.AutoSize = true;
        b.Click += (_, _) => click();
        return b;
    }

    // карточка обрезана по Region (скруглённая), фон = BackColor(_card); здесь только рамка
    private void PaintCardBorder(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Draw.Rounded(new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f), Sc(6));
        using var pen = new Pen(_border);
        g.DrawPath(pen, path);
    }

    private static string AppVersion()
    {
        try { return FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion?.Split('+')[0] ?? "—"; }
        catch { return "—"; }
    }

    private string SafeModel()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var o in s.Get()) return o["Model"]?.ToString() ?? "—";
        }
        catch { /* WMI недоступен */ }
        return "—";
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* нет браузера */ }
    }

    // ---- Навигация (кастомная отрисовка) ----

    private enum NavGlyph { General, Battery, Display, Perf, Keys, About }

    private sealed class NavStrip : Panel
    {
        private readonly SettingsForm _o;
        public (string key, NavGlyph glyph)[] Tabs = [];
        public int Selected;
        public Action<int>? SelectedChanged;
        private int _hover = -1;

        public NavStrip(SettingsForm o)
        {
            _o = o;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private int ItemH => _o.Sc(40);
        private int TopPad => _o.Sc(12);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Y);
            if (i != _hover) { _hover = i; Invalidate(); Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = HitTest(e.Y);
            if (i >= 0 && i != Selected) SelectedChanged?.Invoke(i);
            base.OnMouseClick(e);
        }

        // «О программе» прижата вниз
        private bool IsBottom(int i) => i == Tabs.Length - 1;
        private int RowY(int i) => IsBottom(i) ? Height - _o.Sc(12) - ItemH : TopPad + i * (ItemH + _o.Sc(2));
        private int HitTest(int y)
        {
            for (int i = 0; i < Tabs.Length; i++)
                if (y >= RowY(i) && y < RowY(i) + ItemH) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_o._navBg);
            int pad = _o.Sc(8);

            for (int i = 0; i < Tabs.Length; i++)
            {
                int y = RowY(i);
                var rect = new Rectangle(pad, y, Width - pad * 2, ItemH);
                bool sel = i == Selected, hov = i == _hover;
                if (sel || hov)
                {
                    using var b = new SolidBrush(sel ? _o._sel : Color.FromArgb(_o._dark ? 22 : 14, _o._text));
                    using var path = Draw.Rounded(rect, _o.Sc(5));
                    g.FillPath(b, path);
                }
                if (sel)
                {
                    using var ab = new SolidBrush(_o._accent);
                    using var bar = Draw.Rounded(new RectangleF(rect.X, rect.Y + ItemH * 0.28f, _o.Sc(3), ItemH * 0.44f), _o.Sc(1.5f));
                    g.FillPath(ab, bar);
                }
                var gc = sel ? _o._accent : _o._text2;
                DrawGlyph(g, Tabs[i].glyph, new RectangleF(rect.X + _o.Sc(12), rect.Y + (ItemH - _o.Sc(18)) / 2f, _o.Sc(18), _o.Sc(18)), gc);
                TextRenderer.DrawText(g, Loc.T(Tabs[i].key), _o.TitleFont,
                    new Rectangle(rect.X + _o.Sc(40), rect.Y, rect.Width - _o.Sc(40), ItemH),
                    _o._text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawGlyph(Graphics g, NavGlyph k, RectangleF r, Color c)
        {
            using var pen = new Pen(c, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            switch (k)
            {
                case NavGlyph.General:
                    g.DrawEllipse(pen, x + w * 0.35f, y + h * 0.35f, w * 0.3f, h * 0.3f);
                    for (int a = 0; a < 8; a++)
                    {
                        double ang = a * Math.PI / 4;
                        float cx = x + w / 2f, cy = y + h / 2f;
                        g.DrawLine(pen, cx + (float)Math.Cos(ang) * w * 0.32f, cy + (float)Math.Sin(ang) * h * 0.32f,
                            cx + (float)Math.Cos(ang) * w * 0.48f, cy + (float)Math.Sin(ang) * h * 0.48f);
                    }
                    break;
                case NavGlyph.Battery:
                    g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.3f, w * 0.72f, h * 0.4f);
                    g.DrawLine(pen, x + w * 0.86f, y + h * 0.42f, x + w * 0.86f, y + h * 0.58f);
                    g.DrawLines(pen, [new PointF(x + w * 0.42f, y + h * 0.36f), new PointF(x + w * 0.32f, y + h * 0.52f), new PointF(x + w * 0.46f, y + h * 0.52f), new PointF(x + w * 0.36f, y + h * 0.66f)]);
                    break;
                case NavGlyph.Display:
                    g.DrawRectangle(pen, x + w * 0.1f, y + h * 0.2f, w * 0.8f, h * 0.5f);
                    g.DrawLine(pen, x + w * 0.35f, y + h * 0.86f, x + w * 0.65f, y + h * 0.86f);
                    g.DrawLine(pen, x + w * 0.5f, y + h * 0.7f, x + w * 0.5f, y + h * 0.86f);
                    break;
                case NavGlyph.Perf:
                    g.DrawArc(pen, x + w * 0.12f, y + h * 0.2f, w * 0.76f, h * 0.76f, 180, 180);
                    g.DrawLine(pen, x + w / 2f, y + h * 0.58f, x + w * 0.72f, y + h * 0.32f);
                    break;
                case NavGlyph.Keys:
                    g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.28f, w * 0.84f, h * 0.44f);
                    for (int d = 0; d < 4; d++) g.DrawLine(pen, x + w * (0.22f + d * 0.18f), y + h * 0.42f, x + w * (0.22f + d * 0.18f), y + h * 0.42f);
                    g.DrawLine(pen, x + w * 0.32f, y + h * 0.58f, x + w * 0.68f, y + h * 0.58f);
                    break;
                case NavGlyph.About:
                    g.DrawEllipse(pen, x + w * 0.15f, y + h * 0.15f, w * 0.7f, h * 0.7f);
                    g.DrawLine(pen, x + w / 2f, y + h * 0.45f, x + w / 2f, y + h * 0.68f);
                    using (var dot = new SolidBrush(c))
                        g.FillEllipse(dot, x + w / 2f - _o.Sc(1), y + h * 0.3f, _o.Sc(2.2f), _o.Sc(2.2f));
                    break;
            }
        }
    }
}
