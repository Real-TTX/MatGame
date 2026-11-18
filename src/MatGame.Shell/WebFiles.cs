using System.Reflection;

namespace MatGame.Shell;

/// <summary>The browser files of a game: a path below the origin (<c>/bunnywarfare/js/game/main.js</c>) → its content.</summary>
public interface IWebFiles
{
    /// <summary>The file, or null when there is none.</summary>
    Stream? Open(string path);
}

/// <summary>
/// The files embedded into the app (<c>&lt;EmbeddedResource Include="..\Module\wwwroot\**" LogicalName="web/bunnywarfare/%(RecursiveDir)%(Filename)%(Extension)" /&gt;</c>):
/// one exe holds the whole game.
/// </summary>
public sealed class EmbeddedWebFiles : IWebFiles
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _names;

    public EmbeddedWebFiles(Assembly assembly, string prefix = "web/")
    {
        _assembly = assembly;
        _names = assembly.GetManifestResourceNames()
            .Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(n => "/" + n.Replace('\\', '/')[prefix.Length..], n => n, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _names.Count;

    public Stream? Open(string path) => _names.TryGetValue(path, out var name) ? _assembly.GetManifestResourceStream(name) : null;
}

/// <summary>Files from a folder (development: <c>MATGAME_WEBROOT</c> points at the folder that contains <c>&lt;game&gt;/</c>, edits show after a reload).</summary>
public sealed class FolderWebFiles(string root) : IWebFiles
{
    private readonly string _root = Path.GetFullPath(root);

    public Stream? Open(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            return null;
        }

        return File.OpenRead(full);
    }
}

/// <summary>The content types the games need.</summary>
public static class MimeTypes
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".webmanifest"] = "application/manifest+json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".glb"] = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".wasm"] = "application/wasm",
        [".txt"] = "text/plain; charset=utf-8",
        [".bin"] = "application/octet-stream",
    };

    public static string Of(string path) => Types.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";
}
