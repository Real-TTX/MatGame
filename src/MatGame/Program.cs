using System.Globalization;
using System.Threading.RateLimiting;
using MatGame.Business;
using MatGame.Data;
using MatGame.Platform;
using MatGame.Sdk;
using MatGame.Web;
using MatPlatform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--healthcheck"))
{
    return await HealthCheck.RunAsync();
}

// numbers in forms and in the query are always written with a point (input type=number), whatever the language of the server
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? $"http://+:{AppInfo.Port}");

// the games' files (static web assets of the plugins under /<game>/) also outside Development (tests, dotnet run in Production); published apps have them in wwwroot
builder.WebHost.UseStaticWebAssets();

// ---- the database (PostgreSQL): the default fits the compose files, no variable is needed; MATGAME_DB overrides it ----
var connection = builder.Configuration["MATGAME_DB"]
                 ?? builder.Configuration.GetConnectionString("Default")
                 ?? "Host=db;Port=5432;Database=matgame;Username=matgame;Password=matgame";
builder.Services.AddDbContext<AppDbContext>(o => AppDbContext.Use(o, connection));

// the keys that protect the cookie are in the volume: a sign-in survives a restart of the container
var dataDirectory = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? "/data", builder.Environment.ContentRootPath);
var keys = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keys);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("MatGame");

// ---- the Mat platform contract (/api, /api/health, state, configure, actions; MAT_API_KEY or a token of MatIdentity) ----
var platform = builder.AddMatPlatform<MatGamePlatform>(MatGamePlatform.Describe, o =>
{
    o.DataDirectory = dataDirectory;
    o.ConnectionsAccess = http => Task.FromResult(http.User.IsInRole(GameAccounts.AdminRole));
    o.UiLanguage = _ => "de";
});

// ---- sign-in: the browser's cookie, the apps' device tokens, optionally MatIdentity (MATIDENTITY_AUTHORITY) ----
builder.Services.AddMatGameAuthentication(auth => auth.AddMatIdentitySignIn<MatGameIdentityUsers>(platform, o =>
{
    o.CookieScheme = GameAccounts.CookieScheme;
    o.LoginPath = "/Account/Login";
}));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GameAccounts.AdminPolicy, p => p.AddAuthenticationSchemes(GameAccounts.AnyScheme).RequireRole(GameAccounts.AdminRole))
    // the same name as a policy: a plugin may write RequireAuthorization(GameAccounts.AnyScheme) or [Authorize(AuthenticationSchemes = GameAccounts.AnyScheme)]
    .AddPolicy(GameAccounts.AnyScheme, p => p.AddAuthenticationSchemes(GameAccounts.AnyScheme).RequireAuthenticatedUser());

builder.Services.AddRazorPages(o =>
{
    // the plugins' admin pages live under /Admin/<Game>/ too: the folder rule covers them
    o.Conventions.AuthorizeFolder("/Admin", GameAccounts.AdminPolicy);
}).AddMvcOptions(o =>
{
    // the business layer validates (with German messages at the field); the framework only reports values it cannot read
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    var m = o.ModelBindingMessageProvider;
    m.SetValueMustNotBeNullAccessor(_ => "Bitte ausfüllen.");
    m.SetMissingBindRequiredValueAccessor(_ => "Bitte ausfüllen.");
    m.SetMissingKeyOrValueAccessor(() => "Bitte ausfüllen.");
    m.SetAttemptedValueIsInvalidAccessor((value, _) => $"„{value}“ ist kein gültiger Wert.");
    m.SetValueIsInvalidAccessor(value => $"„{value}“ ist kein gültiger Wert.");
    m.SetValueMustBeANumberAccessor(_ => "Bitte eine Zahl eingeben.");
    m.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"„{value}“ ist kein gültiger Wert.");
    m.SetNonPropertyValueMustBeANumberAccessor(() => "Bitte eine Zahl eingeben.");
    m.SetUnknownValueIsInvalidAccessor(_ => "Ungültiger Wert.");
    m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Ungültiger Wert.");
    m.SetMissingRequestBodyRequiredValueAccessor(() => "Bitte ausfüllen.");
});
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 64 * 1024);
builder.Services.AddAntiforgery(o => o.HeaderName = GameApi.AntiforgeryHeader);
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream", "image/svg+xml", "application/manifest+json"]);
});

// the apps call from their own origin (WebView2: https://<game>.app, MAUI ...): any origin, bearer tokens only, never cookies
builder.Services.AddCors(o => o.AddPolicy(MatGameProtocol.AppCorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static RateLimitPartition<string> PerAddress(HttpContext http, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 });
    o.AddPolicy(RateLimits.SignIn, http => PerAddress(http, 10, TimeSpan.FromMinutes(1)));
    o.AddPolicy(RateLimits.Device, http => PerAddress(http, 60, TimeSpan.FromMinutes(1)));
    o.AddPolicy(RateLimits.Discovery, http => PerAddress(http, 120, TimeSpan.FromMinutes(1)));
    o.OnRejected = async (context, ct) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Zu viele Versuche. Bitte in einer Minute noch einmal." }, ct);
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // behind a reverse proxy (Matcad, Caddy, nginx, Traefik) the address and the scheme of the visitor are in the headers
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddSingleton<IGameAccounts, HostGameAccounts>();
builder.Services.AddSingleton<Assets>();
builder.Services.AddHostedService<Housekeeping>();

// ---- the games (plugins): their services now, their routes below ----
builder.AddGames(connection, dataDirectory);

var app = builder.Build();

// ---- the database is brought up to date, then every game starts (migrates its own schema, seeds, closes what the last run left open) ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception e) when (attempt < 15)
        {
            log.LogWarning("The database is not ready yet ({Message}), attempt {Attempt}", e.Message, attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

await app.StartGamesAsync();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseResponseCompression();
app.UseMiddleware<SecurityHeaders>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // the games' modules import each other without a version: the browser asks every time (ETag), so a new release is never mixed with an old module
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "no-cache";
        }
    },
});
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<GameGate>();
app.UseAuthorization();

app.MapDiscovery();
app.MapHostApi();
app.MapMatPlatform();
app.MapRazorPages();
app.MapGames();

await app.RunAsync();
return 0;

/// <summary>The entry point (for WebApplicationFactory in the tests).</summary>
public partial class Program;
