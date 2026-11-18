using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MatGame.Sdk;

/// <summary>
/// The rules every game API follows (the host's own API too):
/// <list type="bullet">
/// <item>a change from the browser (cookie) carries the antiforgery token in the header <c>RequestVerificationToken</c>; an app (bearer token) does not need one,</item>
/// <item>an app that names a game version below the game's <see cref="GameInfo.MinClient"/> or another protocol gets <c>426</c>,</item>
/// <item>the apps may call from any origin (CORS policy <see cref="MatGameProtocol.AppCorsPolicy"/>, without cookies).</item>
/// </list>
/// </summary>
public static class GameApi
{
    /// <summary>The header of the antiforgery token (the host registers antiforgery with it).</summary>
    public const string AntiforgeryHeader = "RequestVerificationToken";

    /// <summary>Applies the rules to a group of a game: <c>game.MapGroup("/api").ForGame(Info)</c>.</summary>
    public static RouteGroupBuilder ForGame(this RouteGroupBuilder group, GameInfo game)
    {
        group.RequireCors(MatGameProtocol.AppCorsPolicy);
        group.AddEndpointFilter((context, next) => CheckClient(context, next, game));
        group.AddEndpointFilter(CheckAntiforgery);
        return group;
    }

    /// <summary>The version check of <see cref="ForGame"/> for other places (the hub's negotiation, a page): null = fine, else the message.</summary>
    public static string? ClientProblem(HttpRequest request, GameInfo game)
    {
        var header = request.Headers[MatGameProtocol.ClientHeader].ToString();
        if (string.IsNullOrEmpty(header))
        {
            header = request.Query["mgc"].ToString();
        }

        if (string.IsNullOrEmpty(header))
        {
            return null; // the browser on the master: always the version the master serves
        }

        var protocol = request.Headers[MatGameProtocol.ProtocolHeader].ToString();
        if (string.IsNullOrEmpty(protocol))
        {
            protocol = request.Query["mgp"].ToString();
        }

        if (protocol.Length > 0 && protocol != MatGameProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return "Diese App passt nicht zu diesem Server. Bitte die App aktualisieren.";
        }

        var (id, version, _) = MatGameProtocol.ParseClient(header);
        if (id is not null && !string.Equals(id, game.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null; // a header of another game (the portal) is not our business
        }

        return GameVersion.AtLeast(version, game.MinClient)
            ? null
            : $"Deine App ist zu alt für diesen Server (mindestens {game.MinClient}). Bitte die App aktualisieren.";
    }

    private static async ValueTask<object?> CheckClient(EndpointFilterInvocationContext context, EndpointFilterDelegate next, GameInfo game)
    {
        var problem = ClientProblem(context.HttpContext.Request, game);
        return problem is null ? await next(context) : Results.Json(new { error = problem, update = true }, statusCode: StatusCodes.Status426UpgradeRequired);
    }

    private static async ValueTask<object?> CheckAntiforgery(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var change = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method);
        var app = http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        if (change && !app)
        {
            var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(http))
            {
                return Results.BadRequest(new { error = "Die Sitzung ist abgelaufen – bitte die Seite neu laden.", field = (string?)null });
            }
        }

        return await next(context);
    }
}
