using System.Security.Cryptography;
using System.Text;
using MatGame.Data;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Business;

/// <summary>What an app says about itself when it signs in.</summary>
public sealed record DeviceInfo(string? Name, string? Platform, string? Game);

/// <summary>A device in the profile and the administration.</summary>
public sealed record DeviceRow(long Id, long AccountId, string AccountName, string Name, string Platform, string Game, DateTime CreateDate, DateTime LastUsedDate);

/// <summary>A started sign-in in the browser: the code for the player, the secret the app polls with.</summary>
public sealed record DeviceLinkStart(string Code, string DeviceCode, int Interval, int ExpiresIn);

/// <summary>The state of a sign-in in the browser for the app: pending, a token, or gone (expired, denied, used).</summary>
public sealed record DeviceLinkPoll(bool Pending, string? Token, Account? Account);

/// <summary>A sign-in in the browser as the /link page shows it.</summary>
public sealed record DeviceLinkView(string Code, string DeviceName, string Platform, string Game, DateTime ExpiresAt, bool Done);

/// <summary>
/// The devices (apps) of the accounts: a device signs in with a random token (32 bytes, base64url); only its SHA-256 is stored. A token works until the device
/// is removed, or until it was not used for <see cref="UnusedDays"/> days. The sign-in in the browser (<see cref="StartLinkAsync"/>) gives an app a token without
/// typing a password into it.
/// </summary>
public static class DeviceLogic
{
    public const int UnusedDays = 180;

    public const int LinkMinutes = 10;

    public const int PollSeconds = 3;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>A token is expired when it was not used for <see cref="UnusedDays"/> days.</summary>
    public static bool IsExpired(DateTime lastUsedUtc, DateTime nowUtc) => nowUtc - lastUsedUtc > TimeSpan.FromDays(UnusedDays);

