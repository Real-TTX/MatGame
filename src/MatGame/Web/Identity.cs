using System.Security.Claims;
using System.Text.Encodings.Web;
using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace MatGame.Web;

/// <summary>
/// The sign-in of MatGame: the browser's cookie (<see cref="GameAccounts.CookieScheme"/>), the apps' device tokens (<see cref="GameAccounts.TokenScheme"/>), and the
/// policy scheme <see cref="GameAccounts.AnyScheme"/> that picks one per request (a bearer token or a hub's <c>access_token</c> = token, else the cookie).
/// </summary>
public static class Identity
{
    public const string CookieName = "matgame.auth";

    public static void AddMatGameAuthentication(this IServiceCollection services, Action<AuthenticationBuilder>? more = null)
    {
        var builder = services.AddAuthentication(o =>
            {
                o.DefaultScheme = GameAccounts.AnyScheme;
                o.DefaultChallengeScheme = GameAccounts.AnyScheme;
            })
            .AddPolicyScheme(GameAccounts.AnyScheme, "Cookie or token", o => o.ForwardDefaultSelector = context =>
                HasToken(context.Request) ? GameAccounts.TokenScheme : GameAccounts.CookieScheme)
            .AddCookie(GameAccounts.CookieScheme, o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.IsEssential = true;
                o.LoginPath = "/Account/Login";
                o.ReturnUrlParameter = "returnUrl";
                o.AccessDeniedPath = "/Account/Denied";
                o.ExpireTimeSpan = TimeSpan.FromDays(60);
                o.SlidingExpiration = true;
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context => Redirect(context, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => Redirect(context, StatusCodes.Status403Forbidden),
                    OnValidatePrincipal = ValidateCookieAsync,
                };
            })
            .AddScheme<AuthenticationSchemeOptions, DeviceTokenHandler>(GameAccounts.TokenScheme, null);
        more?.Invoke(builder);
    }

    /// <summary>A request with a device token: <c>Authorization: Bearer</c>, or <c>access_token</c> on a hub address (WebSockets cannot send headers).</summary>
    public static bool HasToken(HttpRequest request)
    {
        if (request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsHub(request.Path) && !string.IsNullOrEmpty(request.Query["access_token"]);
    }

    /// <summary>"/bunnywarfare/hub", "/bunnywarfare/hub/negotiate".</summary>
    public static bool IsHub(PathString path)
    {
        var value = path.Value ?? string.Empty;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments[1].Equals("hub", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A request of a script (APIs, hubs) gets a status code instead of the sign-in page.</summary>
    public static bool IsMachineRequest(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/api") || IsHub(path))
        {
            return true;
        }

        var segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase)
               || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task SignInAsync(HttpContext http, Account account)
    {
        var principal = GameAccounts.ToPrincipal(AccountLogic.ToGameAccount(account), GameAccounts.CookieScheme);
        await http.SignInAsync(GameAccounts.CookieScheme, principal, new AuthenticationProperties { IsPersistent = true });
        http.User = principal;
    }

    public static Task SignOutAsync(HttpContext http) => http.SignOutAsync(GameAccounts.CookieScheme);

    public static Actor ToActor(ClaimsPrincipal user) => Actor.Of(user.ToAccount());

    private static Task Redirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (IsMachineRequest(context.Request))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    // a disabled or deleted account is signed out at once, a changed name, role or guest state takes effect
    private static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var account = context.Principal?.ToAccount();
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var current = account is null ? null : await AccountLogic.FindActiveAsync(db, account.Id);
        if (current is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(GameAccounts.CookieScheme);
            return;
        }

        var fresh = AccountLogic.ToGameAccount(current);
        if (fresh != account)
        {
            context.ReplacePrincipal(GameAccounts.ToPrincipal(fresh, GameAccounts.CookieScheme));
            context.ShouldRenew = true;
        }
    }
}

/// <summary>Signs an app in with its device token (only its SHA-256 is stored, see <see cref="DeviceLogic"/>).</summary>
public sealed class DeviceTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim()
            : Identity.IsHub(Request.Path) ? Request.Query["access_token"].ToString() : string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var db = Context.RequestServices.GetRequiredService<AppDbContext>();
        var found = await DeviceLogic.ValidateAsync(db, token);
        if (found is not { } valid)
        {
            return AuthenticateResult.Fail("Unknown or expired device token.");
        }

        var principal = GameAccounts.ToPrincipal(AccountLogic.ToGameAccount(valid.Account), GameAccounts.TokenScheme, valid.Device.Id);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, GameAccounts.TokenScheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
