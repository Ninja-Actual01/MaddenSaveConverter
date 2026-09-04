using System.IO;
using System.Windows.Media;

namespace MaddenSaveConverter;

/// <summary>
/// Plays the bundled boot-up sting (.mp3 beside the exe) once, fire-and-forget.
/// Silently no-ops when no audio is shipped or the codec is unavailable, so it
/// never blocks startup and never throws in headless/CLI runs.
/// </summary>
public static class BootSound
{
    public static void Play()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var mp3 = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "Sounds\\*.mp3").FirstOrDefault()
                : null;
            if (mp3 == null) return;

            var player = new System.Windows.Media.MediaPlayer();
            player.MediaFailed += (_, _) => player.Close();
            player.MediaOpened += (_, _) => { try { player.Play(); } catch { } };
            player.Open(new Uri(mp3, UriKind.Absolute));
        }
        catch
        {
            // never let audio break startup
        }
    }
}