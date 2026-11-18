using System.Globalization;
using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using MatGame.Sdk.Web;
using MatPlatform;
using Microsoft.AspNetCore.Antiforgery;

namespace MatGame.Web;

/// <summary>
/// The host API (docs/PROTOCOL.md "Host API", <c>/api/matgame/...</c>): the session of the browser (cookie + antiforgery), the tokens of the apps, the sign-in of a
/// device in the browser, the account, the games. Errors: 400 <c>{ error, field }</c>, 401, 403, 410 (a device code that is gone), 426 (an app too old).
/// </summary>
public static class HostApi
{
    public sealed record LoginRequest(string? LoginName, string? Password, DeviceInfo? Device = null);

    public sealed record RegisterRequest(string? DisplayName, string? LoginName, string? Password, DeviceInfo? Device = null);

    public sealed record GuestRequest(string? Name, DeviceInfo? Device = null);

    public sealed record AccountRequest(string? DisplayName);

    public sealed record PasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record DeviceStartRequest(DeviceInfo? Device);

    public sealed record DevicePollRequest(string? DeviceCode);

    /// <summary>The platform's own "game" for the version check (the protocol only, no minimum).</summary>
    private static readonly GameInfo Platform = new("matgame", "MatGame", string.Empty, AppInfo.Version, MatGameProtocol.Version, null, Ui.Accent);

    public static void MapHostApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/matgame").ForGame(Platform).AddEndpointFilter(Errors);

