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
    readonly List<string> _manifest = new();      // display titles in playlist order
    readonly List<string> _titles = new();        // display title per playable slot
    readonly List<string> _audioPaths = new();    // audio file per playable slot (aligned with _titles)
    readonly Dictionary<string, string> _byTitle = new(StringComparer.OrdinalIgnoreCase); // title -> file
    int _index = -1;
    bool _playing;

    public bool HasTracks => _audioPaths.Count > 0 || _manifest.Count > 0;
    public bool IsPlaying => _playing;
    public string NowPlaying => _index >= 0 && _index < _titles.Count ? _titles[_index] : "—";

    public BgmPlayer()
    {
        Load();
    }

    public string CurrentLabel =>
        _index < 0 ? "no music playing"
        : _titles.Count > 0 ? $"▶ {NowPlaying}"
        : "music loaded";

    void Load()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Music");
        if (!Directory.Exists(root)) return;

        // Manifest first: ordered user-facing titles.
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
                    if (!string.IsNullOrWhiteSpace(s)) _manifest.Add(s.Trim());
                }
            }
        }
        catch { /* manifest optional */ }

        // Audio files, keyed by base name so a manifest title maps to its audio.
        foreach (var f in Directory.GetFiles(root))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".mp3" or ".wav" or ".m4a")) continue;
            _byTitle[Path.GetFileNameWithoutExtension(f)] = f;
        }

        // Build the aligned playlist: manifest order when a file exists, then stragglers.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _manifest)
        {
            if (_byTitle.TryGetValue(t, out var file))
            {
                _titles.Add(t); _audioPaths.Add(file); used.Add(t);
            }
        }
        foreach (var kv in _byTitle)
        {
            if (!used.Contains(kv.Key)) { _titles.Add(kv.Key); _audioPaths.Add(kv.Value); }
        }

        // No manifest: fall back to the audio files themselves.
        if (_titles.Count == 0)
        {
            if (_byTitle.Count > 0)
            {
                foreach (var kv in _byTitle.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                { _titles.Add(kv.Key); _audioPaths.Add(kv.Value); }
            }
            else
            {
                _titles.AddRange(_manifest);
            }
        }
    }

    /// <summary>Toggle play/stop.</summary>
    public void Toggle()
    {
        if (_audioPaths.Count == 0) { if (!_playing) _index = 0; return; }
        if (_playing) { Stop(); return; }
        if (_index < 0) _index = 0;
        PlayFrom(_index);
    }

    /// <summary>Skip to the next track.</summary>
    public void Skip()
    {
        if (_titles.Count == 0) return;
        Stop();
        _index = (_index + 1) % _titles.Count;
        if (_audioPaths.Count > 0) PlayFrom(_index);
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