    /// <summary>"ABCD-EFGH": 8 letters and digits without the ones that look alike.</summary>
    public static string NewCode()
    {
        Span<char> chars = stackalloc char[9];
        for (var i = 0; i < 9; i++)
        {
            chars[i] = i == 4 ? '-' : CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>"abcd efgh" → "ABCD-EFGH" (null when it is no code).</summary>
    public static string? NormalizeCode(string? input)
    {
        var letters = new string((input ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return letters.Length == 8 ? $"{letters[..4]}-{letters[4..]}" : null;
    }

    /// <summary>Signs a device in for the account and returns its token (shown once).</summary>
    public static async Task<string> CreateAsync(AppDbContext db, Account account, DeviceInfo? info, CancellationToken ct = default)
    {
        var token = NewToken();
        db.Devices.Add(new Device
        {
            AccountId = account.Id,
            Name = Cut(info?.Name, 80) ?? "Gerät",
            Platform = Cut(info?.Platform, 20) ?? string.Empty,
            Game = Cut(info?.Game, 40) ?? string.Empty,
            TokenHash = Hash(token),
            LastUsedDate = DateTime.UtcNow,
        });
        db.ActingAs(account.Id);
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>The device and its active account of a token, or null (unknown, expired, account gone). Updates the last use at most every few minutes.</summary>
    public static async Task<(Device Device, Account Account)?> ValidateAsync(AppDbContext db, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200)
        {
            return null;
        }

        var hash = Hash(token);
        var device = await db.Devices.Include(d => d.Account).FirstOrDefaultAsync(d => d.TokenHash == hash, ct);
        var now = DateTime.UtcNow;
        if (device?.Account is not { } account || account.UpdateState == UpdateState.Deleted || !account.IsActive)
        {
            return null;
        }

        if (IsExpired(device.LastUsedDate, now))
        {
            db.Devices.Remove(device);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (now - device.LastUsedDate > TimeSpan.FromMinutes(5))
        {
            device.LastUsedDate = now;
            db.ActingAs(account.Id);
            await db.SaveChangesAsync(ct);
        }

        return (device, account);
    }

    /// <summary>Removes the device of a token (the app signs out).</summary>
    public static async Task RevokeTokenAsync(AppDbContext db, Actor actor, long deviceId, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.AccountId == actor.AccountId, ct);
        if (device is not null)
        {
            db.Devices.Remove(device);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Removes a device (its owner in the profile, or an administrator).</summary>
    public static async Task RemoveAsync(AppDbContext db, Actor actor, long deviceId, CancellationToken ct = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct) ?? throw new BusinessException("Dieses Gerät gibt es nicht mehr.");
        Guard.AdminOrSelf(actor, device.AccountId);
        db.Devices.Remove(device);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<IReadOnlyList<DeviceRow>> ListOwnAsync(AppDbContext db, Actor actor, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        return await Rows(db.Devices.Where(d => d.AccountId == actor.AccountId).OrderByDescending(d => d.LastUsedDate)).ToListAsync(ct);
    }

    public static async Task<PagedResult<DeviceRow>> ListAsync(AppDbContext db, Actor actor, ListRequest request, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        var all = db.Devices.AsQueryable();
        var query = all;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(d => d.Name.ToLower().Contains(s) || d.Account!.DisplayName.ToLower().Contains(s) || d.Account!.LoginName.Contains(s));
        }

        if (request.Filter("platform") is { } platform)
        {
            query = query.Where(d => d.Platform == platform);
        }

        var matching = await query.CountAsync(ct);
        var total = await all.CountAsync(ct);
        var sorted = request.Sort switch
        {
            "name" => query.SortBy(d => d.Name, request.Descending),
            "account" => query.SortBy(d => d.Account!.DisplayName, request.Descending),
            _ => query.SortBy(d => d.LastUsedDate, !request.Descending),
        };
        var items = await Rows(sorted.Page(request)).ToListAsync(ct);
        return new PagedResult<DeviceRow>(items, matching, total);
    }

    private static IQueryable<DeviceRow> Rows(IQueryable<Device> query) =>
        query.Select(d => new DeviceRow(d.Id, d.AccountId, d.Account!.DisplayName, d.Name, d.Platform, d.Game, d.CreateDate, d.LastUsedDate));

    // ---------------------------------------------------------------- sign-in in the browser

    public static async Task<DeviceLinkStart> StartLinkAsync(AppDbContext db, DeviceInfo? info, CancellationToken ct = default)
    {
        var secret = NewToken();
        string code;
        do
        {
            code = NewCode();
        }
        while (await db.DeviceLinks.AnyAsync(l => l.Code == code && l.ExpiresAt > DateTime.UtcNow, ct));

        db.DeviceLinks.Add(new DeviceLink
        {
            Code = code,
            DeviceCodeHash = Hash(secret),
            DeviceName = Cut(info?.Name, 80) ?? "Gerät",
            Platform = Cut(info?.Platform, 20) ?? string.Empty,
            Game = Cut(info?.Game, 40) ?? string.Empty,
            ExpiresAt = DateTime.UtcNow.AddMinutes(LinkMinutes),
        });
        db.ActingAs(null);
        await db.SaveChangesAsync(ct);
        return new DeviceLinkStart(code, secret, PollSeconds, LinkMinutes * 60);
    }

    /// <summary>The open sign-in of a code (for the /link page), or null when it is unknown or expired.</summary>
    public static async Task<DeviceLinkView?> FindLinkAsync(AppDbContext db, string? code, CancellationToken ct = default)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
        {
            return null;
        }

        var link = await db.DeviceLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Code == normalized && l.ExpiresAt > DateTime.UtcNow && !l.Denied && !l.Collected, ct);
        return link is null ? null : new DeviceLinkView(link.Code, link.DeviceName, link.Platform, link.Game, link.ExpiresAt, link.ApprovedAccountId is not null);
    }

    /// <summary>The signed-in player confirms (or denies) the code shown by the app.</summary>
    public static async Task DecideLinkAsync(AppDbContext db, Actor actor, string? code, bool approve, CancellationToken ct = default)
    {
        Guard.SignedIn(actor);
        var normalized = NormalizeCode(code) ?? throw new BusinessException("Bitte den Code aus der App eingeben (8 Zeichen).", "Code");
        var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.Code == normalized && l.ExpiresAt > DateTime.UtcNow && !l.Denied && !l.Collected, ct)
                   ?? throw new BusinessException("Diesen Code gibt es nicht (mehr). Bitte in der App neu starten.", "Code");
        if (approve)
        {
            link.ApprovedAccountId = actor.AccountId;
        }
        else
        {
            link.Denied = true;
        }

        db.ActingAs(actor.AuditId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The app asks whether the player confirmed: then it gets its token (once).</summary>
    public static async Task<DeviceLinkPoll?> PollLinkAsync(AppDbContext db, string? deviceCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(deviceCode))
        {
            return null;
        }

        var hash = Hash(deviceCode);
        var link = await db.DeviceLinks.FirstOrDefaultAsync(l => l.DeviceCodeHash == hash, ct);
        if (link is null || link.Denied || link.Collected || link.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        if (link.ApprovedAccountId is not { } accountId)
        {
            return new DeviceLinkPoll(true, null, null);
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.IsActive && a.UpdateState != UpdateState.Deleted, ct);
        link.Collected = true;
        await db.SaveChangesAsync(ct);
        if (account is null)
        {
            return null;
        }

        var token = await CreateAsync(db, account, new DeviceInfo(link.DeviceName, link.Platform, link.Game), ct);
        return new DeviceLinkPoll(false, token, account);
    }

    /// <summary>Removes old sign-ins in the browser and devices unused for too long (the hourly housekeeping): how many.</summary>
    public static async Task<int> CleanUpAsync(AppDbContext db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var links = await db.DeviceLinks.Where(l => l.ExpiresAt < now.AddDays(-1)).ExecuteDeleteAsync(ct);
        var limit = now.AddDays(-UnusedDays);
        var devices = await db.Devices.Where(d => d.LastUsedDate < limit).ExecuteDeleteAsync(ct);
        return links + devices;
    }

    private static string? Cut(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > max ? text[..max] : text;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
