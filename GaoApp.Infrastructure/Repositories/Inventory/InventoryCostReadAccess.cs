using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

internal static class InventoryCostReadAccess
{
    // The caller's identity comes from ICurrentUser, never query parameters.
    // Recheck the active membership in the requested store on every read.
    internal static Task<bool> CanViewAsync(
        AppDbContext db, int storeId, int? userId, CancellationToken ct)
        => userId is not > 0
            ? Task.FromResult(false)
            : db.UserInStores.AsNoTracking().AnyAsync(membership =>
                membership.StoreId == storeId && membership.UserId == userId.Value
                && membership.IsActive && !membership.IsDeleted
                && membership.User.IsActive && !membership.User.IsDeleted
                && membership.Role.StoreId == storeId && !membership.Role.IsDeleted
                && membership.Role.IsSystemRole && membership.Role.Code == "ADMIN", ct);
}
