using MatGame.Data;
using MatGame.Sdk;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Business;

/// <summary>A game as the master offers it: what the plugin says plus what the administration decided.</summary>
public sealed record GameState(GameInfo Info, bool IsEnabled, string? MinClientOverride)
{
    /// <summary>The oldest app version that may play online.</summary>
    public string? MinClient => MinClientOverride ?? Info.MinClient;

    /// <summary>The info the plugins' version check uses (with the administration's minimum).</summary>
    public GameInfo Effective => Info with { MinClient = MinClient };
}

/// <summary>The administration's decisions about the games (on/off, minimum app version).</summary>
public static class GameLogic
{
    public static async Task<IReadOnlyList<GameState>> StatesAsync(AppDbContext db, IEnumerable<GameInfo> games, CancellationToken ct = default)
    {
        var settings = await db.GameSettings.AsNoTracking().ToDictionaryAsync(g => g.GameId, ct);
        return games.Select(g => settings.TryGetValue(g.Id, out var s) ? new GameState(g, s.IsEnabled, s.MinClient) : new GameState(g, true, null)).ToList();
    }

    public static async Task SaveAsync(AppDbContext db, Actor actor, string gameId, bool isEnabled, string? minClient, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        var min = string.IsNullOrWhiteSpace(minClient) ? null : minClient.Trim();
        if (min is not null && !System.Text.RegularExpressions.Regex.IsMatch(min, @"^\d+(\.\d+){0,2}$"))
        {
            throw new BusinessException("Eine Version wie 1.4 oder 1.4.12 (leer = die des Spiels).", "MinClient");
        }

        var setting = await db.GameSettings.FirstOrDefaultAsync(g => g.GameId == gameId, ct);
        if (setting is null)
        {
            setting = new GameSetting { GameId = gameId };
            db.GameSettings.Add(setting);
        }

        setting.IsEnabled = isEnabled;
        setting.MinClient = min;
        db.ActingAs(actor.AuditId);
        await db.SaveChangesAsync(ct);
    }
}
