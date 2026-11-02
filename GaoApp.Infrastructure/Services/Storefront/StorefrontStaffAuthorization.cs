using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal static class StorefrontStaffAuthorization
{
    internal static async Task RequireAsync(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService? permissions,
        string permission, CancellationToken ct)
    {
        if (db.CurrentStoreId is not > 0 || !user.IsAuthenticated || user.UserId is not > 0 || permissions is null)
            throw new ForbiddenAppException("Cần nhân viên có quyền thật trong cửa hàng.");
        var store = db.CurrentStoreId.Value; var actor = user.UserId.Value;
        if (!await db.Stores.AnyAsync(x => x.Id == store && x.IsActive, ct) ||
            !await db.UserInStores.AnyAsync(x => x.StoreId == store && x.UserId == actor && x.IsActive && x.User.IsActive, ct) ||
            !await permissions.HasPermissionAsync(store, actor, StorefrontPermissions.ViewOrders, ct) ||
            !await permissions.HasPermissionAsync(store, actor, permission, ct))
            throw new ForbiddenAppException("Không có quyền thao tác đơn website của cửa hàng.");
    }
}
