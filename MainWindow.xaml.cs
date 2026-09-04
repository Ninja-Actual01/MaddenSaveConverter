using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MaddenSaveConverter.Convert;
using MaddenSaveConverter.Franchise;
using MaddenSaveConverter.Tdb2;

namespace MaddenSaveConverter;

public partial class MainWindow : Window
{
    // default save locations + bundled templates (shared with CLI)
    static readonly string M26Saves = Cli.M26Saves;
    static readonly string M27Saves = Cli.M27Saves;
    static string TplM26 => Cli.TplM26;
    static string TplM27 => Cli.TplM27;

    string _sourcePath;
    FranchiseEngineClient _engine;
    BgmPlayer _bgm;
    string _studioOrigPath;   // the user's original (never modified)
    string _studioWorkPath;   // temp working copy we read/<-> edit against (scratch copy)
    string _studioOutputPath; // final edited copy (same saves folder, game-eligible name)
    string _studioBackupPath; // verified backup of the original
    readonly List<StudioRow> _teams = new();

    public MainWindow()
    {
        InitializeComponent();
        Log("LEGION OF LOOM FOOTBALL GAME TOOL");
        Log("------------------------------------");
        Log($"M26 saves: {M26Saves}");
        Log($"M27 saves: {M27Saves}");
        Log("");
        IntroToggle.IsChecked = AppSettings.ShowIntroOnBoot;
    }

    void Log(string s)
    {
        LogBox.AppendText(s + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    void IntroToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IntroToggle != null && IntroToggle.IsChecked.HasValue)
            AppSettings.SetIntroEnabled(IntroToggle.IsChecked.Value);
    }

    void BgmToggle_Click(object sender, RoutedEventArgs e)
    {
        _bgm ??= new BgmPlayer();
        _bgm.Toggle();
        BgmToggleBtn.Content = _bgm.IsPlaying ? "⏸ Pause" : "▶ Play";
        BgmNow.Text = _bgm.CurrentLabel;
    }

    void BgmSkip_Click(object sender, RoutedEventArgs e)
    {
        _bgm ??= new BgmPlayer();
        _bgm.Skip();
        BgmToggleBtn.Content = _bgm.IsPlaying ? "⏸ Pause" : "▶ Play";
        BgmNow.Text = _bgm.CurrentLabel;
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick a save file",
            InitialDirectory = Directory.Exists(M27Saves) ? M27Saves : M26Saves,
            Filter = "Save files (ROSTER-*, CAREERDRAFT-*, CAREER-*)|ROSTER-*;CAREERDRAFT-*;CAREER-*|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            SourcePathBox.Text = _sourcePath = dlg.FileName;
    }

    void SourcePath_TextChanged(object sender, TextChangedEventArgs e)
    {
        DetectPanel.Visibility = Visibility.Collapsed;
        ConvertBtn.IsEnabled = false;
        OutputNote.Text = "";
        if (!File.Exists(SourcePathBox.Text)) return;

        try
        {
            var header = new byte[0x80];
            using (var fs = File.OpenRead(SourcePathBox.Text))
                if (fs.Read(header, 0, header.Length) < 0x2E) return;

            if (System.Text.Encoding.ASCII.GetString(header, 0, 8) != "FBCHUNKS")
            {
                DetectPanel.Visibility = Visibility.Visible;
                DetectText.Text = $"{Path.GetFileName(SourcePathBox.Text)} — not an FBCHUNKS save (draft classes and franchise files come in a later build)";
                return;
            }

            var all = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
            string ver;
            int end = Array.IndexOf(header, (byte)0, 0x2E);
            ver = System.Text.Encoding.ASCII.GetString(header, 0x2E, end < 0 ? 40 : end - 0x2E);
            if (all.Contains("Madden-27")) ver = "Madden-27";
            else if (all.Contains("Madden-26")) ver = "Madden-26";
            string from, to, tpl;
            string fn = Path.GetFileName(SourcePathBox.Text);
            bool isDraft = fn.StartsWith("CAREERDRAFT", StringComparison.OrdinalIgnoreCase);
            bool isFranchise = fn.StartsWith("CAREER-", StringComparison.OrdinalIgnoreCase) && !isDraft;
            if (ver.Contains("Madden-27"))
            {
                from = "Madden 27"; to = "Madden 26";
                tpl = isDraft ? Cli.TplDraftM26 : isFranchise ? Cli.TplFranchM26 : TplM26;
                OutputNote.Text = $"Output → {M26Saves}\\{Path.GetFileName(SourcePathBox.Text)}";
            }
            else if (ver.Contains("Madden-26"))
            {
                from = "Madden 26"; to = "Madden 27";
                tpl = isDraft ? Cli.TplDraftM27 : isFranchise ? Cli.TplFranchM27 : TplM27;
                OutputNote.Text = $"Output → {M27Saves}\\{Path.GetFileName(SourcePathBox.Text)}";
            }
            else
            {
                DetectPanel.Visibility = Visibility.Visible;
                DetectIcon.Text = "!";
                DetectText.Text = $"Unrecognized version: {ver} — only Madden 26/27 rosters are supported right now";
                return;
            }

            string kind = isDraft ? "draft class" : isFranchise ? "franchise" : "roster";
            DetectPanel.Visibility = Visibility.Visible;
            DetectText.Text = $"Detected: {from} {kind} ({ver})   →   will convert for {to}";

            ConvertBtn.IsEnabled = true;
            ConvertBtn.Tag = tpl;
        }
        catch (Exception ex)
        {
            Log("[error] " + ex.Message);
        }
    }

