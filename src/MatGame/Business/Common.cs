using MatGame.Sdk;

namespace MatGame.Business;

/// <summary>Who acts: every method of the business layer takes one and checks the permission itself.</summary>
public sealed record Actor(long AccountId, string DisplayName, bool IsAdmin, bool IsGuest = false)
{
    /// <summary>The server itself (start-up, the platform API, clean-up): may do everything, saves without a user id.</summary>
    public static readonly Actor System = new(0, "System", true);

    /// <summary>Somebody who is not signed in (sign-in, registration, guests, devices).</summary>
    public static readonly Actor Anonymous = new(-1, "Gast", false);

    public bool IsSystem => AccountId == 0;

    public bool IsSignedIn => AccountId > 0;

    /// <summary>The id written to the audit columns (null for the system and anonymous callers).</summary>
    public long? AuditId => AccountId > 0 ? AccountId : null;

    public static Actor Of(GameAccount? account) =>
        account is null ? Anonymous : new Actor(account.Id, account.Name, account.IsAdmin, account.IsGuest);
}

/// <summary>The actor may not do what was asked.</summary>
public sealed class AccessDeniedException(string message = "Keine Berechtigung.") : Exception(message);

/// <summary>A rule of the business was broken; <see cref="Field"/> names the input it belongs to (null = the whole form).</summary>
public sealed class BusinessException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}

/// <summary>The permission checks of the business layer.</summary>
public static class Guard
{
    public static void Admin(Actor actor)
    {
        if (!actor.IsAdmin)
        {
            throw new AccessDeniedException("Nur für Administratoren.");
        }
    }

    public static void SignedIn(Actor actor)
    {
        if (!actor.IsSignedIn)
        {
            throw new AccessDeniedException("Bitte zuerst anmelden.");
        }
    }

    public static void AdminOrSelf(Actor actor, long accountId)
    {
        if (!actor.IsAdmin && actor.AccountId != accountId)
        {
            throw new AccessDeniedException();
        }
    }
}

/// <summary>Search, filters, sort and page of a list.</summary>
public sealed record ListRequest(string? Search, IReadOnlyDictionary<string, string?> Filters, string? Sort, bool Descending, int Page, int PageSize = 20)
{
    public string? Filter(string name) => Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>One page of a list: its rows, how many rows match the search and filters, and how many there are at all.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Matching, int Total);

internal static class QueryExtensions
{
    public static IQueryable<T> Page<T>(this IQueryable<T> query, ListRequest request)
        => query.Skip((Math.Max(1, request.Page) - 1) * request.PageSize).Take(request.PageSize);

    public static IQueryable<T> SortBy<T, TKey>(this IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, TKey>> key, bool descending)
        => descending ? Queryable.OrderByDescending(query, key) : Queryable.OrderBy(query, key);
}
