using MatGame.Business;
using MatGame.Data;
using Microsoft.AspNetCore.Mvc;

namespace MatGame.Pages;

/// <summary>/link?code=ABCD-EFGH: the player confirms (or denies) the sign-in of an app. Not signed in: the sign-in panel first, then back here.</summary>
public sealed class LinkModel(AppDbContext db) : HostPageModel(db)
{
    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    public DeviceLinkView? Link { get; private set; }

    public ServerSettings Settings { get; private set; } = ServerSettings.Default;

    public bool CanRegister { get; private set; }

    public bool FirstRun { get; private set; }

    public bool IdentityEnabled { get; private set; }

    /// <summary>"approved" or "denied" after the decision.</summary>
    public string? Done { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        await LoadAsync();
        Done = done is "approved" or "denied" ? done : null;
    }

    public async Task<IActionResult> OnPostAsync(string decision)
    {
        return await TryAsync(async () =>
        {
            await DeviceLogic.DecideLinkAsync(Db, Me, Code, decision == "approve");
            return RedirectToPage(new { code = (string?)null, done = decision == "approve" ? "approved" : "denied" });
        }, LoadAsync);
    }

    private async Task LoadAsync()
    {
        Settings = await SettingLogic.GetAsync(Db);
        CanRegister = await AccountLogic.CanRegisterAsync(Db);
        FirstRun = !await AccountLogic.HasAnyAsync(Db);
        IdentityEnabled = HttpContext.RequestServices.GetService<MatPlatform.MatPlatformSettings>()?.IdentityEnabled == true;
        Link = await DeviceLogic.FindLinkAsync(Db, Code);
        ViewData["ServerName"] = Settings.Name;
    }
}
