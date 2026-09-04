using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MaddenSaveConverter;

/// <summary>
/// Plays the bundled boot-up sting (.mp3 beside the exe) once, fire-and-forget.
/// Backed by WinMM (mciSendString) for reliable short mp3 playback — no extra
/// packages, no Media Foundation timing flakiness. Silently no-ops when no audio
/// is shipped or playback fails, and never blocks startup.
/// </summary>
public static class BootSound
{
    public static void Play()
    {
        try
        {
            var mp3 = FindBundled(AppContext.BaseDirectory, "Sounds", "*.mp3");
            if (mp3 != null) PlayMci(mp3);
        }
        catch
        {
            // never let audio break startup
        }
    }

    static string FindBundled(string root, string sub, string pattern)
    {
        var dir = Path.Combine(root, sub);
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, pattern).FirstOrDefault();
    }

    // mciSendString: alias 'fgt_boot', open the file, play async, then close.
    static void PlayMci(string path)
    {
        const string alias = "fgt_boot";
        CloseMci(alias);
        var open = $"open \"{path}\" type mpegvideo alias {alias}";
        if (mciSendString(open, null, 0, IntPtr.Zero) != 0) return;
        mciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
        // Let it finish then clean up (short sting, a few seconds).
        Task.Run(async () =>
        {
            try { await Task.Delay(6000); } finally { CloseMci(alias); }
        });
    }

    static void CloseMci(string alias)
        => mciSendString($"close {alias}", null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder returnString, int returnLength, IntPtr hwndCallback);
}