using System.Globalization;
using System.Security.Claims;

namespace MatGame.Sdk;

/// <summary>
/// The account of a request, as the host signed it in (cookie in the browser, bearer token of a device in the apps). Plugins never sign in
/// themselves: they read the account with <see cref="GameAccounts.ToAccount"/> and keep their own row per account (keyed by <see cref="Id"/>).
/// </summary>
/// <param name="Id">The account id of MatGame (bigint, never reused).</param>
/// <param name="Name">The display name.</param>
/// <param name="IsAdmin">An administrator of MatGame (every plugin's admin too).</param>
/// <param name="IsGuest">A guest account (no password, one device): no ranking.</param>
public sealed record GameAccount(long Id, string Name, bool IsAdmin, bool IsGuest);

/// <summary>The claims of the host's principal and the way from a principal to a <see cref="GameAccount"/>.</summary>
public static class GameAccounts
{
    /// <summary>The cookie scheme of the browser.</summary>
    public const string CookieScheme = "MatGame";

    /// <summary>The bearer scheme of the apps (device tokens).</summary>
    public const string TokenScheme = "MatGameToken";

    /// <summary>The policy that lets in a cookie or a token (the host's fallback policy).</summary>
    public const string AnyScheme = "MatGame.Any";

    /// <summary>The role of administrators.</summary>
    public const string AdminRole = "Admin";

    /// <summary>The policy of <c>/Admin</c> and of the plugins' admin pages.</summary>
    public const string AdminPolicy = "Admin";

    public const string GuestClaim = "matgame:guest";

    public const string DeviceClaim = "matgame:device";

    public static ClaimsPrincipal ToPrincipal(GameAccount account, string scheme, long? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, account.Name),
        };
        if (account.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        if (account.IsGuest)
        {
            claims.Add(new Claim(GuestClaim, "1"));
        }

        if (deviceId is not null)
        {
            claims.Add(new Claim(DeviceClaim, deviceId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    /// <summary>The account of the principal, or null when nobody is signed in.</summary>
    public static GameAccount? ToAccount(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || !long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new GameAccount(id, user.FindFirstValue(ClaimTypes.Name) ?? "?", user.IsInRole(AdminRole), user.HasClaim(c => c.Type == GuestClaim));
    }

    /// <summary>True when the request came with a device token (an app), false for the browser's cookie.</summary>
    public static bool IsDevice(this ClaimsPrincipal user) => user.HasClaim(c => c.Type == DeviceClaim);
}

/// <summary>
/// What a plugin may ask the host about accounts (a service of the host: inject <see cref="IGameAccounts"/>).
/// </summary>
public interface IGameAccounts
{
    /// <summary>The account, or null when it does not exist (any more).</summary>
    Task<GameAccount?> FindAsync(long id, CancellationToken ct = default);

    /// <summary>Several accounts at once (names in lists).</summary>
    Task<IReadOnlyDictionary<long, GameAccount>> FindManyAsync(IEnumerable<long> ids, CancellationToken ct = default);

    /// <summary>
    /// Takes over an account of an old stand-alone game server (the import of a plugin): returns the id of the existing account with this login name
    /// (case-insensitive) or of a new one. The password hash is the format of ASP.NET Core's <c>PasswordHasher</c> (v3); it is kept, so the old password works.
    /// </summary>
    Task<long> ImportAsync(ImportedAccount account, CancellationToken ct = default);
}

/// <summary>An account of an old stand-alone server (see <see cref="IGameAccounts.ImportAsync"/>).</summary>
public sealed record ImportedAccount(string LoginName, string DisplayName, string PasswordHash, bool IsAdmin, bool IsActive, DateTime CreateDate, DateTime? LastLoginDate);
