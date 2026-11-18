using MatGame.Sdk;

namespace MatGame.Tests.SampleGame;

/// <summary>The sample game: <c>/sample/</c> (its page), <c>/sample/api/ping</c> (who am I), <c>/Admin/Sample</c> (its admin page).</summary>
public sealed class SampleModule : IGameModule
{
    public static int AccountsDeleted;

    public GameInfo Info { get; } = new("sample", "Beispielspiel", "Ein Spiel für die Tests.", "1.0.0", 3, "1.2.0", "#0f766e", "img/cover.svg", "img/cover.svg");

    public IReadOnlyList<AdminLink> Admin { get; } = [new("Beispiel", "/Admin/Sample", "sample")];

    public void AddServices(IServiceCollection services, GameEnvironment environment)
    {
        services.AddSingleton(new SampleState(environment));
    }

    public void MapGame(IEndpointRouteBuilder game, GameEnvironment environment)
    {
        game.MapGet("/", () => Results.Redirect($"{environment.BasePath}/index.html"));
        var api = game.MapGroup("/api").ForGame(Info);
        api.MapGet("/ping", (HttpContext http) => Results.Ok(new { account = http.User.ToAccount(), device = http.User.IsDevice(), basePath = environment.BasePath }));
        api.MapPost("/echo", (Echo body) => Results.Ok(body));
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        services.GetRequiredService<SampleState>().Started = true;
        return Task.CompletedTask;
    }

    public Task<GameStats> StatsAsync(IServiceProvider services, CancellationToken ct) => Task.FromResult(new GameStats(2, 1));

    public Task<GameProfile?> ProfileAsync(IServiceProvider services, long accountId, CancellationToken ct) => Task.FromResult<GameProfile?>(new GameProfile(4, "Testprofil"));

    public Task AccountDeletedAsync(IServiceProvider services, long accountId, CancellationToken ct)
    {
        Interlocked.Increment(ref AccountsDeleted);
        return Task.CompletedTask;
    }

    public sealed record Echo(string? Text);
}

/// <summary>What the host gave the sample game.</summary>
public sealed class SampleState(GameEnvironment environment)
{
    public GameEnvironment Environment { get; } = environment;

    public bool Started { get; set; }
}
