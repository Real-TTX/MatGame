using MatGame.Data;
using Microsoft.EntityFrameworkCore;

namespace MatGame.Business;

/// <summary>The numbers of the overview.</summary>
public sealed record AdminNumbers(int Accounts, int Guests, int Devices);

public static class AdminLogic
{
    public static async Task<AdminNumbers> NumbersAsync(AppDbContext db, Actor actor, CancellationToken ct = default)
    {
        Guard.Admin(actor);
        var alive = db.Accounts.Where(a => a.UpdateState != UpdateState.Deleted);
        return new AdminNumbers(await alive.CountAsync(a => !a.IsGuest, ct), await alive.CountAsync(a => a.IsGuest, ct), await db.Devices.CountAsync(ct));
    }
}
