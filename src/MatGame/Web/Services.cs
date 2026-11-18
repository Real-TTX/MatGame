using System.Reflection;
using System.Text;
using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;

namespace MatGame.Web;

/// <summary>The version of the build (release 0.1.42-20261118, nightly-42-20261118 or local-20261118; see Directory.Build.props).</summary>
public static class AppInfo
{
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";

    public static readonly DateTime Started = DateTime.UtcNow;

    public const int Port = 2770;
}

/// <summary><c>dotnet MatGame.dll --healthcheck</c>: asks the running app for /api/health (the image needs no curl). Exit code 0 = healthy.</summary>
public static class HealthCheck
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"http://localhost:{AppInfo.Port}/api/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}

/// <summary>
/// The icon sprites, inline in every admin page for <c>&lt;use href="#mat-i-..."&gt;</c>: the toolkit's, MatGame's, and each game's
/// <c>&lt;game&gt;/img/admin-icons.svg</c> (a plugin's wwwroot/img/admin-icons.svg: symbols named <c>mat-i-…</c>).
/// </summary>
public sealed class Assets
{
    public Assets(IWebHostEnvironment env, GameHost games)
    {
        var sprite = new StringBuilder(Read(env, "matui/matui-icons.svg")).Append(Read(env, "img/app-icons.svg"));
        foreach (var game in games.Games)
        {
            sprite.Append(Read(env, $"{game.Id}/img/admin-icons.svg"));
        }

        Sprite = sprite.ToString();
    }

    public string Sprite { get; }

    private static string Read(IWebHostEnvironment env, string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>What the host tells the plugins about accounts (<see cref="IGameAccounts"/>).</summary>
public sealed class HostGameAccounts(IServiceScopeFactory scopes) : IGameAccounts
{
    public async Task<GameAccount?> FindAsync(long id, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var account = await AccountLogic.FindActiveAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), id, ct);
        return account is null ? null : AccountLogic.ToGameAccount(account);
    }

    public async Task<IReadOnlyDictionary<long, GameAccount>> FindManyAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        return await AccountLogic.FindManyAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ids, ct);
    }

    public async Task<long> ImportAsync(ImportedAccount account, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        return await AccountLogic.ImportAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), account, ct);
    }
}

/// <summary>The hourly housekeeping: old device sign-ins and devices unused for too long.</summary>
public sealed class Housekeeping(IServiceScopeFactory scopes, ILogger<Housekeeping> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var removed = await DeviceLogic.CleanUpAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
                if (removed > 0)
                {
                    log.LogInformation("Housekeeping removed {Count} old device sign-ins and devices", removed);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Housekeeping failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Security headers of every answer.</summary>
public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            headers.XFrameOptions = "SAMEORIGIN";
        }

        return next(context);
    }
}