    async void Convert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConvertBtn.IsEnabled = false;
            var srcPath = SourcePathBox.Text;
            var tplPath = (string)ConvertBtn.Tag;
            bool toM27 = tplPath == TplM27 || tplPath == Cli.TplDraftM27 || tplPath == Cli.TplFranchM27;
            string fn2 = Path.GetFileName(srcPath);
            bool isDraft = fn2.StartsWith("CAREERDRAFT", StringComparison.OrdinalIgnoreCase);
            bool isFranchise = fn2.StartsWith("CAREER-", StringComparison.OrdinalIgnoreCase) && !isDraft;
            string outDir = toM27 ? M27Saves : M26Saves;
            string outPath = Path.Combine(outDir, Path.GetFileName(srcPath));

            Log("");
            Log($"converting: {srcPath}");
            Log($"type:       {(isDraft ? "draft class" : isFranchise ? "franchise" : "roster")}");
            Log($"direction:  {(toM27 ? "M26 → M27" : "M27 → M26")}");

            var res = await System.Threading.Tasks.Task.Run(() =>
            {
                if (isDraft)
                    return toM27 ? DraftConverter.M26ToM27(srcPath, tplPath, outPath)
                                 : DraftConverter.M27ToM26(srcPath, tplPath, outPath);
                if (isFranchise)
                    return toM27 ? FranchiseConverter.M26ToM27(srcPath, tplPath, outPath)
                                 : FranchiseConverter.M27ToM26(srcPath, tplPath, outPath);
                var src = RosterFile.Load(srcPath);
                var tpl = RosterFile.Load(tplPath);
                return toM27
                    ? RosterProfiles.M26ToM27(src, tpl, outPath)
                    : RosterProfiles.M27ToM26(src, tpl, outPath);
            });

            foreach (var l in res.Log) Log("  " + l);

            Log("verify:");
            bool ok = (isDraft || isFranchise) ? File.Exists(outPath) : RosterFile.Verify(outPath, m => Log("  " + m));
            if (ok)
            {
                Log($"DONE → {outPath}");
                Log("back up your saves before loading this in-game!");
            }
            else
            {
                Log("FAILED verification - output was NOT written correctly");
            }
            ConvertBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log("[error] " + ex.Message);
            ConvertBtn.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------
    // Franchise Studio
    // ------------------------------------------------------------------

    sealed class StudioRow
    {
        public string DisplayName { get; set; }
        public string NickName { get; set; }
        public string AssetName { get; set; }
    }

    FranchiseEngineClient Context => _engine ??= new FranchiseEngineClient();