        // ---- the browser (cookie) ----
        api.MapGet("/session", SessionAsync);
        api.MapPost("/session/login", async (LoginRequest body, HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            var account = await AccountLogic.SignInAsync(db, body.LoginName, body.Password)
                          ?? throw new BusinessException("Anmeldename oder Passwort stimmen nicht.", "LoginName");
            await LinkPendingIdentityAsync(http, db, account);
            await Identity.SignInAsync(http, account);
            return await SessionAsync(http, db, af);
        }).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/session/register", async (RegisterRequest body, HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            var account = await AccountLogic.RegisterAsync(db, body.DisplayName, body.LoginName, body.Password);
            await Identity.SignInAsync(http, account);
            return await SessionAsync(http, db, af);
        }).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/session/guest", async (GuestRequest body, HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            var account = await AccountLogic.CreateGuestAsync(db, body.Name);
            await Identity.SignInAsync(http, account);
            return await SessionAsync(http, db, af);
        }).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/session/logout", async (HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            await Identity.SignOutAsync(http);
            http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
            return await SessionAsync(http, db, af);
        });

        // ---- the account (cookie or token) ----
        api.MapPost("/account/upgrade", async (RegisterRequest body, HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            var account = await AccountLogic.UpgradeAsync(db, Me(http), body.DisplayName, body.LoginName, body.Password);
            if (!http.User.IsDevice())
            {
                await Identity.SignInAsync(http, account);
            }

            return await SessionAsync(http, db, af);
        });
        api.MapPut("/account", async (AccountRequest body, HttpContext http, AppDbContext db, IAntiforgery af) =>
        {
            var account = await AccountLogic.UpdateProfileAsync(db, Me(http), body.DisplayName);
            if (!http.User.IsDevice())
            {
                await Identity.SignInAsync(http, account);
            }

            return await SessionAsync(http, db, af);
        });
        api.MapPost("/account/password", async (PasswordRequest body, HttpContext http, AppDbContext db) =>
        {
            await AccountLogic.ChangePasswordAsync(db, Me(http), body.CurrentPassword, body.NewPassword);
            return Results.Ok(new { ok = true });
        });
        api.MapGet("/account/devices", async (HttpContext http, AppDbContext db) =>
            Results.Ok((await DeviceLogic.ListOwnAsync(db, Me(http))).Select(d => new { d.Id, d.Name, d.Platform, d.Game, d.CreateDate, d.LastUsedDate, current = CurrentDevice(http) == d.Id })));
        api.MapDelete("/account/devices/{id:long}", async (long id, HttpContext http, AppDbContext db) =>
        {
            await DeviceLogic.RemoveAsync(db, Me(http), id);
            return Results.Ok(new { ok = true });
        });

        // ---- the apps (tokens) ----
        api.MapPost("/token", async (LoginRequest body, AppDbContext db) =>
        {
            var account = await AccountLogic.SignInAsync(db, body.LoginName, body.Password)
                          ?? throw new BusinessException("Anmeldename oder Passwort stimmen nicht.", "LoginName");
            return await TokenAsync(db, account, body.Device);
        }).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/token/register", async (RegisterRequest body, AppDbContext db) =>
            await TokenAsync(db, await AccountLogic.RegisterAsync(db, body.DisplayName, body.LoginName, body.Password), body.Device)).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/token/guest", async (GuestRequest body, AppDbContext db) =>
            await TokenAsync(db, await AccountLogic.CreateGuestAsync(db, body.Name), body.Device)).RequireRateLimiting(RateLimits.SignIn);
        api.MapPost("/token/revoke", async (HttpContext http, AppDbContext db) =>
        {
            if (CurrentDevice(http) is { } device)
            {
                await DeviceLogic.RevokeTokenAsync(db, Me(http), device);
            }

            return Results.Ok(new { ok = true });
        });

        // ---- the sign-in of a device in the browser ----
        api.MapPost("/device/start", async (DeviceStartRequest body, HttpContext http, AppDbContext db) =>
        {
            var start = await DeviceLogic.StartLinkAsync(db, body.Device);
            var verify = $"{PublicBase(http)}/link?code={Uri.EscapeDataString(start.Code)}";
            return Results.Ok(new { code = start.Code, deviceCode = start.DeviceCode, verifyUrl = verify, interval = start.Interval, expiresIn = start.ExpiresIn });
        }).RequireRateLimiting(RateLimits.Device);
        api.MapPost("/device/poll", async (DevicePollRequest body, AppDbContext db) =>
        {
            var poll = await DeviceLogic.PollLinkAsync(db, body.DeviceCode);
            if (poll is null)
            {
                return Results.Json(new { error = "Die Anmeldung ist abgelaufen oder wurde abgelehnt." }, statusCode: StatusCodes.Status410Gone);
            }

            return poll.Pending
                ? Results.Json(new { pending = true }, statusCode: StatusCodes.Status202Accepted)
                : Results.Ok(new { token = poll.Token, account = AccountJson(poll.Account!) });
        }).RequireRateLimiting(RateLimits.Device);

        // ---- the games ----
        api.MapGet("/games", async (HttpContext http, AppDbContext db, GameHost host) =>
        {
            var states = await host.StatesAsync(http.RequestAborted);
            var stats = await host.StatsAsync(http.RequestServices, http.RequestAborted);
            var me = http.User.ToAccount();
            var profiles = me is null ? null : await host.ProfilesAsync(http.RequestServices, me.Id, http.RequestAborted);
            return Results.Ok(host.Games.Select(g => new
            {
                id = g.Id,
                name = g.Info.Name,
                tagline = g.Info.Tagline,
                path = g.Environment.BasePath,
                accent = g.Info.Accent,
                cover = GameHost.Asset(g, g.Info.Cover),
                icon = GameHost.Asset(g, g.Info.Icon),
                status = states[g.Id].IsEnabled ? "ok" : "off",
                stats = stats[g.Id],
                profile = profiles?.GetValueOrDefault(g.Id),
            }));
        });
    }

    /// <summary>The account as every answer shows it.</summary>
    public static object AccountJson(Account a) => new { a.Id, name = a.DisplayName, a.LoginName, a.IsAdmin, a.IsGuest };

    /// <summary>The address of this master as the caller sees it (behind a proxy: the forwarded host and scheme).</summary>
    public static string PublicBase(HttpContext http)
    {
        var settings = http.RequestServices.GetService<MatPlatformSettings>();
        if (!string.IsNullOrWhiteSpace(settings?.AppPublicUrl))
        {
            return settings.AppPublicUrl.TrimEnd('/');
        }

        return $"{http.Request.Scheme}://{http.Request.Host}";
    }

    private static Actor Me(HttpContext http)
    {
        var actor = Identity.ToActor(http.User);
        return actor.IsSignedIn ? actor : throw new AccessDeniedException("Bitte zuerst anmelden.");
    }

    private static long? CurrentDevice(HttpContext http) =>
        long.TryParse(http.User.FindFirst(GameAccounts.DeviceClaim)?.Value, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static async Task<IResult> SessionAsync(HttpContext http, AppDbContext db, IAntiforgery af)
    {
        var me = http.User.ToAccount();
        var account = me is null ? null : await AccountLogic.FindActiveAsync(db, me.Id);
        var settings = await SettingLogic.GetAsync(db);
        string? antiforgery = null;
        if (!http.User.IsDevice() && !Identity.HasToken(http.Request))
        {
            antiforgery = af.GetAndStoreTokens(http).RequestToken;
        }

        return Results.Ok(new
        {
            signedIn = account is not null,
            account = account is null ? null : AccountJson(account),
            canRegister = await AccountLogic.CanRegisterAsync(db),
            guests = settings.GuestsAllowed,
            firstRun = !await AccountLogic.HasAnyAsync(db),
            antiforgery,
            server = new { name = settings.Name, version = AppInfo.Version },
        });
    }

    private static async Task<IResult> TokenAsync(AppDbContext db, Account account, DeviceInfo? device)
    {
        var token = await DeviceLogic.CreateAsync(db, account, device);
        return Results.Ok(new { token, account = AccountJson(account) });
    }

    /// <summary>A person who came from MatIdentity with the name of an existing account proves it with the password: the account is linked now.</summary>
    private static async Task LinkPendingIdentityAsync(HttpContext http, AppDbContext db, Account account)
    {
        var settings = http.RequestServices.GetService<MatPlatformSettings>();
        if (settings?.IdentityEnabled != true)
        {
            return;
        }

        if (MatIdentityPendingLink.TakeFor(http, account.Id.ToString(CultureInfo.InvariantCulture)) is { } link)
        {
            await AccountLogic.LinkIdentityAsync(db, account.Id, link.Subject);
        }
    }

    /// <summary>A broken rule is a 400 with the message and the field (camelCase), a denied action a 401/403.</summary>
    private static async ValueTask<object?> Errors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (BusinessException e)
        {
            var field = string.IsNullOrEmpty(e.Field) ? null : char.ToLower(e.Field[0], CultureInfo.InvariantCulture) + e.Field[1..];
            return Results.BadRequest(new { error = e.Message, field });
        }
        catch (AccessDeniedException e)
        {
            var signedIn = context.HttpContext.User.Identity?.IsAuthenticated == true;
            return Results.Json(new { error = e.Message }, statusCode: signedIn ? 403 : 401);
        }
        catch (BadHttpRequestException)
        {
            return Results.BadRequest(new { error = "Die Anfrage ist unvollständig.", field = (string?)null });
        }
    }
}

/// <summary>The names of the rate limits.</summary>
public static class RateLimits
{
    public const string SignIn = "signin";
    public const string Device = "device";
    public const string Discovery = "discovery";
}
