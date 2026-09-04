using System.Diagnostics;
using System.Text;
using System.Text.Json;

#nullable enable

namespace MaddenSaveConverter.Franchise;

/// <summary>
/// C# client for the Node <c>FranchiseEngine/engine.mjs</c> JSON-lines service.
/// Spawns one long-lived <c>node</c> process and sends one JSON request per line,
/// awaiting one JSON response per line. All franchise editing is done by the Node
/// service against COPIES of saves (originals are never touched).
/// </summary>
public sealed class FranchiseEngineClient : IDisposable
{
    /// <summary>Pre-resolved path to node.exe, or null to auto-probe.</summary>
    public static string? NodePathOverride { get; set; }

    public static string? EnginePathOverride { get; set; }

    public string NodePath { get; }
    public string EnginePath { get; }

    private readonly Process _proc;
    private readonly StreamWriter _stdin;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _sendLock = new();
    private int _nextId;
    private bool _disposed;

    public FranchiseEngineClient()
    {
        NodePath = ResolveNode();
        EnginePath = ResolveEngine();
        var psi = new ProcessStartInfo
        {
            FileName = NodePath,
            Arguments = '"' + EnginePath + '"',
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(EnginePath) ?? string.Empty
        };
        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!_proc.Start())
            throw new InvalidOperationException($"Failed to start node ({NodePath})");
        _stdin = _proc.StandardInput;
        _stdin.AutoFlush = true;
        _stdin.NewLine = "\n";
        var stderr = _proc.StandardError;
        var _ = Task.Run(() =>
        {
            try { stderr.ReadToEnd(); } catch { /* ignored */ }
        });
        // Read responses line-by-line on the background.
        var stdout = _proc.StandardOutput;
        var _stderr = Task.Run(() => ReadLoopAsync(stdout));
    }

    /// <summary>Send a request and await the matching response.</summary>
    public Task<JsonElement> RequestAsync(string cmd, object? args, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["cmd"] = cmd,
            ["args"] = args
        };
        try
        {
            lock (_sendLock)
            {
                _stdin.WriteLine(JsonSerializer.Serialize(payload));
                _stdin.Flush();
            }
        }
        catch (Exception ex)
        {
            lock (_pending) _pending.Remove(id);
            throw new InvalidOperationException("Failed to write to node engine: " + ex.Message, ex);
        }
        return tcs.Task;
    }

    private async Task ReadLoopAsync(StreamReader stdout)
    {
        try
        {
            string? line;
            while ((line = await stdout.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement.Clone();
                    int id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : -1;
                    if (!TryTake(id, out var tcs)) continue;

                    if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                    {
                        var result = root.TryGetProperty("data", out var d) ? d.Clone() : default;
                        tcs.SetResult(result);
                    }
                    else
                    {
                        var err = root.TryGetProperty("error", out var e) ? e.GetString() : "engine error";
                        tcs.SetException(new InvalidOperationException("Node engine: " + err));
                    }
                }
                catch (JsonException) { /* ignore malformed line */ }
            }
        }
        catch { /* engine exited */ }
        finally
        {
            // Fail anything still waiting.
            lock (_pending)
            {
                foreach (var tcs in _pending.Values)
                    tcs.TrySetException(new InvalidOperationException("Node engine exited"));
                _pending.Clear();
            }
        }
    }

    private bool TryTake(int id, out TaskCompletionSource<JsonElement> tcs)
    {
        lock (_pending) return _pending.Remove(id, out tcs!);
    }

    public async Task<JsonElement> PingAsync() => await RequestAsync("ping", null);

    /// <summary>Open a copy of a save and report game meta + table counts.</summary>
    public Task<JsonElement> IntrospectAsync(string src, string workPath, string backupPath) =>
        RequestAsync("introspect", new { src, workPath, backupPath });

    /// <summary>Read records from a table for display.</summary>
    public Task<JsonElement> ReadTableAsync(string src, string workPath, string tableName, string[]? fields, int? tableIndex = null) =>
        RequestAsync("readTable", new { src, workPath, tableName, fields, tableIndex });

    /// <summary>Edit matching records then save to workPath and reload-verify.</summary>
    public Task<JsonElement> EditTableAsync(string src, string workPath, string tableName, object edits, string? backupPath = null) =>
        RequestAsync("editTable", new { src, workPath, backupPath, tableName, edits });

    public Task<JsonElement> SaveRoundTripAsync(string src, string workPath, string outPath) =>
        RequestAsync("saveRoundTrip", new { src, workPath, outPath });

    private static string ResolveNode()
    {
        if (NodePathOverride != null && File.Exists(NodePathOverride)) return NodePathOverride;
        var env = Environment.GetEnvironmentVariable("USE_NODE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var candidates = new[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        // Last resort: rely on PATH.
        return "node";
    }

    private static string ResolveEngine()
    {
        if (EnginePathOverride != null && File.Exists(EnginePathOverride)) return EnginePathOverride;
        var local = Path.Combine(AppContext.BaseDirectory, "FranchiseEngine", "engine.mjs");
        if (File.Exists(local)) return local;
        var dev = Path.Combine(Directory.GetCurrentDirectory(), "FranchiseEngine", "engine.mjs");
        if (File.Exists(dev)) return dev;
        throw new FileNotFoundException("Cannot locate FranchiseEngine/engine.mjs next to the app or in the working directory.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stdin.Close(); } catch { /* ignored */ }
        try { _proc.Kill(); } catch { /* ignored */ }
        try { _proc.Dispose(); } catch { /* ignored */ }
    }
}