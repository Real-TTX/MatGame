using MatGame.Sdk;

namespace MatGame;

/// <summary>
/// The games of this host, compiled in from the sibling repositories (MatGame.csproj, MSBuild property <c>MatGameGames</c>: "all", "none" or a list).
/// A new game: its plugin project (an <see cref="IGameModule"/> in a Razor class library with <c>StaticWebAssetBasePath</c> = its id), a ProjectReference with
/// its switch in MatGame.csproj, a line here, its build context in the Dockerfile and the compose files, and its checkout in the CI.
/// </summary>
public static class Games
{
    /// <summary>Extra modules for tests (the sample game); set before the host is built.</summary>
    public static List<IGameModule> Extra { get; } = [];

    public static IEnumerable<IGameModule> All()
    {
#if GAME_BUNNYWARFARE
        yield return new BunnyWarfare.Module.BunnyWarfareModule();
#endif
#if GAME_KINGBEE
        yield return new KingBee.Module.KingBeeModule();
#endif
        foreach (var module in Extra)
        {
            yield return module;
        }
    }
}
