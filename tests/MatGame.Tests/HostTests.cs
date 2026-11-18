using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MatGame.Business;
using MatGame.Data;
using MatGame.Tests.SampleGame;
using MatGame.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MatGame.Tests;

/// <summary>A database of its own for a test class on the server of MATGAME_TEST_DB (made and dropped).</summary>
public sealed class TestDatabase : IDisposable
{
    public static readonly string? Server = Environment.GetEnvironmentVariable("MATGAME_TEST_DB");

    private readonly string name = "mgtest_" + Guid.NewGuid().ToString("N")[..12];

    public TestDatabase()
    {
        if (Server is null)
        {
            return;
        }

        using var connection = new NpgsqlConnection(Server);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        command.ExecuteNonQuery();
        ConnectionString = new NpgsqlConnectionStringBuilder(Server) { Database = name, Pooling = false }.ConnectionString;
    }

    public string ConnectionString { get; } = string.Empty;

    public void Dispose()
    {
        if (Server is null)
        {
            return;
        }

        using var connection = new NpgsqlConnection(Server);
        connection.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        command.ExecuteNonQuery();
    }
}

/// <summary>The whole host in memory (with the sample game) against a real PostgreSQL: discovery, sign-in, guests, tokens, device link, version check, the plugin path.</summary>
public sealed class HostTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static HostTests()
    {
        if (!Games.Extra.OfType<SampleModule>().Any())
        {
            Games.Extra.Add(new SampleModule());
        }
    }

    private readonly TestDatabase database = new();
    private readonly string data = Path.Combine(Path.GetTempPath(), "matgame-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program>? app;

    public HostTests()
    {
        if (TestDatabase.Server is null)
        {
            return;
        }

        app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("MATGAME_DB", database.ConnectionString);
            b.UseSetting("DataDirectory", data);
        });
    }

    public void Dispose()
    {
        app?.Dispose();
        database.Dispose();
    }

    private HttpClient Browser() => app!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>A browser with the antiforgery token of its session in the header.</summary>
    private static async Task<HttpClient> WithAntiforgeryAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/matgame/session");
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", session.GetProperty("antiforgery").GetString());
        return client;
    }

    private async Task<HttpClient> SignedInBrowserAsync(string login, bool register)
    {
        var client = await WithAntiforgeryAsync(Browser());
        var response = register
            ? await client.PostAsJsonAsync("/api/matgame/session/register", new { displayName = login, loginName = login, password = "geheim123" })
            : await client.PostAsJsonAsync("/api/matgame/session/login", new { loginName = login, password = "geheim123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", session.GetProperty("antiforgery").GetString());
        return client;
    }

    private HttpClient App(string? token = null, string client = "sample/1.5.0 (windows)")
    {
        var http = app!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        http.DefaultRequestHeaders.Add("X-MatGame-Protocol", "1");
        http.DefaultRequestHeaders.Add("X-MatGame-Client", client);
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http;
    }

    [Fact]
    public async Task The_discovery_names_the_master_and_its_games()
    {
        if (app is null)
        {
            return;
        }

        var doc = await Browser().GetFromJsonAsync<JsonElement>("/.well-known/matgame");
        Assert.Equal("matgame", doc.GetProperty("kind").GetString());
        Assert.Equal(1, doc.GetProperty("protocol").GetInt32());
        Assert.True(doc.GetProperty("guests").GetBoolean());
        var sample = doc.GetProperty("games").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "sample");
        Assert.Equal("/sample", sample.GetProperty("path").GetString());
        Assert.Equal("1.2.0", sample.GetProperty("minClient").GetString());
        Assert.Equal("/sample/img/cover.svg", sample.GetProperty("cover").GetString());
        Assert.Equal("ok", sample.GetProperty("status").GetString());
        Assert.True(app.Services.GetRequiredService<SampleState>().Started);
    }

    [Fact]
    public async Task The_first_account_becomes_the_administrator_and_sees_the_plugin_admin_inside_the_host_layout()
    {
        if (app is null)
        {
            return;
        }

        var anonymous = await Browser().GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var admin = await SignedInBrowserAsync("chef", register: true);
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/matgame/session");
        Assert.True(session.GetProperty("account").GetProperty("isAdmin").GetBoolean());

        var page = await admin.GetStringAsync("/Admin/Sample");
        Assert.Contains("mat-nav", page, StringComparison.Ordinal);                   // the host's admin layout
        Assert.Contains("href=\"/Admin/Sample\"", page, StringComparison.Ordinal);    // the plugin's entry in the menu
        Assert.Contains("id=\"sample-admin\"", page, StringComparison.Ordinal);      // the plugin's page
        Assert.Contains("mat-field", page, StringComparison.Ordinal);                // md-field of the SDK rendered

        var dashboard = await admin.GetStringAsync("/Admin");
        Assert.Contains("Beispielspiel", dashboard, StringComparison.Ordinal);
        foreach (var path in new[] { "/Admin/Accounts", "/Admin/Devices", "/Admin/Games", "/Admin/Games/Edit?id=sample", "/Admin/Settings", "/Admin/Accounts/Edit" })
        {
            var response = await admin.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}");
        }

        // the second account is a player: no administration
        await admin.PostAsJsonAsync("/api/matgame/session/logout", new { });
        var player = await SignedInBrowserAsync("hoppel", register: true);
        var denied = await player.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/Denied", denied.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_from_the_browser_needs_the_antiforgery_token()
    {
        if (app is null)
        {
            return;
        }

        var response = await Browser().PostAsJsonAsync("/api/matgame/session/guest", new { name = "Flausch" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_guest_plays_and_becomes_an_account_with_the_same_id()
    {
        if (app is null)
        {
            return;
        }

        await SignedInBrowserAsync("chef", register: true);
        var guest = await WithAntiforgeryAsync(Browser());
        var created = await (await guest.PostAsJsonAsync("/api/matgame/session/guest", new { name = "Flausch" })).Content.ReadFromJsonAsync<JsonElement>();
        var account = created.GetProperty("account");
        Assert.True(account.GetProperty("isGuest").GetBoolean());
        var id = account.GetProperty("id").GetInt64();

        guest.DefaultRequestHeaders.Remove("RequestVerificationToken");
        guest.DefaultRequestHeaders.Add("RequestVerificationToken", created.GetProperty("antiforgery").GetString());
        var ping = await guest.GetFromJsonAsync<JsonElement>("/sample/api/ping");
        Assert.True(ping.GetProperty("account").GetProperty("isGuest").GetBoolean());

        var upgraded = await guest.PostAsJsonAsync("/api/matgame/account/upgrade", new { displayName = "Flausch", loginName = "flausch", password = "geheim123" });
        var after = (await upgraded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        Assert.Equal(id, after.GetProperty("id").GetInt64());
        Assert.False(after.GetProperty("isGuest").GetBoolean());
    }

    [Fact]
    public async Task An_app_signs_in_with_a_token_reaches_the_game_and_signs_out()
    {
        if (app is null)
        {
            return;
        }

        var answer = await App().PostAsJsonAsync("/api/matgame/token/register",
            new { displayName = "Möhre", loginName = "moehre", password = "geheim123", device = new { name = "Sample · Windows", platform = "windows", game = "sample" } });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var token = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var app1 = App(token);
        var session = await app1.GetFromJsonAsync<JsonElement>("/api/matgame/session");
        Assert.Equal("moehre", session.GetProperty("account").GetProperty("loginName").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("antiforgery").ValueKind);

        var ping = await app1.GetFromJsonAsync<JsonElement>("/sample/api/ping");
        Assert.True(ping.GetProperty("device").GetBoolean());
        var echo = await app1.PostAsJsonAsync("/sample/api/echo", new { text = "hi" }); // no antiforgery for a token
        Assert.Equal(HttpStatusCode.OK, echo.StatusCode);

        var devices = await app1.GetFromJsonAsync<JsonElement>("/api/matgame/account/devices");
        Assert.Single(devices.EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await app1.PostAsJsonAsync("/api/matgame/token/revoke", new { })).StatusCode);
        var gone = await app1.GetFromJsonAsync<JsonElement>("/api/matgame/session");
        Assert.False(gone.GetProperty("signedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app1.GetAsync("/api/matgame/account/devices")).StatusCode);
    }

    [Fact]
    public async Task A_device_signs_in_through_the_browser_with_a_code()
    {
        if (app is null)
        {
            return;
        }

        var browser = await SignedInBrowserAsync("chef", register: true);
        var start = await (await App().PostAsJsonAsync("/api/matgame/device/start", new { device = new { name = "Sample · Android", platform = "android", game = "sample" } }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var code = start.GetProperty("code").GetString()!;
        var deviceCode = start.GetProperty("deviceCode").GetString()!;
        Assert.Contains("/link?code=", start.GetProperty("verifyUrl").GetString(), StringComparison.Ordinal);

        var pending = await App().PostAsJsonAsync("/api/matgame/device/poll", new { deviceCode });
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);

        var page = await browser.GetStringAsync($"/link?code={code}");
        Assert.Contains(code, page, StringComparison.Ordinal);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var chef = (await AccountLogic.SignInAsync(db, "chef", "geheim123"))!;
            await DeviceLogic.DecideLinkAsync(db, new Actor(chef.Id, chef.DisplayName, chef.IsAdmin), code, approve: true);
        }

        var done = await App().PostAsJsonAsync("/api/matgame/device/poll", new { deviceCode });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var token = (await done.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var session = await App(token).GetFromJsonAsync<JsonElement>("/api/matgame/session");
        Assert.Equal("chef", session.GetProperty("account").GetProperty("loginName").GetString());

        // a code is used once
        Assert.Equal(HttpStatusCode.Gone, (await App().PostAsJsonAsync("/api/matgame/device/poll", new { deviceCode })).StatusCode);
    }

    [Fact]
    public async Task An_app_too_old_for_the_game_gets_426_and_a_switched_off_game_is_gone()
    {
        if (app is null)
        {
            return;
        }

        var old = await App(client: "sample/1.1.0 (windows)").GetAsync("/sample/api/ping");
        Assert.Equal((HttpStatusCode)426, old.StatusCode);
        Assert.True((await old.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("update").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await App(client: "sample/1.5.0 (windows)").GetAsync("/sample/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App(client: "sample/nightly-7-20261118 (windows)").GetAsync("/sample/api/ping")).StatusCode);
        var other = App();
        other.DefaultRequestHeaders.Remove("X-MatGame-Protocol");
        other.DefaultRequestHeaders.Add("X-MatGame-Protocol", "99");
        Assert.Equal((HttpStatusCode)426, (await other.GetAsync("/api/matgame/session")).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await GameLogic.SaveAsync(db, Actor.System, "sample", false, "1.6");
            await scope.ServiceProvider.GetRequiredService<GameHost>().RefreshAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await App().GetAsync("/sample/api/ping")).StatusCode);
        var doc = await Browser().GetFromJsonAsync<JsonElement>("/.well-known/matgame");
        var sample = doc.GetProperty("games").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "sample");
        Assert.Equal("off", sample.GetProperty("status").GetString());
        Assert.Equal("1.6", sample.GetProperty("minClient").GetString());
    }

    [Fact]
    public async Task The_portal_and_the_platform_contract_answer()
    {
        if (app is null)
        {
            return;
        }

        var portal = await Browser().GetStringAsync("/");
        Assert.Contains("Beispielspiel", portal, StringComparison.Ordinal);
        Assert.Contains("href=\"/sample/\"", portal, StringComparison.Ordinal);

        var definition = await Browser().GetFromJsonAsync<JsonElement>("/api");
        Assert.Equal("matgame", definition.GetProperty("app").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Browser().GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Browser().GetAsync("/api/state")).StatusCode);

        var games = await Browser().GetFromJsonAsync<JsonElement>("/api/matgame/games");
        Assert.Equal(2, games.EnumerateArray().Single(g => g.GetProperty("id").GetString() == "sample").GetProperty("stats").GetProperty("playersOnline").GetInt32());
    }
}
