using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;

namespace MatGame.Web;

/// <summary><c>GET /.well-known/matgame</c>: who this master is and which games it has (docs/PROTOCOL.md "Discovery"). Anonymous, no secrets, rate-limited.</summary>
public static class Discovery
{
    public static void MapDiscovery(this WebApplication app)
    {
        app.MapGet(MatGameProtocol.DiscoveryPath, async (AppDbContext db, GameHost host, HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Ok(await DocumentAsync(db, host, http.RequestAborted));
            })
            .RequireCors(MatGameProtocol.AppCorsPolicy)
            .RequireRateLimiting(RateLimits.Discovery);
    }

    public static async Task<object> DocumentAsync(AppDbContext db, GameHost host, CancellationToken ct)
    {
        var settings = await SettingLogic.GetAsync(db, ct);
        var states = await host.StatesAsync(ct);
        return new
        {
            kind = "matgame",
            protocol = MatGameProtocol.Version,
            name = settings.Name,
            version = AppInfo.Version,
            registration = await AccountLogic.CanRegisterAsync(db, ct),
            guests = settings.GuestsAllowed,
            moved = settings.MovedTo,
            games = host.Games.Select(g =>
            {
                var state = states[g.Id];
                return new
                {
                    id = g.Id,
                    name = g.Info.Name,
                    tagline = g.Info.Tagline,
                    path = g.Environment.BasePath,
                    protocol = g.Info.Protocol,
                    minClient = state.MinClient,
                    accent = g.Info.Accent,
                    cover = GameHost.Asset(g, g.Info.Cover),
                    icon = GameHost.Asset(g, g.Info.Icon),
                    status = state.IsEnabled ? "ok" : "off",
                };
            }),
        };
    }
}
