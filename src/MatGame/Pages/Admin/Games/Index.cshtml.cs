using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using MatGame.Web;

namespace MatGame.Pages.Admin.Games;

/// <summary>A game in the list of the administration.</summary>
public sealed record GameRow(GameState State, GameStats Stats, string? Cover);

public sealed class IndexModel(AppDbContext db, GameHost host) : HostPageModel(db)
{
    public IReadOnlyList<GameRow> Games { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Business.Guard.Admin(Me);
        var states = await host.StatesAsync(HttpContext.RequestAborted);
        var stats = await host.StatsAsync(HttpContext.RequestServices, HttpContext.RequestAborted);
        Games = host.Games.Select(g => new GameRow(states[g.Id], stats[g.Id], GameHost.Asset(g, g.Info.Cover))).ToList();
    }
}