    void StudioBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick a franchise save (a COPY will be edited)",
            InitialDirectory = Directory.Exists(M27Saves) ? M27Saves : M26Saves,
            Filter = "Franchise saves (CAREER-*)|CAREER-*|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        StudioPathBox.Text = _studioOrigPath = dlg.FileName;
        StudioStatus.Text = "Click OPEN IN STUDIO to inspect this save (as a copy).";
        StudioSaveBtn.IsEnabled = false;
        TeamGrid.ItemsSource = null;
        _teams.Clear();
    }

    async void StudioOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StudioOpenBtn.IsEnabled = false;
            if (string.IsNullOrEmpty(_studioOrigPath) || !File.Exists(_studioOrigPath))
            {
                StudioStatus.Text = "Pick a franchise save first.";
                return;
            }
            var fn = Path.GetFileName(_studioOrigPath);
            _studioBackupPath = Path.Combine(Path.Combine(Path.GetTempPath(), "FGT"), fn + ".orig.bak");
            _studioWorkPath = Path.Combine(Path.Combine(Path.GetTempPath(), "FGT"), "work-" + fn);
            Directory.CreateDirectory(Path.GetDirectoryName(_studioWorkPath));

            // Game-eligible output name: must be CAREER-<alphanumeric slot>, no extra
            // punctuation. So we sanitize the original slot name to [A-Za-z0-9] and
            // append "Edited" so it stays loadable: CAREER-2027START -> CAREER-2027STARTEdited.
            _studioOutputPath = EligibleOutputPath();

            StudioStatus.Text = "Introspecting save (making a verified copy)…";
            var intro = await Context.IntrospectAsync(_studioOrigPath, _studioWorkPath, _studioBackupPath);
            string gameYear = intro.GetProperty("gameYear").ToString();
            string gameType = intro.GetProperty("gameType").GetString();
            StudioStatus.Text = $"Opened a copy of {fn} (gameYear {gameYear}, type {gameType}). Edit below, then Save.";

            var team = await ReadTeamsAsync();
            _teams.Clear();
            foreach (var row in team.GetProperty("rows").EnumerateArray())
            {
                _teams.Add(new StudioRow
                {
                    DisplayName = ReadString(row, "DisplayName") ?? "",
                    NickName = ReadString(row, "NickName") ?? "",
                    AssetName = ReadString(row, "AssetName") ?? ""
                });
            }
            TeamGrid.ItemsSource = _teams;

            var season = await ReadTable("SeasonInfo", new[] { "CurrentSeasonYear" });
            if (season.TryGetProperty("rows", out var srows) && srows.GetArrayLength() > 0)
            {
                var r0 = srows[0];
                SeasonYearBox.Text = r0.TryGetProperty("CurrentSeasonYear", out var sy) ? sy.ToString() : "";
            }

            StudioSaveBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StudioStatus.Text = "Error opening save: " + ex.Message;
        }
        finally
        {
            StudioOpenBtn.IsEnabled = true;
        }
    }

    async Task<JsonElement> ReadTeamsAsync() =>
        await Context.ReadTableAsync(_studioWorkPath, _studioWorkPath, "Team",
            new[] { "DisplayName", "NickName", "AssetName" });

    async Task<JsonElement> ReadTable(string tableName, string[] fields) =>
        await Context.ReadTableAsync(_studioWorkPath, _studioWorkPath, tableName, fields);

    async void StudioSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StudioSaveBtn.IsEnabled = false;
            StudioStatus.Text = "Saving changed copy…";
            var result = await SaveFullAsync();
            int edited = result.GetProperty("edited").GetInt32();
            bool reloaded = result.TryGetProperty("reloadedOk", out var rl) && rl.GetBoolean();
            StudioSaveBtn.IsEnabled = true;
            StudioStatus.Text = $"Saved a copy to your saves folder: {_studioOutputPath}  ({edited} edits, reloaded={reloaded}). Load it in-game to verify.";
        }
        catch (Exception ex)
        {
            StudioStatus.Text = "Error saving copy: " + ex.Message;
            StudioSaveBtn.IsEnabled = true;
        }
    }

    // Build and apply the FULL current state (all team grid edits + season year) from the
    // original to the game-eligible output path in one atomic call, so saves never clobber
    // each other.
    async Task<JsonElement> SaveFullAsync()
    {
        var edits = new List<object>();
        foreach (var t in _teams)
        {
            edits.Add(new { match = new { field = "AssetName", value = t.AssetName }, field = "DisplayName", value = t.DisplayName });
            edits.Add(new { match = new { field = "AssetName", value = t.AssetName }, field = "NickName", value = t.NickName });
        }
        if (int.TryParse(SeasonYearBox.Text, out int seasonYear))
            edits.Add(new { field = "CurrentSeasonYear", value = seasonYear });
        return await Context.EditTableAsync(_studioOrigPath, _studioOutputPath, "Team", edits, _studioBackupPath);
    }

    async void ApplySeason_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplySeasonBtn.IsEnabled = false;
            if (!int.TryParse(SeasonYearBox.Text, out _))
            {
                StudioStatus.Text = "Season year must be a number.";
                return;
            }
            // The season field is saved together with team edits by SaveFullAsync; this just
            // persists everything now.
            var result = await SaveFullAsync();
            int edited = result.GetProperty("edited").GetInt32();
            StudioStatus.Text = $"Saved a copy to your saves folder: {_studioOutputPath}  ({edited} edits). Load it in-game to verify.";
        }
        catch (Exception ex)
        {
            StudioStatus.Text = "Error setting season year: " + ex.Message;
        }
        finally
        {
            ApplySeasonBtn.IsEnabled = true;
            StudioSaveBtn.IsEnabled = true;
        }
    }

    static string ReadString(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    // Build a game-eligible output filename in the original's saves folder. In-game
    // franchise saves are only recognized as "CAREER-<alphanumeric slot name>" with NO
    // extra punctuation (no underscore/hyphen/space). We strip everything to letters and
    // digits from the original slot, guard length, and append "Edited".
    string EligibleOutputPath()
    {
        var dir = Path.GetDirectoryName(_studioOrigPath) ?? ".";
        var slot = Path.GetFileNameWithoutExtension(_studioOrigPath);
        int dash = slot.IndexOf('-');
        var name = dash >= 0 ? slot.Substring(dash + 1) : slot;
        var clean = new string(name.Where(char.IsLetterOrDigit).ToArray());
        const int maxSlot = 20;
        if (clean.Length > maxSlot) clean = clean.Substring(0, maxSlot);
        clean += "Edited";
        return Path.Combine(dir, "CAREER-" + clean);
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _bgm?.Stop(); } catch { }
        try { _engine?.Dispose(); } catch { }
        base.OnClosed(e);
    }
}
