using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Web;

/// <summary>A game the host runs: the plugin and where it lives.</summary>
public sealed record HostedGame(IGameModule Module, GameEnvironment Environment)
{
    public GameInfo Info => Module.Info;

    public string Id => Module.Info.Id;
}

/// <summary>
/// The games (plugins) of this host: chosen from <see cref="Games.All"/> by <c>MATGAME_GAMES</c>, each with its <see cref="GameEnvironment"/>, and the administration's
/// decisions about them (on/off, minimum app version), cached for the requests (refreshed after a change and every 30 seconds).
/// </summary>
public sealed class GameHost
{
    private readonly IServiceScopeFactory scopes;
    private IReadOnlyDictionary<string, GameState> states = new Dictionary<string, GameState>();
    private DateTime loaded = DateTime.MinValue;

    public GameHost(IReadOnlyList<HostedGame> games, IServiceScopeFactory scopes)
    {
        Games = games;
        this.scopes = scopes;
        states = games.ToDictionary(g => g.Id, g => new GameState(g.Info, true, null));
    }

    public IReadOnlyList<HostedGame> Games { get; }

    /// <summary>The games of <paramref name="modules"/> that <paramref name="filter"/> (<c>MATGAME_GAMES</c>: "bunnywarfare,kingbee"; empty = all) lets in.</summary>
    public static IReadOnlyList<IGameModule> Choose(IEnumerable<IGameModule> modules, string? filter)
    {
        var all = modules.ToList();
        if (string.IsNullOrWhiteSpace(filter) || filter.Trim() == "*")
        {
            return all;
        }

        var wanted = filter.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToLowerInvariant()).ToHashSet();
        return all.Where(m => wanted.Contains(m.Info.Id)).ToList();
    }

    public HostedGame? Find(string? id) => Games.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game whose address starts the path (<c>/bunnywarfare/...</c>), or null.</summary>
    public HostedGame? FindByPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        var end = value.IndexOf('/', 1);
        var first = end < 0 ? value.TrimStart('/') : value[1..end];
        return first.Length == 0 ? null : Find(first);
    }

    public async Task<IReadOnlyDictionary<string, GameState>> StatesAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - loaded > TimeSpan.FromSeconds(30))
        {
            await RefreshAsync(ct);
        }

        return states;
    }

    public async Task<GameState> StateAsync(HostedGame game, CancellationToken ct = default) => (await StatesAsync(ct))[game.Id];

    /// <summary>Reads the administration's decisions again (after a change in the admin).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var list = await GameLogic.StatesAsync(db, Games.Select(g => g.Info), ct);
            states = list.ToDictionary(s => s.Info.Id);
            loaded = DateTime.UtcNow;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // the database is not there yet (start-up): keep the last decisions
        }
    }

    /// <summary>The numbers of every game (a game that fails counts as 0).</summary>
    public async Task<IReadOnlyDictionary<string, GameStats>> StatsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var result = new Dictionary<string, GameStats>();
        foreach (var game in Games)
        {
            try
            {
                result[game.Id] = await game.Module.StatsAsync(services, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                services.GetRequiredService<ILogger<GameHost>>().LogWarning(e, "The numbers of {Game} failed", game.Id);
                result[game.Id] = new GameStats(0, 0);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, GameProfile?>> ProfilesAsync(IServiceProvider services, long accountId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, GameProfile?>();
        foreach (var game in Games)
        {
            try
            {
                result[game.Id] = await game.Module.ProfileAsync(services, accountId, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                services.GetRequiredService<ILogger<GameHost>>().LogWarning(e, "The profile of {Game} failed", game.Id);
                result[game.Id] = null;
            }
        }

        return result;
    }

    /// <summary>Tells every game that an account is gone.</summary>
    public async Task AccountDeletedAsync(IServiceProvider services, long accountId, CancellationToken ct = default)
    {
        foreach (var game in Games)
        {
            try
            {
                await game.Module.AccountDeletedAsync(services, accountId, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                services.GetRequiredService<ILogger<GameHost>>().LogError(e, "{Game} could not remove the data of account {Account}", game.Id, accountId);
            }
        }
    }

    /// <summary>An address of a game made absolute below its path ("img/cover.jpg" → "/bunnywarfare/img/cover.jpg"); absolute ones stay.</summary>
    public static string? Asset(HostedGame game, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        if (relative.StartsWith('/') || relative.Contains("://", StringComparison.Ordinal))
        {
            return relative;
        }

        return $"{game.Environment.BasePath}/{relative}";
    }
}

/// <summary>Registers the games with the host and starts them.</summary>
public static class GameHostSetup
{
    public static GameHostRegistration AddGames(this WebApplicationBuilder builder, string connection, string dataDirectory)
    {
        var modules = GameHost.Choose(Games.All(), builder.Configuration["MATGAME_GAMES"]);
        var hosted = new List<HostedGame>();
        foreach (var module in modules)
        {
            var id = module.Info.Id;
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9]{1,30}$") || id is "api" or "admin" or "account" or "link" or "lib" or "css" or "js" or "img" or "fonts" or "matui")
            {
                throw new InvalidOperationException($"The game id '{id}' is not allowed (lower case letters and digits, not a path of the host).");
            }

            var folder = Path.Combine(dataDirectory, "games", id);
            Directory.CreateDirectory(folder);
            var environment = new GameEnvironment(connection, folder, "/" + id, builder.Environment.IsDevelopment());
            module.AddServices(builder.Services, environment);

            // its Razor pages (the admin pages): an application part also when only the tests reference the plugin
            var assembly = module.GetType().Assembly;
            builder.Services.AddRazorPages().ConfigureApplicationPartManager(parts =>
            {
                if (!parts.ApplicationParts.Any(p => p.Name == assembly.GetName().Name))
                {
                    foreach (var part in Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                    {
                        parts.ApplicationParts.Add(part);
                    }
                }
            });
            hosted.Add(new HostedGame(module, environment));
        }

        builder.Services.AddSingleton(sp => new GameHost(hosted, sp.GetRequiredService<IServiceScopeFactory>()));
        return new GameHostRegistration(hosted);
    }

    /// <summary>Starts every game (after the host's migration) and maps its routes under its path.</summary>
    public static async Task StartGamesAsync(this WebApplication app)
    {
        var host = app.Services.GetRequiredService<GameHost>();
        var log = app.Services.GetRequiredService<ILogger<GameHost>>();
        foreach (var game in host.Games)
        {
            using var scope = app.Services.CreateScope();
            await game.Module.StartAsync(scope.ServiceProvider, app.Lifetime.ApplicationStopping);
            log.LogInformation("Game {Game} {Version} started at {Path}", game.Id, game.Info.Version, game.Environment.BasePath);
        }

        await host.RefreshAsync();
    }

    public static void MapGames(this WebApplication app)
    {
        foreach (var game in app.Services.GetRequiredService<GameHost>().Games)
        {
            var group = app.MapGroup(game.Environment.BasePath);
            game.Module.MapGame(group, game.Environment);
        }
    }
}

/// <summary>What <see cref="GameHostSetup.AddGames"/> registered (for the admin's menu before the container is built).</summary>
public sealed record GameHostRegistration(IReadOnlyList<HostedGame> Games);

/// <summary>
/// Before a game's routes: a switched-off game answers with a friendly page (or 404 JSON for scripts), and an app too old for the game (the administration's
/// minimum) gets 426 on the game's API and hub.
/// </summary>
public sealed class GameGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GameHost host)
    {
        var game = host.FindByPath(context.Request.Path);
        if (game is null)
        {
            await next(context);
            return;
        }

        var state = await host.StateAsync(game, context.RequestAborted);
        if (!state.IsEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            if (Identity.IsMachineRequest(context.Request))
            {
                await context.Response.WriteAsJsonAsync(new { error = $"{game.Info.Name} ist auf diesem Server abgeschaltet.", off = true });
            }
            else
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(OffPage(game.Info.Name));
            }

            return;
        }

        if (Identity.IsMachineRequest(context.Request) && !HttpMethods.IsOptions(context.Request.Method) && GameApi.ClientProblem(context.Request, state.Effective) is { } problem)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new { error = problem, update = true });
            return;
        }

        await next(context);
    }

    private static string OffPage(string name)
    {
        var safe = System.Net.WebUtility.HtmlEncode(name);
        return $$"""
            <!DOCTYPE html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{safe}} · abgeschaltet</title><link rel="stylesheet" href="/css/portal.css"></head>
            <body class="mg-portal"><main class="mg-off"><div class="mg-panel"><h1>{{safe}} ist gerade abgeschaltet</h1>
            <p>Die Administration hat das Spiel auf diesem Server ausgeschaltet. Schau später noch einmal vorbei.</p>
            <a class="mg-btn mg-btn--primary" href="/">Zu den Spielen</a></div></main></body></html>
            """;
    }
}
