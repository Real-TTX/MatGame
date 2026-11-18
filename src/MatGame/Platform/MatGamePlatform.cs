using System.Globalization;
using System.Text.Json;
using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk.Web;
using MatGame.Web;
using MatPlatform;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Platform;

/// <summary>
/// MatGame as an app of the Mat platform contract (MatDesign docs/PLATFORM.md, contract 2, MatPlatform copied into src/MatGame/MatPlatform): how a consumer such as
/// Mattiverse shows it (tiles, the two settings, buttons), what it consumes (the sign-in of MatIdentity), the launcher and two widgets, its values.
/// </summary>
public sealed class MatGamePlatform(AppDbContext db, GameHost games, IServiceProvider services, ILogger<MatGamePlatform> log)
    : IMatPlatformState, IMatPlatformConfiguration, IMatPlatformActions, IMatPlatformHealth
{
    /// <summary>The id of MatGame in the platform (and the default client id at MatIdentity).</summary>
    public const string AppId = "matgame";

    public const string Icon = "/img/logo-mark.svg";

    public static void Describe(MatPlatformBuilder o)
    {
        var T = o.T;
        o.App(AppId, "MatGame", T("Der Server der Mat-Spiele: Portal, Konten, Online-Partien von BunnyWarfare, King Bee und mehr",
            "The server of the Mat games: portal, accounts, online matches of BunnyWarfare, King Bee and more"), AppInfo.Version, Icon, Ui.Accent, "/");

        o.Consumes(MatCapabilities.IdentityOidc, optional: true);

        o.Launcher("matgame", "MatGame", Icon, "/");
        o.QuickAction("matgame", T("Spiele", "Games"), "gamepad", url: "/");
        o.QuickAction("matgame", T("Administration", "Administration"), "settings", url: "/Admin");
        o.Widget(new MatWidget("players", T("Spieler", "Players"), MatWidgetKinds.Tiles, "medium")
        {
            Items = ["playersOnline", "runningMatches", "accounts"],
            Icon = "gamepad",
            Url = "/Admin",
            Audience = MatAudience.Admin,
        });
        o.Widget(new MatWidget("matches", T("Laufende Partien", "Running matches"), MatWidgetKinds.Stat)
        {
            Items = ["runningMatches"],
            Icon = "play",
            Url = "/Admin/Games",
            Audience = MatAudience.Admin,
        });

        o.Page("overview", T("Übersicht", "Overview"), "dashboard",
            MatSection.Tiles("accounts", "guests", "devices", "games", "playersOnline", "runningMatches"),
            MatSection.Actions("openApp", "openAdmin", "openAccounts", "openGames", "cleanUp"));
        o.Page("settings", T("Einstellungen", "Settings"), "settings",
            MatSection.Form(T("Anmeldung", "Sign-in"), "registrationOpen", "guestsAllowed"));

        o.Tile("accounts", T("Konten", "Accounts"), icon: "users");
        o.Tile("guests", T("Gäste", "Guests"), icon: "user");
        o.Tile("devices", T("Geräte", "Devices"), icon: "smartphone");
        o.Tile("games", T("Spiele an", "Games on"), icon: "gamepad");
        o.Tile("playersOnline", T("Spieler online", "Players online"), icon: "activity");
        o.Tile("runningMatches", T("Laufende Partien", "Running matches"), icon: "play");

        o.Field(new MatField("registrationOpen", T("Registrierung offen", "Registration open"), "switch")
        {
            Hint = T("Neue Spieler legen selbst ein Konto an.", "New players create an account themselves."),
        });
        o.Field(new MatField("guestsAllowed", T("Gäste erlaubt", "Guests allowed"), "switch")
        {
            Hint = T("Online ohne Konto spielen (ohne Rangliste).", "Play online without an account (no ranking)."),
        });

        o.LinkAction("openApp", T("Öffnen", "Open"), "external", "/");
        o.LinkAction("openAdmin", T("Administration", "Administration"), "settings", "/Admin");
        o.LinkAction("openAccounts", T("Konten", "Accounts"), "users", "/Admin/Accounts");
        o.LinkAction("openGames", T("Spiele", "Games"), "gamepad", "/Admin/Games");
        o.PostAction("cleanUp", T("Aufräumen", "Clean up"), "refresh",
            T("Abgelaufene Geräte-Anmeldungen und lange unbenutzte Geräte jetzt entfernen?", "Remove expired device sign-ins and devices unused for long now?"));

        o.Role("admin", T("Administrator", "Administrator"));
        o.Role("player", T("Spieler", "Player"));
    }

    public async Task<IReadOnlyList<MatHealthCheck>> CheckHealthAsync(MatPlatformCall call, CancellationToken ct)
    {
        if (!await db.Database.CanConnectAsync(ct))
        {
            return [new MatHealthCheck("database", "down", call.T("Die Datenbank ist nicht erreichbar.", "The database cannot be reached."))];
        }

        return [new MatHealthCheck("database", "ok")];
    }

    public async Task<IReadOnlyDictionary<string, MatStateValue>> GetStateAsync(MatPlatformCall call, CancellationToken ct)
    {
        var accounts = await db.Accounts.CountAsync(a => a.UpdateState != UpdateState.Deleted && !a.IsGuest, ct);
        var guests = await db.Accounts.CountAsync(a => a.UpdateState != UpdateState.Deleted && a.IsGuest, ct);
        var devices = await db.Devices.CountAsync(ct);
        var states = await games.StatesAsync(ct);
        var stats = await games.StatsAsync(services, ct);
        return new Dictionary<string, MatStateValue>
        {
            ["accounts"] = new(accounts),
            ["guests"] = new(guests),
            ["devices"] = new(devices),
            ["games"] = new(states.Values.Count(s => s.IsEnabled)),
            ["playersOnline"] = new(stats.Values.Sum(s => s.PlayersOnline)),
            ["runningMatches"] = new(stats.Values.Sum(s => s.RunningMatches)),
        };
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetConfigurationAsync(MatPlatformCall call, CancellationToken ct)
    {
        var settings = await SettingLogic.GetAsync(db, ct);
        return new Dictionary<string, object?> { ["registrationOpen"] = settings.RegistrationOpen, ["guestsAllowed"] = settings.GuestsAllowed };
    }

    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, MatPlatformCall call, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        bool? registration = null;
        bool? guests = null;
        foreach (var (key, value) in values)
        {
            var flag = value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => (bool?)null };
            if (flag is null || key is not ("registrationOpen" or "guestsAllowed"))
            {
                errors[key] = call.T("Dieser Wert lässt sich hier nicht ändern.", "This value cannot be changed here.");
                continue;
            }

            if (key == "registrationOpen")
            {
                registration = flag;
            }
            else
            {
                guests = flag;
            }
        }

        if (errors.Count == 0)
        {
            await SettingLogic.SetFlagsAsync(db, Actor.System, registration, guests, ct);
            log.LogInformation("Settings changed through the platform API by {Caller}: {Keys}", call.Caller, string.Join(", ", values.Keys));
        }

        return errors;
    }

    public async Task<MatActionResult?> RunActionAsync(string id, MatPlatformCall call, CancellationToken ct)
    {
        if (id != "cleanUp")
        {
            return null;
        }

        var removed = await DeviceLogic.CleanUpAsync(db, ct);
        return new MatActionResult(true, call.T(
            string.Create(CultureInfo.InvariantCulture, $"Aufgeräumt: {removed} Einträge."),
            string.Create(CultureInfo.InvariantCulture, $"Cleaned up: {removed} entries.")));
    }
}

