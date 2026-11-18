using MatPlatform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatGame.Pages.Account;

/// <summary>
/// Where a protected page sends a visitor who is not signed in. With MatIdentity (MATIDENTITY_AUTHORITY) the sign-in goes there at once (unless <c>?local=1</c>,
/// <c>?signedOut=1</c> or after an error); otherwise, and for <c>?local=1</c>, the sign-in panel of the portal opens and returns to the page.
/// </summary>
public sealed class LoginModel(MatPlatformSettings platform) : PageModel
{
    public IActionResult OnGet(string? returnUrl, string? local, string? signedOut, string? ssoError, string? link, string? sso)
    {
        var back = returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/";
        var auto = platform.IdentityEnabled && local is null && signedOut is null && ssoError is null && link is null;
        if (auto || (platform.IdentityEnabled && sso is not null))
        {
            return Challenge(new AuthenticationProperties { RedirectUri = back }, MatIdentitySignIn.Scheme);
        }

        return Redirect(back == "/" ? "/?login=1" : $"/?login=1&returnUrl={Uri.EscapeDataString(back)}");
    }
}
