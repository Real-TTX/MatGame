using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk.Web;
using Microsoft.AspNetCore.Mvc;

namespace MatGame.Pages.Admin.Devices;

public sealed class IndexModel(AppDbContext db) : HostPageModel(db)
{
    public IReadOnlyList<DeviceRow> Items { get; private set; } = [];

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var request = ReadList(["platform"]);
        var result = await DeviceLogic.ListAsync(Db, Me, request);
        Items = result.Items;
        Toolbar = new ToolbarModel
        {
            Id = "devices", Action = "/Admin/Devices", Label = "Geräte", Search = request.Search, SearchField = true, SearchPlaceholder = "Gerät oder Konto",
            Filters =
            [
                new FilterField("platform", "Plattform",
                    [new FilterOption("windows", "Windows"), new FilterOption("android", "Android"), new FilterOption("ios", "iOS"), new FilterOption("macos", "macOS")], request.Filter("platform")),
            ],
            SortOptions = [new SortOption("used", "Zuletzt benutzt"), new SortOption("name", "Gerät"), new SortOption("account", "Konto")],
            Sort = request.Sort, Descending = request.Descending, DefaultSort = "used",
            Total = result.Total, Matching = result.Matching, Noun = "Geräte", NounOne = "Gerät",
        };
        Pager = BuildPager(Toolbar, request.Page, request.PageSize, result.Matching);
    }

    public async Task<IActionResult> OnPostRemoveAsync(long id)
    {
        try
        {
            await DeviceLogic.RemoveAsync(Db, Me, id);
            Flash("Das Gerät wurde abgemeldet.");
        }
        catch (BusinessException e)
        {
            FlashError(e.Message);
        }

        return RedirectToPage();
    }
}
