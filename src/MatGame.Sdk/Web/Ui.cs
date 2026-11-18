using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace MatGame.Sdk.Web;

/// <summary>Small helpers for the markup of the toolkit (MatUi): icons, initials, dates.</summary>
public static partial class Ui
{
    public const string AppId = "matgame";
    public const string AppName = "MatGame";

    /// <summary>The accent of MatGame: violet (white text on it 7.1:1).</summary>
    public const string Accent = "#6d28d9";

    private static readonly TimeZoneInfo Zone = FindZone();

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();

    /// <summary>An icon from the inline sprite of the page (MatUi's and the app's own).</summary>
    public static IHtmlContent Icon(string name, string? extraClass = null)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not an icon name.", nameof(name));
        }

        var cls = string.IsNullOrEmpty(extraClass) ? "mat-icon" : $"mat-icon {extraClass}";
        return new HtmlString($"<svg class=\"{cls}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-{name}\"></use></svg>");
    }

    /// <summary>A small round swatch of a team colour.</summary>
    public static IHtmlContent Swatch(string? color)
        => IsColor(color) ? new HtmlString($"<span class=\"bw-swatch\" style=\"--bw-team:{color}\" aria-hidden=\"true\"></span>") : HtmlString.Empty;

    public static bool IsColor(string? value) => value is not null && ColorRegex().IsMatch(value);

    public static string Initials(string name)
    {
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return letters.Length == 0 ? "?" : letters;
    }

    /// <summary>A point in time for people (local time of Germany): 17.11.2026 14:05.</summary>
    public static string Stamp(DateTime? utc)
        => utc is null ? "–" : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Number(double value, string format = "0.##") => value.ToString(format, CultureInfo.GetCultureInfo("de-DE"));

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
