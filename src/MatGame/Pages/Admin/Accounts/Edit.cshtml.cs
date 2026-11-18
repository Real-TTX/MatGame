using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk;
using MatGame.Web;
using Microsoft.AspNetCore.Mvc;

namespace MatGame.Pages.Admin.Accounts;

public sealed class EditModel(AppDbContext db, GameHost host) : HostPageModel(db)
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string LoginName { get; set; } = string.Empty;

    [BindProperty]
    public bool IsAdmin { get; set; }

    [BindProperty]
    public bool IsActive { get; set; } = true;

    [BindProperty]
    public string? Password { get; set; }

    public Data.Account? Current { get; private set; }

    public IReadOnlyList<(string Game, GameProfile? Profile)> Profiles { get; private set; } = [];

    public bool IsNew => Id == 0;

    public bool IsSelf => Id == Me.AccountId;

    /// <summary>The tab shown first: the one with an error, else General.</summary>
    public string Tab => HasError(nameof(Password)) || HasError(nameof(IsAdmin)) || HasError(nameof(IsActive)) ? "security" : "general";

    private bool HasError(string key) => ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0;

    private async Task LoadAsync()
    {
        if (IsNew)
        {
            return;
        }

        Current = await AccountLogic.GetAsync(Db, Me, Id);
        if (Current is not null)
        {
            var profiles = await host.ProfilesAsync(HttpContext.RequestServices, Id, HttpContext.RequestAborted);
            Profiles = host.Games.Select(g => (g.Info.Name, profiles.GetValueOrDefault(g.Id))).ToList();
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        if (!IsNew && Current is null)
        {
            return NotFound();
        }

        if (Current is { } c)
        {
            DisplayName = c.DisplayName;
            LoginName = c.LoginName;
            IsAdmin = c.IsAdmin;
            IsActive = c.IsActive;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
        => await TryAsync(async () =>
        {
            var id = await AccountLogic.SaveAsync(Db, Me, new AccountInput(IsNew ? null : Id, DisplayName, LoginName, Password, IsAdmin, IsActive));
            Flash(IsNew ? "Das Konto wurde angelegt." : "Gespeichert.");
            return RedirectToPage(new { id });
        }, LoadAsync);

    public async Task<IActionResult> OnPostDeleteAsync()
        => await TryAsync(async () =>
        {
            await AccountLogic.DeleteAsync(Db, Me, Id);
            await host.AccountDeletedAsync(HttpContext.RequestServices, Id, HttpContext.RequestAborted);
            Flash("Das Konto wurde gelöscht.");
            return RedirectToPage("Index");
        }, LoadAsync);
}
