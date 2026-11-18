using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using MatGame.Web;

namespace MatGame.Pages.Admin;

/// <summary>A game in the overview: the administration's decisions and its numbers.</summary>
public sealed record GameOverview(GameState State, GameStats Stats);

public sealed class IndexModel(AppDbContext db, GameHost host) : HostPageModel(db)
{
    public int Accounts { get; private set; }

    public int Guests { get; private set; }

    public int Devices { get; private set; }

    public IReadOnlyList<GameOverview> Games { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var numbers = await AdminLogic.NumbersAsync(Db, Me);
        (Accounts, Guests, Devices) = (numbers.Accounts, numbers.Guests, numbers.Devices);
        var states = await host.StatesAsync(HttpContext.RequestAborted);
        var stats = await host.StatsAsync(HttpContext.RequestServices, HttpContext.RequestAborted);
        Games = host.Games.Select(g => new GameOverview(states[g.Id], stats[g.Id])).ToList();
    }
}
