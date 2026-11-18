using MatGame.Business;
using MatGame.Data;
using Microsoft.AspNetCore.Mvc;

namespace MatGame.Pages.Admin;

public sealed class SettingsModel(AppDbContext db) : HostPageModel(db)
{
    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public bool RegistrationOpen { get; set; }

    [BindProperty]
    public bool GuestsAllowed { get; set; }

    [BindProperty]
    public string? MovedTo { get; set; }

    /// <summary>The tab shown first: the one with an error, else General.</summary>
    public string Tab => ModelState.TryGetValue(nameof(MovedTo), out var e) && e.Errors.Count > 0 ? "address" : "general";

    public async Task OnGetAsync()
    {
        Guard.Admin(Me);
        var s = await SettingLogic.GetAsync(Db);
        (Name, RegistrationOpen, GuestsAllowed, MovedTo) = (s.Name, s.RegistrationOpen, s.GuestsAllowed, s.MovedTo);
    }

    public async Task<IActionResult> OnPostAsync()
        => await TryAsync(async () =>
        {
            await SettingLogic.SaveAsync(Db, Me, new ServerSettings(Name, RegistrationOpen, GuestsAllowed, MovedTo));
            Flash("Gespeichert.");
            return RedirectToPage();
        });
}
