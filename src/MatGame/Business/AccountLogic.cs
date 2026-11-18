using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MatGame.Data;
using MatGame.Sdk;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Business;

/// <summary>A row of the account list of the administration.</summary>
public sealed record AccountRow(long Id, string LoginName, string DisplayName, bool IsAdmin, bool IsActive, bool IsGuest, bool Linked, int Devices, DateTime? LastLoginDate, DateTime CreateDate);

/// <summary>What the administration may change of an account.</summary>
public sealed record AccountInput(long? Id, string? DisplayName, string? LoginName, string? Password, bool IsAdmin, bool IsActive);

/// <summary>
/// Accounts: registration, sign-in, guests and their upgrade, the profile, and the administration of accounts. The first account of a master becomes its
/// administrator. Names are checked here (German messages at the field).
/// </summary>
public static partial class AccountLogic
{
    public const int PasswordMinLength = 8;

    private static readonly PasswordHasher<Account> Hasher = new();

    [GeneratedRegex("^[a-zA-Z0-9._-]{3,40}$")]
    private static partial Regex LoginRegex();

    private static IQueryable<Account> Alive(AppDbContext db) => db.Accounts.Where(a => a.UpdateState != UpdateState.Deleted);

    public static GameAccount ToGameAccount(Account a) => new(a.Id, a.DisplayName, a.IsAdmin, a.IsGuest);

    public static async Task<bool> HasAnyAsync(AppDbContext db, CancellationToken ct = default) => await Alive(db).AnyAsync(a => !a.IsGuest, ct);

    /// <summary>Registration is open when the setting says so, and always for the first account (it becomes the administrator).</summary>
    public static async Task<bool> CanRegisterAsync(AppDbContext db, CancellationToken ct = default)
        => !await HasAnyAsync(db, ct) || (await SettingLogic.GetAsync(db, ct)).RegistrationOpen;

