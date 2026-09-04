using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MaddenSaveConverter;

/// <summary>
/// Background-music player driven by local audio files in the app's Music folder,
/// with an optional playlist manifest (Music/*.json) that provides the display titles
/// and ordering. Plays via WinMM (mciSendString), the same reliable path as the boot
/// sting. Topline UI exposes play/pause + skip.
/// </summary>
public sealed class BgmPlayer
{
    const string Alias = "bgm";
    readonly List<string> _titles = new();       // display titles from the manifest
    readonly List<string> _audioPaths = new();   // actual playable files, in order
    int _index = -1;
    bool _playing;

    public bool HasTracks => _titles.Count > 0;
    public bool IsPlaying => _playing;
    public string NowPlaying
    {
        get
        {
            if (_index < 0) return "—";
            if (_titles.Count > 0 && _index < _titles.Count) return _titles[_index];
            if (_audioPaths.Count > 0 && _index < _audioPaths.Count)
                return Path.GetFileNameWithoutExtension(_audioPaths[_index]);
            return "—";
        }
    }

    public BgmPlayer()
    {
        Load();
    }

    public string CurrentLabel =>
        HasTracks ? $"▶ {NowPlaying}" : "no music tracks";

    void Load()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Music");
        if (!Directory.Exists(root)) return;

        // Titles come from the first *.json manifest we find (ordered, user-readable).
        try
        {
            var manifest = Directory.GetFiles(root, "*.json").FirstOrDefault();
            if (manifest != null)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var t = doc.RootElement.GetProperty("tracks");
                foreach (var e in t.EnumerateArray())
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) _titles.Add(s.Trim());
                }
            }
        }
        catch { /* manifest optional */ }

        // Actual audio files, sorted by name. If none exist yet the ribbon still shows
        // the manifest titles but playback is inert (offline ready).
        var audio = Directory.GetFiles(root)
            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _audioPaths.AddRange(audio);
    }

    /// <summary>Toggle play/stop.</summary>
    public void Toggle()
    {
        if (_audioPaths.Count == 0) return; // no audio to play yet
        if (_playing) { Stop(); return; }
        if (_index < 0) _index = 0;
        PlayFrom(_index);
    }

    /// <summary>Skip to the next playable audio file (and next display title).</summary>
    public void Skip()
    {
        if (_audioPaths.Count == 0)
        {
            // No audio yet: still let the ribbon cycle manifest titles so it reads naturally.
            if (_titles.Count > 0) _index = (_index + 1) % _titles.Count;
            return;
        }
        Stop();
        _index = (_index + 1) % _audioPaths.Count;
        PlayFrom(_index);
    }

    void PlayFrom(int audioIndex)
    {
        Close();
        var path = _audioPaths[audioIndex % _audioPaths.Count];
        _playing = PlayMci(path);
    }

    bool PlayMci(string path) =>
        mciSendString($"open \"{path}\" type mpegvideo alias {Alias}", null, 0, IntPtr.Zero) == 0
        && mciSendString($"play {Alias} from 0", null, 0, IntPtr.Zero) == 0;

    public void Stop()
    {
        _playing = false;
        Close();
    }

    void Close() => mciSendString($"close {Alias}", null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder returnString, int returnLength, IntPtr hwndCallback);
}