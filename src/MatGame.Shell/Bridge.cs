using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MatGame.Local;

namespace MatGame.Shell;

/// <summary>
/// The bridge between the page and the shell (MatGame docs/PROTOCOL.md "Bridge"): page → shell <c>{ id, target, method, args }</c>,
/// shell → page <c>{ id, ok, result | error, status, body }</c> and events <c>{ event, name, args }</c>. Independent of WebView2 (tests call it directly):
/// <see cref="Post"/> sends a JSON text to the page.
/// </summary>
public sealed class Bridge : IAsyncDisposable
{
    private readonly ShellOptions _options;
    private readonly ShellStorage _storage;
    private readonly Action<string> _post;
    private readonly Action<bool> _fullscreen;
    private readonly Action _quit;
    private ILocalGame? _local;
    private Task? _localStart;

    public Bridge(ShellOptions options, ShellStorage storage, Action<string> post, Action<bool> fullscreen, Action quit)
    {
        _options = options;
        _storage = storage;
        _post = post;
        _fullscreen = fullscreen;
        _quit = quit;
    }

    /// <summary>Sends a JSON text to the page (set by the window; on the UI thread).</summary>
    public Action<string> Post => _post;

    /// <summary>Handles one message of the page and answers it (never throws: an error becomes <c>ok: false</c>).</summary>
    public async Task HandleAsync(string json)
    {
        long id = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            id = root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var n) ? n : 0;
            var target = root.GetProperty("target").GetString() ?? string.Empty;
            var method = root.GetProperty("method").GetString() ?? string.Empty;
            var args = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(e => e.Clone()).ToList()
                : [];
            var result = await DispatchAsync(target, method, args);
            Reply(new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result });
        }
        catch (LocalException e)
        {
            Reply(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e.Message, ["status"] = e.Status, ["body"] = e.Body });
        }
        catch (Exception e)
        {
            ShellLog.Write($"bridge error: {e}");
            Reply(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e is BridgeException ? e.Message : "Fehler in der App.", ["status"] = 500 });
        }
    }

    private void Reply(JsonObject message) => _post(message.ToJsonString());

    /// <summary>A hub message of the offline game to the page.</summary>
    public void Emit(string name, object?[] args)
    {
        var array = new JsonArray(args.Select(a => (JsonNode?)JsonSerializer.SerializeToNode(a, LocalJson.Options)).ToArray());
        _post(new JsonObject { ["event"] = "local", ["name"] = name, ["args"] = array }.ToJsonString());
    }

    private async Task<JsonNode?> DispatchAsync(string target, string method, List<JsonElement> args)
    {
        string? Arg(int i) => i < args.Count && args[i].ValueKind == JsonValueKind.String ? args[i].GetString() : null;

        switch (target, method)
        {
            case ("app", "info"):
                return new JsonObject
                {
                    ["platform"] = "windows",
                    ["game"] = _options.GameId,
                    ["version"] = _options.Version,
                    ["defaultServer"] = _options.DefaultServer,
                    ["channel"] = _options.Channel,
                };
            case ("app", "openBrowser"):
                OpenBrowser(Arg(0));
                return null;
            case ("app", "fullscreen"):
                _fullscreen(args.Count > 0 && args[0].ValueKind == JsonValueKind.True);
                return null;
            case ("app", "quit"):
                _quit();
                return null;
            case ("storage", "get"):
                return _storage.Get(Key(Arg(0)));
            case ("storage", "set"):
                _storage.Set(Key(Arg(0)), Arg(1));
                return null;
            case ("storage", "remove"):
                _storage.Set(Key(Arg(0)), null);
                return null;
            case ("secure", "get"):
                return _storage.GetSecret(Key(Arg(0)));
            case ("secure", "set"):
                _storage.SetSecret(Key(Arg(0)), Arg(1));
                return null;
            case ("secure", "remove"):
                _storage.SetSecret(Key(Arg(0)), null);
                return null;
            case ("local", "start"):
                await StartLocalAsync();
                return null;
            case ("local", "request"):
                return await RequestAsync(Arg(0) ?? "GET", Arg(1) ?? "/", args.Count > 2 && args[2].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? args[2] : null);
            case ("local", "invoke"):
                return await InvokeAsync(Arg(0) ?? string.Empty, args.Count > 1 && args[1].ValueKind == JsonValueKind.Array ? args[1].EnumerateArray().Select(e => e.Clone()).ToList() : []);
            default:
                throw new BridgeException($"Unbekannter Aufruf {target}.{method}.");
        }
    }

    private static string Key(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 200 ? throw new BridgeException("Ungültiger Schlüssel.") : key;

    private static void OpenBrowser(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new BridgeException("Nur http- und https-Adressen.");
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private Task StartLocalAsync()
    {
        if (_options.LocalGame is null)
        {
            throw new BridgeException("Dieses Spiel braucht offline keinen Spielkern.");
        }

        return _localStart ??= StartLocalCoreAsync();
    }

    private async Task StartLocalCoreAsync()
    {
        _local = _options.LocalGame!();
        await _local.StartAsync(new LocalGameHost(_options, this), CancellationToken.None);
        ShellLog.Write($"local game {_local.GameId} started");
    }

    private async Task<JsonNode?> RequestAsync(string method, string path, JsonElement? body)
    {
        await StartLocalAsync();
        var response = await _local!.RequestAsync(method.ToUpperInvariant(), path, body, CancellationToken.None);
        return new JsonObject
        {
            ["status"] = response.Status,
            ["body"] = response.Body is null ? null : JsonNode.Parse(response.Body.Value.GetRawText()),
        };
    }

    private async Task<JsonNode?> InvokeAsync(string method, List<JsonElement> args)
    {
        await StartLocalAsync();
        try
        {
            var result = await _local!.InvokeAsync(method, args, CancellationToken.None);
            return result is null ? null : JsonNode.Parse(result.Value.GetRawText());
        }
        catch (Exception e) when (e is not BridgeException)
        {
            // the same text SignalR's client shows for a HubException online, so the game reads the message the same way
            throw new LocalException($"An unexpected error occurred invoking '{method}' on the server. HubException: {e.Message}", 400, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_local is not null)
        {
            await _local.DisposeAsync();
        }
    }

    private sealed class BridgeException(string message) : Exception(message);

    private sealed class LocalException(string message, int status, JsonNode? body) : Exception(message)
    {
        public int Status { get; } = status;

        public JsonNode? Body { get; } = body;
    }
}

/// <summary>What the offline game gets from the shell.</summary>
internal sealed class LocalGameHost(ShellOptions options, Bridge bridge) : ILocalHost
{
    public string DataDirectory { get; } = Directory.CreateDirectory(Path.Combine(options.DataDirectory, "offline")).FullName;

    public string PlayerName => "Gast";

    public void Emit(string name, params object?[] args) => bridge.Emit(name, args);

    public void Log(string message) => ShellLog.Write($"[{options.GameId}] {message}");
}