    public static async Task<Account?> FindActiveAsync(AppDbContext db, long id, CancellationToken ct = default)
        => await Alive(db).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.IsActive, ct);

    public static async Task<Account> RegisterAsync(AppDbContext db, string? displayName, string? loginName, string? password, CancellationToken ct = default)
    {
        if (!await CanRegisterAsync(db, ct))
        {
            throw new BusinessException("Neue Konten legt hier nur die Administration an.");
        }

        var first = !await HasAnyAsync(db, ct);
        var account = new Account
        {
            DisplayName = CheckDisplayName(displayName),
            LoginName = await CheckLoginNameAsync(db, loginName, null, ct),
            IsAdmin = first,
            LastLoginDate = DateTime.UtcNow,
        };
        account.PasswordHash = Hasher.HashPassword(account, CheckPassword(password, "Password"));
        db.ActingAs(null);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public static async Task<Account?> SignInAsync(AppDbContext db, string? loginName, string? password, CancellationToken ct = default)
    {
        var login = loginName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (login.Length == 0 || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var account = await Alive(db).FirstOrDefaultAsync(a => a.LoginName == login && !a.IsGuest, ct);
        if (account is null || !account.IsActive || account.PasswordHash.Length == 0)
        {
            return null;
        }

        var check = Hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (check == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = Hasher.HashPassword(account, password);
        }

        account.LastLoginDate = DateTime.UtcNow;
        db.ActingAs(account.Id);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>A guest account (when the master allows guests): the chosen name, a generated login name, no password.</summary>
    public static async Task<Account> CreateGuestAsync(AppDbContext db, string? name, CancellationToken ct = default)
    {
        if (!(await SettingLogic.GetAsync(db, ct)).GuestsAllowed)
        {
            throw new BusinessException("Auf diesem Server kann man nicht als Gast spielen. Bitte anmelden.");
        }

        var display = name?.Trim() ?? string.Empty;
        if (display.Length is < 2 or > 24)
        {
            throw new BusinessException("Der Name braucht 2 bis 24 Zeichen.", "Name");
        }

        var account = new Account
        {
            DisplayName = display,
            LoginName = "gast-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            IsGuest = true,
            LastLoginDate = DateTime.UtcNow,
        };
        db.ActingAs(null);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>The guest becomes a full account (same id: everything the games keep stays with it).</summary>
    public static async Task<Account> UpgradeAsync(AppDbContext db, Actor actor, string? displayName, string? loginName, string? password, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        var account = await Alive(db).FirstOrDefaultAsync(a => a.Id == actor.AccountId, ct) ?? throw new AccessDeniedException("Bitte zuerst anmelden.");
        if (!account.IsGuest)
        {
            throw new BusinessException("Das ist schon ein richtiges Konto.");
        }

        account.DisplayName = CheckDisplayName(displayName);
        account.LoginName = await CheckLoginNameAsync(db, loginName, account.Id, ct);
        account.PasswordHash = Hasher.HashPassword(account, CheckPassword(password, "Password"));
        account.IsGuest = false;
        db.ActingAs(account.Id);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public static async Task<Account> UpdateProfileAsync(AppDbContext db, Actor actor, string? displayName, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        var account = await Alive(db).FirstOrDefaultAsync(a => a.Id == actor.AccountId, ct) ?? throw new AccessDeniedException("Bitte zuerst anmelden.");
        var max = account.IsGuest ? 24 : 60;
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length < 2 || name.Length > max)
        {
            throw new BusinessException($"Der Name braucht 2 bis {max} Zeichen.", "DisplayName");
        }

        account.DisplayName = name;
        db.ActingAs(account.Id);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public static async Task ChangePasswordAsync(AppDbContext db, Actor actor, string? currentPassword, string? newPassword, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        var account = await Alive(db).FirstOrDefaultAsync(a => a.Id == actor.AccountId, ct) ?? throw new AccessDeniedException("Bitte zuerst anmelden.");
        if (account.IsGuest)
        {
            throw new BusinessException("Ein Gast hat kein Passwort. Mach zuerst ein Konto daraus.");
        }

        if (account.PasswordHash.Length > 0 && Hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            throw new BusinessException("Das bisherige Passwort stimmt nicht.", "CurrentPassword");
        }

        account.PasswordHash = Hasher.HashPassword(account, CheckPassword(newPassword, "NewPassword"));
        db.ActingAs(account.Id);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- administration

    public static async Task<PagedResult<AccountRow>> ListAsync(AppDbContext db, Actor actor, ListRequest request, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        var all = Alive(db);
        var query = all;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(a => a.DisplayName.ToLower().Contains(s) || a.LoginName.Contains(s));
        }

        query = request.Filter("kind") switch
        {
            "admin" => query.Where(a => a.IsAdmin),
            "player" => query.Where(a => !a.IsAdmin && !a.IsGuest),
            "guest" => query.Where(a => a.IsGuest),
            _ => query,
        };
        query = request.Filter("status") switch
        {
            "active" => query.Where(a => a.IsActive),
            "disabled" => query.Where(a => !a.IsActive),
            _ => query,
        };

        var matching = await query.CountAsync(ct);
        var total = await all.CountAsync(ct);
        query = request.Sort switch
        {
            "login" => query.SortBy(a => a.LastLoginDate, !request.Descending),
            "created" => query.SortBy(a => a.CreateDate, !request.Descending),
            _ => query.SortBy(a => a.DisplayName, request.Descending),
        };

        var items = await query.Page(request)
            .Select(a => new AccountRow(a.Id, a.LoginName, a.DisplayName, a.IsAdmin, a.IsActive, a.IsGuest, a.IdentitySubject != null,
                db.Devices.Count(d => d.AccountId == a.Id), a.LastLoginDate, a.CreateDate))
            .ToListAsync(ct);
        return new PagedResult<AccountRow>(items, matching, total);
    }

    public static async Task<Account?> GetAsync(AppDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        Guard.AdminOrSelf(actor, id);
        return await Alive(db).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public static async Task<long> SaveAsync(AppDbContext db, Actor actor, AccountInput input, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        Account account;
        if (input.Id is { } id)
        {
            account = await Alive(db).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new BusinessException("Dieses Konto gibt es nicht mehr.");
        }
        else
        {
            account = new Account();
            db.Accounts.Add(account);
        }

        account.DisplayName = CheckDisplayName(input.DisplayName);
        if (!account.IsGuest || input.Id is null)
        {
            account.LoginName = await CheckLoginNameAsync(db, input.LoginName, input.Id, ct);
        }

        if (input.Id is null || !string.IsNullOrEmpty(input.Password))
        {
            account.PasswordHash = Hasher.HashPassword(account, CheckPassword(input.Password, "Password"));
        }

        if (input.Id == actor.AccountId && (!input.IsAdmin || !input.IsActive))
        {
            throw new BusinessException("Das eigene Konto kann sich nicht selbst die Administration nehmen oder sperren.", input.IsAdmin ? "IsActive" : "IsAdmin");
        }

        account.IsAdmin = input.IsAdmin && !account.IsGuest;
        account.IsActive = input.IsActive;
        db.ActingAs(actor.AuditId);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    /// <summary>Marks the account deleted, signs out its devices; the games are told by the caller (<see cref="Web.GameHost"/>).</summary>
    public static async Task DeleteAsync(AppDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        if (id == actor.AccountId)
        {
            throw new BusinessException("Das eigene Konto lässt sich hier nicht löschen.");
        }

        var account = await Alive(db).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new BusinessException("Dieses Konto gibt es nicht mehr.");
        account.UpdateState = UpdateState.Deleted;
        account.IsActive = false;
        account.LoginName = $"deleted-{account.Id}-{account.LoginName}";
        if (account.LoginName.Length > 60)
        {
            account.LoginName = account.LoginName[..60];
        }

        account.IdentitySubject = null;
        db.Devices.RemoveRange(db.Devices.Where(d => d.AccountId == id));
        db.ActingAs(actor.AuditId);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- for the plugins and MatIdentity

    public static async Task<IReadOnlyDictionary<long, GameAccount>> FindManyAsync(AppDbContext db, IEnumerable<long> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return await Alive(db).AsNoTracking().Where(a => list.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ToGameAccount, ct);
    }

    /// <summary>An account of an old stand-alone server: the existing one with the login name, or a new one with the old hash.</summary>
    public static async Task<long> ImportAsync(AppDbContext db, ImportedAccount input, CancellationToken ct = default)
    {
        var login = input.LoginName.Trim().ToLowerInvariant();
        var existing = await Alive(db).FirstOrDefaultAsync(a => a.LoginName == login, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var first = !await HasAnyAsync(db, ct);
        var account = new Account
        {
            LoginName = login.Length > 60 ? login[..60] : login,
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? login : input.DisplayName.Trim(),
            PasswordHash = input.PasswordHash,
            IsAdmin = input.IsAdmin || first,
            IsActive = input.IsActive,
            LastLoginDate = input.LastLoginDate,
        };
        db.ActingAs(null);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    /// <summary>
    /// The sign-in through MatIdentity: the account linked to <paramref name="subject"/>, else a new one. An unlinked local account with the same login name is
    /// never linked by the name alone: the answer is (null, its id) and the caller asks for its password first (MatIdentityPendingLink).
    /// </summary>
    public static async Task<(Account? Account, long? LinkAccountId)> IdentitySignInAsync(AppDbContext db, string subject, string? name, string? preferredUserName, string? role,
        CancellationToken ct = default)
    {
        var account = await Alive(db).FirstOrDefaultAsync(a => a.IdentitySubject == subject, ct);
        if (account is null)
        {
            var login = Clean(preferredUserName) ?? Clean(name) ?? ("id-" + subject);
            var match = await Alive(db).FirstOrDefaultAsync(a => a.LoginName == login && !a.IsGuest, ct);
            if (match is not null)
            {
                return (null, match.Id);
            }

            account = new Account { LoginName = login, DisplayName = name?.Trim() is { Length: >= 2 } n ? (n.Length > 60 ? n[..60] : n) : login, IdentitySubject = subject };
            account.IsAdmin = !await HasAnyAsync(db, ct);
            db.Accounts.Add(account);
        }

        if (!account.IsActive)
        {
            return (null, null);
        }

        if (role is not null)
        {
            account.IsAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
        }

        account.LastLoginDate = DateTime.UtcNow;
        db.ActingAs(null);
        await db.SaveChangesAsync(ct);
        return (account, null);

        static string? Clean(string? value)
        {
            var text = value?.Trim().ToLowerInvariant().Replace(' ', '.');
            return text is not null && LoginRegex().IsMatch(text) ? text : null;
        }
    }

    /// <summary>Links the local account (whose password was just checked) to the MatIdentity person.</summary>
    public static async Task LinkIdentityAsync(AppDbContext db, long accountId, string subject, CancellationToken ct = default)
    {
        var account = await Alive(db).FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null || await Alive(db).AnyAsync(a => a.IdentitySubject == subject && a.Id != accountId, ct))
        {
            return;
        }

        account.IdentitySubject = subject;
        db.ActingAs(accountId);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- rules

    private static string CheckDisplayName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 60)
        {
            throw new BusinessException("Der Name braucht 2 bis 60 Zeichen.", "DisplayName");
        }

        return name;
    }

    private static async Task<string> CheckLoginNameAsync(AppDbContext db, string? value, long? id, CancellationToken ct)
    {
        var login = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!LoginRegex().IsMatch(login))
        {
            throw new BusinessException("Der Anmeldename braucht 3 bis 40 Zeichen: Buchstaben, Ziffern, Punkt, Strich.", "LoginName");
        }

        if (login.StartsWith("gast-", StringComparison.Ordinal) || login.StartsWith("deleted-", StringComparison.Ordinal))
        {
            throw new BusinessException("Dieser Anmeldename ist reserviert.", "LoginName");
        }

        if (await db.Accounts.AnyAsync(a => a.LoginName == login && a.Id != id, ct))
        {
            throw new BusinessException("Diesen Anmeldenamen gibt es schon.", "LoginName");
        }

        return login;
    }

    private static string CheckPassword(string? value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length < PasswordMinLength)
        {
            throw new BusinessException($"Das Passwort braucht mindestens {PasswordMinLength} Zeichen.", field);
        }

        return value;
    }
}