/// <summary>
/// MatGame's side of the sign-in through MatIdentity: the person becomes an account (by <c>sub</c>, else a new one; the claim <c>role</c> when it is there) and is
/// signed in with MatGame's cookie. A local account with the same login name is linked only after its password (<see cref="MatIdentitySignInResult.LinkRequired"/>).
/// The back-channel logout is not supported yet (cookies are not kept on the server): <c>/signout-backchannel</c> answers 501.
/// </summary>
public sealed class MatGameIdentityUsers(AppDbContext db, ILogger<MatGameIdentityUsers> log) : IMatIdentityUsers
{
    public async Task<MatIdentitySignInResult> SignInAsync(MatIdentityPerson person, HttpContext http, CancellationToken ct)
    {
        var (account, link) = await AccountLogic.IdentitySignInAsync(db, person.Subject, person.Name, person.PreferredUserName, person.Role, ct);
        if (link is { } linkId)
        {
            return MatIdentitySignInResult.LinkRequired(linkId.ToString(CultureInfo.InvariantCulture));
        }

        if (account is null)
        {
            return MatIdentitySignInResult.Refused("disabled");
        }

        await Identity.SignInAsync(http, account);
        log.LogInformation("Account {AccountId} signed in through MatIdentity", account.Id);
        return MatIdentitySignInResult.Success();
    }
}
