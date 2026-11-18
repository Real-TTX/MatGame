using System.Globalization;

namespace MatGame.Sdk;

/// <summary>
/// The protocol between the apps and a master (docs/PROTOCOL.md). Every request of an app carries <see cref="ProtocolHeader"/> (the platform protocol)
/// and <see cref="ClientHeader"/> (<c>bunnywarfare/1.4.0 (windows)</c>); a hub connection carries both as query values (<c>mgp</c>, <c>mgc</c>).
/// A client the master cannot serve gets <c>426</c> with <c>{ error, update: true }</c>.
/// </summary>
public static class MatGameProtocol
{
    /// <summary>The platform protocol (discovery, sign-in, devices). Rises only with a change old apps cannot follow.</summary>
    public const int Version = 1;

    public const string ProtocolHeader = "X-MatGame-Protocol";

    public const string ClientHeader = "X-MatGame-Client";

    /// <summary>The name of the CORS policy the host defines for the apps (any origin, no cookies, bearer tokens only).</summary>
    public const string AppCorsPolicy = "MatGame.Apps";

    /// <summary>The discovery document.</summary>
    public const string DiscoveryPath = "/.well-known/matgame";

    /// <summary>Reads <c>bunnywarfare/1.4.0 (windows)</c>: game id, version, platform (null parts when the header is missing or broken).</summary>
    public static (string? Game, string? Version, string? Platform) ParseClient(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return (null, null, null);
        }

        var text = header.Trim();
        string? platform = null;
        var open = text.IndexOf('(', StringComparison.Ordinal);
        if (open > 0 && text.EndsWith(')'))
        {
            platform = text[(open + 1)..^1].Trim();
            text = text[..open].Trim();
        }

        var slash = text.IndexOf('/', StringComparison.Ordinal);
        return slash <= 0 ? (text, null, platform) : (text[..slash], text[(slash + 1)..], platform);
    }
}

/// <summary>
/// Compares the versions of the family: release <c>1.4.12-20261118</c> (major.minor.build), nightly <c>nightly-42-20261118</c> (build only),
/// local <c>local-20261118</c> (always new enough: a developer's build).
/// </summary>
public static class GameVersion
{
    /// <summary>True when <paramref name="version"/> is at least <paramref name="minimum"/> (null or empty minimum = always).</summary>
    public static bool AtLeast(string? version, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        if (version.StartsWith("local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (version.StartsWith("nightly", StringComparison.OrdinalIgnoreCase))
        {
            // a nightly follows dev: it is never held back by the minimum of the releases
            return true;
        }

        return Compare(Parse(version), Parse(minimum)) >= 0;
    }

    private static int[] Parse(string version)
    {
        var core = version.Split('-', 2)[0];
        return core.Split('.').Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Concat([0, 0, 0]).Take(3).ToArray();
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < 3; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}
