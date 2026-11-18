using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatGame.Tests.SampleGame.Pages.Admin.Sample;

public sealed class IndexModel : PageModel
{
    public string Greeting { get; private set; } = "Hallo aus dem Beispielspiel";

    public void OnGet()
    {
    }
}
