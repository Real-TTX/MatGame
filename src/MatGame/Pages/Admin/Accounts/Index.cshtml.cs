using MatGame.Business;
using MatGame.Data;
using MatGame.Sdk.Web;

namespace MatGame.Pages.Admin.Accounts;

public sealed class IndexModel(AppDbContext db) : HostPageModel(db)
{
    public IReadOnlyList<AccountRow> Items { get; private set; } = [];

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var request = ReadList(["kind", "status"]);
        var result = await AccountLogic.ListAsync(Db, Me, request);
        Items = result.Items;
        Toolbar = new ToolbarModel
        {
            Id = "accounts", Action = "/Admin/Accounts", Label = "Konten", Search = request.Search, SearchField = true, SearchPlaceholder = "Name oder Anmeldename",
            Filters =
            [
                new FilterField("kind", "Art", [new FilterOption("admin", "Administrator"), new FilterOption("player", "Spieler"), new FilterOption("guest", "Gast")], request.Filter("kind")),
                new FilterField("status", "Status", [new FilterOption("active", "Aktiv"), new FilterOption("disabled", "Gesperrt")], request.Filter("status")),
            ],
            SortOptions = [new SortOption("name", "Name"), new SortOption("login", "Letzte Anmeldung"), new SortOption("created", "Angelegt")],
            Sort = request.Sort, Descending = request.Descending, DefaultSort = "name",
            Total = result.Total, Matching = result.Matching, Noun = "Konten", NounOne = "Konto",
        };
        Pager = BuildPager(Toolbar, request.Page, request.PageSize, result.Matching);
    }
}
