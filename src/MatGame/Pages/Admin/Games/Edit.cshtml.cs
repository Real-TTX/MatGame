using MatGame.Business;
using MatGame.Data;
using MatGame.Web;
using Microsoft.AspNetCore.Mvc;

namespace MatGame.Pages.Admin.Games;

public sealed class EditModel(AppDbContext db, GameHost host) : HostPageModel(db)
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public bool IsEnabled { get; set; } = true;

    [BindProperty]
    public string? MinClient { get; set; }

    public GameState? Game { get; private set; }

    private async Task LoadAsync()
    {
        Guard.Admin(Me);
        var game = host.Find(Id);
        Game = game is null ? null : await host.StateAsync(game, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        if (Game is null)
        {
            return NotFound();
        }

        IsEnabled = Game.IsEnabled;
        MinClient = Game.MinClientOverride;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
        => await TryAsync(async () =>
        {
            await LoadAsync();
            if (Game is null)
            {
                return NotFound();
            }

            await GameLogic.SaveAsync(Db, Me, Game.Info.Id, IsEnabled, MinClient);
            await host.RefreshAsync(HttpContext.RequestAborted);
            Flash("Gespeichert.");
            return RedirectToPage(new { id = Id });
        }, LoadAsync);
}
