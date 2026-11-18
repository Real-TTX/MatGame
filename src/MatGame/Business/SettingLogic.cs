using MatGame.Data;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Business;

/// <summary>The settings of the master (name, registration, guests, a new address).</summary>
public sealed record ServerSettings(string Name, bool RegistrationOpen, bool GuestsAllowed, string? MovedTo)
{
    public static readonly ServerSettings Default = new("MatGame", true, true, null);
}

public static class SettingLogic
{
    private const string NameKey = "ServerName";
    private const string RegistrationKey = "RegistrationOpen";
    private const string GuestsKey = "GuestsAllowed";
    private const string MovedKey = "MovedTo";

    /// <summary>Anyone may read them (the discovery and the portal show them).</summary>
    public static async Task<ServerSettings> GetAsync(AppDbContext db, CancellationToken ct = default)
    {
        var values = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var d = ServerSettings.Default;
        return new ServerSettings(
            values.TryGetValue(NameKey, out var name) && !string.IsNullOrWhiteSpace(name) ? name : d.Name,
            values.TryGetValue(RegistrationKey, out var reg) ? reg == "1" : d.RegistrationOpen,
            values.TryGetValue(GuestsKey, out var guests) ? guests == "1" : d.GuestsAllowed,
            values.TryGetValue(MovedKey, out var moved) && !string.IsNullOrWhiteSpace(moved) ? moved : null);
    }

    public static async Task SaveAsync(AppDbContext db, Actor actor, ServerSettings input, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 60)
        {
            throw new BusinessException("Der Name braucht 2 bis 60 Zeichen.", "Name");
        }

        var moved = string.IsNullOrWhiteSpace(input.MovedTo) ? null : input.MovedTo.Trim().TrimEnd('/');
        if (moved is not null && (!Uri.TryCreate(moved, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")))
        {
            throw new BusinessException("Eine vollständige Adresse mit https://.", "MovedTo");
        }

        db.ActingAs(actor.AuditId);
        await SetAsync(db, NameKey, name, ct);
        await SetAsync(db, RegistrationKey, input.RegistrationOpen ? "1" : "0", ct);
        await SetAsync(db, GuestsKey, input.GuestsAllowed ? "1" : "0", ct);
        await SetAsync(db, MovedKey, moved ?? string.Empty, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Changes single values (the platform API's configure): registration and guests.</summary>
    public static async Task SetFlagsAsync(AppDbContext db, Actor actor, bool? registrationOpen, bool? guestsAllowed, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        db.ActingAs(actor.AuditId);
        if (registrationOpen is { } r)
        {
            await SetAsync(db, RegistrationKey, r ? "1" : "0", ct);
        }

        if (guestsAllowed is { } g)
        {
            await SetAsync(db, GuestsKey, g ? "1" : "0", ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SetAsync(AppDbContext db, string key, string value, CancellationToken ct)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else if (setting.Value != value)
        {
            setting.Value = value;
        }
    }
}
