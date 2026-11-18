using MatGame.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatGame.Pages.Account;

/// <summary>Signs out (POST from the admin's user menu) and goes back to the portal.</summary>
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await Identity.SignOutAsync(HttpContext);
        return Redirect("/");
    }
}
