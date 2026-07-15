using System.Reflection;

namespace XiControl.SystemIntegration;

/// <summary>
/// Встроенные звуки (WAV из assets/sound, EmbeddedResource `sound.&lt;имя&gt;.wav`).
/// Проигрывание — в фоне (PlaySync на своём потоке), чтобы не держать UI.
/// </summary>
public static class Sound
{
    /// <summary>Джингл готовности «В дорогу» (батарея заряжена до 100%).</summary>
    public static void PlayTravelReady() => Play("sound.travel-ready.wav");

    private static void Play(string resource) => Task.Run(() =>
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream is null) { Log.Write($"Sound: ресурс не найден: {resource}"); return; }
            using var player = new System.Media.SoundPlayer(stream);
            player.PlaySync();
        }
        catch (Exception ex) { Log.Ex("Sound.Play", ex); }
    });
}
