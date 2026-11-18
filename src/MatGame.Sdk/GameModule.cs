using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MatGame.Sdk;

/// <summary>
/// Who a game is, for the portal, the discovery document and the version check of the apps.
/// </summary>
/// <param name="Id">The key of the game: path segment (<c>/bunnywarfare/</c>), database schema, id in the apps (lower case letters and digits only).</param>
/// <param name="Name">The name shown to people ("BunnyWarfare").</param>
/// <param name="Tagline">One German sentence for the card in the portal.</param>
/// <param name="Version">The version of the plugin (its informational version).</param>
/// <param name="Protocol">The protocol number of the game: rises only with a change that old apps cannot follow.</param>
/// <param name="MinClient">The oldest app version that may play online (compared by <see cref="GameVersion"/>); null = every app of the protocol.</param>
/// <param name="Accent">The colour of the card (#rrggbb).</param>
/// <param name="Cover">The picture of the card, relative to the game's address (<c>img/cover.jpg</c>), or null.</param>
/// <param name="Icon">The square icon, relative to the game's address, or null.</param>
public sealed record GameInfo(
    string Id,
    string Name,
    string Tagline,
    string Version,
    int Protocol,
    string? MinClient,
    string Accent,
    string? Cover = null,
    string? Icon = null);

/// <summary>What the host tells a plugin about where it runs.</summary>
/// <param name="ConnectionString">The PostgreSQL database of MatGame: the plugin keeps its tables in its own schema (<see cref="GameInfo.Id"/>).</param>
/// <param name="DataDirectory">A folder of the plugin's own in the data volume (created by the host).</param>
/// <param name="BasePath">The address of the game below the host: <c>/bunnywarfare</c> (no slash at the end).</param>
/// <param name="IsDevelopment">The host runs in Development.</param>
public sealed record GameEnvironment(string ConnectionString, string DataDirectory, string BasePath, bool IsDevelopment);

/// <summary>An entry of the game in the menu of <c>/Admin</c>. The pages live in the plugin (Razor pages under <c>Pages/Admin/{Game}/</c>).</summary>
/// <param name="Title">The text of the entry ("Welten").</param>
/// <param name="Path">The address of the page (<c>/Admin/BunnyWarfare/Worlds</c>).</param>
/// <param name="Icon">An icon of the MatUi sprite.</param>
public sealed record AdminLink(string Title, string Path, string Icon);

/// <summary>Numbers of a game for the portal and the widgets.</summary>
public sealed record GameStats(int PlayersOnline, int RunningMatches);

/// <summary>What an account has reached in a game, for the profile in the portal ("Level 9").</summary>
public sealed record GameProfile(int Level, string? Detail);

/// <summary>
/// A game in MatGame. The host lists its modules in <c>Games.cs</c> (compiled in: one image with every game). At start it gives each its services,
/// maps its routes under <see cref="GameEnvironment.BasePath"/>, shows it in the portal and the admin, and names it in <c>/.well-known/matgame</c>.
/// A module never changes the host's tables: it keeps its own schema. <c>MATGAME_GAMES=bunnywarfare,kingbee</c> limits which modules a host loads.
/// </summary>
public interface IGameModule
{
    GameInfo Info { get; }

    /// <summary>Its services (its DbContext on <see cref="GameEnvironment.ConnectionString"/> with its own schema, its room registry, its clock ...).</summary>
    void AddServices(IServiceCollection services, GameEnvironment environment);

    /// <summary>
    /// Its routes, on a group at <see cref="GameEnvironment.BasePath"/>: the web client (<c>/</c> and the client's own paths), the game API (<c>/api/...</c>),
    /// the hub (<c>/hub</c>). Use <see cref="GameApi.ForGame"/> on the API group: it checks the protocol and the antiforgery token of cookie requests.
    /// </summary>
    void MapGame(IEndpointRouteBuilder game, GameEnvironment environment);

    /// <summary>The entries of its own menu in <c>/Admin</c> (the menu switcher under the logo shows it; empty = no menu).</summary>
    IReadOnlyList<AdminLink> Admin { get; }

    /// <summary>Once at start, before the first request: migrate its schema, seed, close what the last run left open.</summary>
    Task StartAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>The numbers for the portal (cheap: called often).</summary>
    Task<GameStats> StatsAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>What the account reached in this game, or null when it never played.</summary>
    Task<GameProfile?> ProfileAsync(IServiceProvider services, long accountId, CancellationToken ct);

    /// <summary>The account was deleted: remove or anonymise what the game keeps about it.</summary>
    Task AccountDeletedAsync(IServiceProvider services, long accountId, CancellationToken ct);
}
