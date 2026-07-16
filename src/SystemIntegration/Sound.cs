using System.Reflection;

namespace XiControl.SystemIntegration;

/// <summary>
/// Звуки уведомлений. По умолчанию — встроенный WAV (EmbeddedResource `sound.&lt;имя&gt;.wav`),
/// но пользователь может указать свой файл. Проигрывание — в фоне (PlaySync на своём потоке).
/// </summary>
public static class Sound
{
    /// <summary>
    /// Джингл готовности «В дорогу» (батарея заряжена до 100%). Если задан <paramref name="customFile"/>
    /// и файл существует — играем его (только WAV/PCM), иначе — встроенный джингл.
    /// </summary>
    public static void PlayTravelReady(string? customFile = null) => Task.Run(() =>
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(customFile)
                ? null
                : Environment.ExpandEnvironmentVariables(customFile.Trim());

            if (path is not null)
            {
                if (File.Exists(path)) { using var p = new System.Media.SoundPlayer(path); p.PlaySync(); return; }
                Log.Write($"Sound: свой WAV не найден ({path}) — играю встроенный");
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("sound.travel-ready.wav");
            if (stream is null) { Log.Write("Sound: встроенный ресурс не найден: sound.travel-ready.wav"); return; }
            using var player = new System.Media.SoundPlayer(stream);
            player.PlaySync();
        }
        catch (Exception ex) { Log.Ex("Sound.PlayTravelReady", ex); }
    });
}
