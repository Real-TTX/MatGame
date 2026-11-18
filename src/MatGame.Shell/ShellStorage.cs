using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MatGame.Shell;

/// <summary>
/// The storage of the bridge: <c>storage.json</c> (key → JSON text) and <c>secure.json</c> (key → value protected with DPAPI for the current Windows user)
/// in the game's data folder. Written atomically (temp file + move), so a crash never leaves half a file.
/// </summary>
public sealed class ShellStorage
{
    private readonly string _plainFile;
    private readonly string _secureFile;
    private readonly Lock _sync = new();

    public ShellStorage(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _plainFile = Path.Combine(dataDirectory, "storage.json");
        _secureFile = Path.Combine(dataDirectory, "secure.json");
    }

    public string? Get(string key)
    {
        lock (_sync)
        {
            return Read(_plainFile).GetValueOrDefault(key);
        }
    }

    public void Set(string key, string? value)
    {
        lock (_sync)
        {
            var all = Read(_plainFile);
            if (value is null)
            {
                all.Remove(key);
            }
            else
            {
                all[key] = value;
            }

            Write(_plainFile, all);
        }
    }

    public string? GetSecret(string key)
    {
        lock (_sync)
        {
            var stored = Read(_secureFile).GetValueOrDefault(key);
            if (stored is null)
            {
                return null;
            }

            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy(key), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            {
                return null; // another Windows user or a broken entry: as if there was none
            }
        }
    }

    public void SetSecret(string key, string? value)
    {
        lock (_sync)
        {
            var all = Read(_secureFile);
            if (value is null)
            {
                all.Remove(key);
            }
            else
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy(key), DataProtectionScope.CurrentUser);
                all[key] = Convert.ToBase64String(bytes);
            }

            Write(_secureFile, all);
        }
    }

    private static byte[] Entropy(string key) => Encoding.UTF8.GetBytes("MatGame.Shell:" + key);

    private static Dictionary<string, string> Read(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }

    private static void Write(string file, Dictionary<string, string> all)
    {
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, overwrite: true);
    }
}
