using System.Text.Json;

namespace MatGame.Local;

/// <summary>
/// The offline side of a game inside the app (MatGame.Shell): what the server does online, done on the device. The browser part of the game talks to it
/// through the shell's bridge with the same calls it makes online (sdk/js/transport.js, <c>LocalTransport</c>):
/// <list type="bullet">
/// <item><see cref="RequestAsync"/> answers what online is the game API (<c>GET /api/home</c>, <c>POST /api/matches/quick</c> ...),</item>
/// <item><see cref="InvokeAsync"/> answers what online is a hub method (<c>Join</c>, <c>Fire</c> ...),</item>
/// <item><see cref="ILocalHost.Emit"/> sends what online the hub sends (<c>Turn</c>, <c>Shot</c> ...).</item>
/// </list>
/// A game without server logic offline (KingBee: the simulation runs in the browser) needs no <see cref="ILocalGame"/>.
/// </summary>
public interface ILocalGame : IAsyncDisposable
{
    /// <summary>The game id (<c>bunnywarfare</c>).</summary>
    string GameId { get; }

    /// <summary>Called once when the shell starts the game.</summary>
    Task StartAsync(ILocalHost host, CancellationToken ct);

    /// <summary>A request of the game API: method (GET/POST/PUT/DELETE), path below the game's API (<c>/home</c>), the JSON body or null. Returns the JSON answer.</summary>
    Task<LocalResponse> RequestAsync(string method, string path, JsonElement? body, CancellationToken ct);

    /// <summary>A hub method with its arguments. Returns its result (or null).</summary>
    Task<JsonElement?> InvokeAsync(string method, IReadOnlyList<JsonElement> args, CancellationToken ct);
}

/// <summary>The answer of a local API request (status like HTTP: 200, 400 with <c>{ error, field }</c>, 404 ...).</summary>
public sealed record LocalResponse(int Status, JsonElement? Body)
{
    public static LocalResponse Ok(object? value) => new(200, value is null ? null : JsonSerializer.SerializeToElement(value, LocalJson.Options));

    public static LocalResponse Error(int status, string message, string? field = null) =>
        new(status, JsonSerializer.SerializeToElement(new { error = message, field }, LocalJson.Options));
}

/// <summary>What the shell gives a local game.</summary>
public interface ILocalHost
{
    /// <summary>The offline profile and the game's files on the device (<c>%APPDATA%\MatGame\&lt;game&gt;</c>).</summary>
    string DataDirectory { get; }

    /// <summary>The name of the local player (the guest name the player chose; "Gast" by default).</summary>
    string PlayerName { get; }

    /// <summary>Sends a hub message to the browser part (<c>Turn</c>, <c>Shot</c> ...), like <c>Clients.Caller.SendAsync(name, args)</c> online.</summary>
    void Emit(string name, params object?[] args);

    /// <summary>Writes to the shell's log.</summary>
    void Log(string message);
}

/// <summary>The JSON rules of the bridge: camelCase like ASP.NET Core and SignalR, so the browser part reads the same shapes offline and online.</summary>
public static class LocalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
