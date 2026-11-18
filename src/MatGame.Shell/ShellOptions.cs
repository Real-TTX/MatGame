using System.Reflection;
using MatGame.Local;

namespace MatGame.Shell;

/// <summary>What a game's app tells the shell (BunnyWarfare.App/Program.cs).</summary>
public sealed class ShellOptions
{
    /// <summary>The game id (<c>bunnywarfare</c>): the base path of its files, its data folder, the origin of the page.</summary>
    public required string GameId { get; init; }

    /// <summary>The name in the title bar, the Start menu and "Installierte Apps".</summary>
    public required string Title { get; init; }

    /// <summary>The page the window opens below the base path (<c>index.html</c>).</summary>
    public string StartPage { get; init; } = "index.html";

    /// <summary>The built-in master (the app's default server); the player can choose another one in the game.</summary>
    public string? DefaultServer { get; init; }

    /// <summary>The game's browser files. Default: the embedded resources of the entry assembly below <c>web/</c>.</summary>
    public IWebFiles? Files { get; init; }

    /// <summary>The offline game, or null when the browser part plays offline on its own (KingBee).</summary>
    public Func<ILocalGame>? LocalGame { get; init; }

    /// <summary>The GitHub repository of the game's releases (<c>Real-TTX/BunnyWarfare</c>) for updates; null = no updates.</summary>
    public string? Repository { get; init; }

    /// <summary>The colour of the window while the page loads.</summary>
    public Color Background { get; init; } = Color.FromArgb(0x1f, 0x15, 0x0e);

    /// <summary>The version of the app: the informational version of the entry assembly (release 0.2.12-20261118, nightly-12-20261118, local-20261118).</summary>
    public string Version { get; init; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";

    /// <summary>The data of the game on this device: <c>%APPDATA%\MatGame\&lt;game&gt;</c> (offline profile, storage, tokens, log).</summary>
    public string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatGame", GameId);

    /// <summary>The origin of the page in the app (a reserved name that never reaches the network: the shell answers every request itself).</summary>
    public string Origin => $"https://{GameId}.matgame.invalid";

    /// <summary>The address the window opens.</summary>
    public string StartUrl => $"{Origin}/{GameId}/{StartPage}";

    /// <summary>release, nightly or local (from <see cref="Version"/>).</summary>
    public string Channel => Version.StartsWith("nightly", StringComparison.OrdinalIgnoreCase) ? "nightly"
        : Version.StartsWith("local", StringComparison.OrdinalIgnoreCase) ? "local" : "release";

    /// <summary>The build number (release: the third part, nightly: the second; 0 for local).</summary>
    public int BuildNumber
    {
        get
        {
            var parts = Version.Split('-', '.');
            return Channel switch
            {
                "nightly" when parts.Length > 1 && int.TryParse(parts[1], out var n) => n,
                "release" when parts.Length > 2 && int.TryParse(parts[2], out var b) => b,
                _ => 0,
            };
        }
    }
}
