using System.Drawing.Drawing2D;

namespace XiControl.Ui;

/// <summary>
/// Палитра тёмных флайаутов (панель Mi-кнопки, «Монитор»; OSD пока со своей копией).
/// Флайауты сознательно всегда тёмные — следовать ли системной теме, решается в Фазе 6.4;
/// до тех пор единственный источник этих цветов здесь. Акценты — из docs/10-colors.md.
/// </summary>
public static class FlyoutPalette
{
    public static readonly Color Card = Color.FromArgb(28, 28, 30);    // фон карточки
    public static readonly Color Border = Color.FromArgb(70, 70, 74);  // рамка по контуру
    public static readonly Color Text = Color.FromArgb(238, 238, 238);
    public static readonly Color Dim = Color.FromArgb(150, 150, 155);  // вторичный текст
    public static readonly Color Green = Color.FromArgb(52, 199, 89);  // ок / заряд / тихий
    public static readonly Color Blue = Color.FromArgb(90, 170, 255);  // авто / CPU
    public static readonly Color Orange = Color.FromArgb(255, 149, 0); // турбо / разряд / «в дорогу»
    public static readonly Color Red = Color.FromArgb(255, 82, 82);    // полная мощность / выключено
}

/// <summary>
/// База флайаутов: borderless tool-window поверх всех окон (не светится в таскбаре и Alt-Tab),
/// скруглённый Region, тёмная карточка с рамкой, Esc прячет. OSD сюда сознательно не переведён —
/// он не активируется и закрывается затуханием, а не действиями пользователя.
/// </summary>
public abstract class FlyoutForm : Form
{
    protected FlyoutForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = FlyoutPalette.Card;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected float S => DeviceDpi / 96f;
    protected int Sc(float v) => (int)Math.Round(v * S);

    /// <summary>
    /// Скруглить окно под текущий Size. Прежний Region освобождаем сами:
    /// присваивание не отдаёт старый GDI-хэндл.
    /// </summary>
    protected void SetRoundedRegion(int corner)
    {
        var old = Region;
        using var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), corner);
        Region = new Region(path);
        old?.Dispose();
    }

    /// <summary>Общий фон кадра: сглаживание, ClearType, заливка карточки и рамка по контуру.</summary>
    protected void PaintChrome(Graphics g, int corner)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(FlyoutPalette.Card);
        using var pen = new Pen(FlyoutPalette.Border);
        using var path = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), corner);
        g.DrawPath(pen, path);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }
}
