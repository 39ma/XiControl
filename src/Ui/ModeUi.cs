using XiControl.Wmi;

namespace XiControl.Ui;

/// <summary>UI-маппинг режима производительности: ключ локализации и вид OSD.</summary>
internal static class ModeUi
{
    public static string? Key(PerfMode m) => m switch
    {
        PerfMode.Eco => "mode.eco",
        PerfMode.Quiet => "mode.quiet",
        PerfMode.Auto => "mode.auto",
        PerfMode.Turbo => "mode.turbo",
        PerfMode.FullSpeed => "mode.full",
        _ => null,
    };

    public static OsdKind Kind(PerfMode m) => m switch
    {
        PerfMode.Eco => OsdKind.Eco,
        PerfMode.Quiet => OsdKind.Quiet,
        PerfMode.Auto => OsdKind.Auto,
        PerfMode.Turbo => OsdKind.Turbo,
        PerfMode.FullSpeed => OsdKind.Full,
        _ => OsdKind.Auto,
    };
}
