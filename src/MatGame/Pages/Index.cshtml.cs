using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using MatGame.Web;

namespace MatGame.Pages;

/// <summary>A game card of the portal.</summary>
public sealed record PortalGame(string Id, string Name, string Tagline, string Path, string Accent, string? Cover, string? Icon, bool IsEnabled, GameStats Stats, GameProfile? Profile);

/// <summary>The portal: the games of this master, the sign-in (account or guest) and the profile.</summary>
public sealed class IndexModel(AppDbContext db, GameHost host) : HostPageModel(db)
{
    public ServerSettings Settings { get; private set; } = ServerSettings.Default;

    public IReadOnlyList<PortalGame> Games { get; private set; } = [];

    public Data.Account? Account { get; private set; }

    public IReadOnlyList<DeviceRow> Devices { get; private set; } = [];

    public bool CanRegister { get; private set; }

    public bool FirstRun { get; private set; }

    public bool IdentityEnabled { get; private set; }

    public string? ReturnUrl { get; private set; }

    /// <summary>Which panel opens at once: "login", "register", "guest", "profile" (from the query), or null.</summary>
    public string? OpenPanel { get; private set; }

    public async Task OnGetAsync(string? returnUrl, string? login, string? profile, string? register)
    {
        Settings = await SettingLogic.GetAsync(Db);
        CanRegister = await AccountLogic.CanRegisterAsync(Db);
        FirstRun = !await AccountLogic.HasAnyAsync(Db);
        IdentityEnabled = HttpContext.RequestServices.GetService<MatPlatform.MatPlatformSettings>()?.IdentityEnabled == true;
        ReturnUrl = returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : null;

        var me = Me;
        Account = me.IsSignedIn ? await AccountLogic.FindActiveAsync(Db, me.AccountId) : null;
        var states = await host.StatesAsync(HttpContext.RequestAborted);
        var stats = await host.StatsAsync(HttpContext.RequestServices, HttpContext.RequestAborted);
        var profiles = Account is null ? null : await host.ProfilesAsync(HttpContext.RequestServices, Account.Id, HttpContext.RequestAborted);
        Games = host.Games.Select(g => new PortalGame(g.Id, g.Info.Name, g.Info.Tagline, g.Environment.BasePath + "/", g.Info.Accent,
            GameHost.Asset(g, g.Info.Cover), GameHost.Asset(g, g.Info.Icon), states[g.Id].IsEnabled, stats[g.Id], profiles?.GetValueOrDefault(g.Id))).ToList();
        if (Account is not null)
        {
            Devices = await DeviceLogic.ListOwnAsync(Db, me);
        }

        ViewData["ServerName"] = Settings.Name;
        OpenPanel = Account is null
            ? (register is not null || FirstRun ? "register" : login is not null || ReturnUrl is not null ? "login" : null)
            : (profile is not null ? "profile" : null);
    }
}
