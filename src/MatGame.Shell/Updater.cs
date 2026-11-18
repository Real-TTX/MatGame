using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace MatGame.Shell;

/// <summary>
/// The manifest CI publishes in the rolling release of a channel (<c>release-channel</c>, <c>nightly-channel</c>) of the game's repository:
/// <c>&lt;game&gt;-update.json</c> next to the setup exe.
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("buildNumber")] int BuildNumber,
    [property: JsonPropertyName("setup")] string Setup,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>Updates over the air from GitHub releases: check, download, check the SHA-256, start the new exe with <c>--update</c>, end.</summary>
public sealed class Updater(ShellOptions options)
{
    private static readonly HttpClient Http = CreateClient();

    public string ManifestName => $"{options.GameId}-update.json";

    public bool CanUpdate => options.Repository is not null && options.Channel != "local" && Environment.GetEnvironmentVariable("MATGAME_NO_UPDATES") != "1";

    private string Base(string channel) => $"https://github.com/{options.Repository}/releases/download/{channel}-channel";

    /// <summary>The newer version of the own channel, or null (also when offline: updates are a nice-to-have).</summary>
    public async Task<UpdateManifest?> CheckAsync(CancellationToken ct = default)
    {
        if (!CanUpdate)
        {
            return null;
        }

        try
        {
            var manifest = await Http.GetFromJsonAsync<UpdateManifest>($"{Base(options.Channel)}/{ManifestName}", ct);
            var newer = manifest is not null
                        && string.Equals(manifest.Channel, options.Channel, StringComparison.OrdinalIgnoreCase)
                        && manifest.BuildNumber > options.BuildNumber;
            ShellLog.Write($"update check: {(newer ? $"{manifest!.Version} available" : "up to date")}");
            return newer ? manifest : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            ShellLog.Write($"update check failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Downloads the setup, checks it and starts it with <c>--update</c> (it ends this game and installs itself).</summary>
    public async Task InstallAsync(UpdateManifest manifest, CancellationToken ct = default)
    {
        var target = Path.Combine(Path.GetTempPath(), "MatGame", options.GameId, manifest.Setup);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var file = File.Create(target))
        {
            await using var download = await Http.GetStreamAsync($"{Base(manifest.Channel)}/{manifest.Setup}", ct);
            await download.CopyToAsync(file, ct);
        }

        await using (var check = File.OpenRead(target))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException("Das Update ist beschädigt (Prüfsumme stimmt nicht).");
            }
        }

        ShellLog.Write($"starting update to {manifest.Version}");
        Process.Start(new ProcessStartInfo(target, $"{Installer.UpdateArgument} {Installer.QuietArgument}") { UseShellExecute = false });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MatGame.Shell");
        return client;
    }
}
