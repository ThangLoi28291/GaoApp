using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Security;

public sealed class StoreAdminAccess(AppDbContext db, ICurrentStore store, ICurrentUser user) : IStoreAdminAccess
{
    public Task<bool> IsAdminAsync(CancellationToken ct = default)
        => user.UserId is not > 0 || store.StoreId <= 0
            ? Task.FromResult(false)
            : db.UserInStores.AsNoTracking().AnyAsync(m =>
                m.StoreId == store.StoreId && m.UserId == user.UserId.Value
                && m.IsActive && !m.IsDeleted && m.User.IsActive && !m.User.IsDeleted
                && m.Role.StoreId == store.StoreId && !m.Role.IsDeleted
                && m.Role.IsSystemRole && m.Role.Code == "ADMIN", ct);

    public async Task RequireAsync(CancellationToken ct = default)
    {
        if (!await IsAdminAsync(ct))
            throw new ForbiddenAppException("Chỉ admin của cửa hàng được quản lý phiếu nhận ca và xác nhận tiền bàn giao.");
    }
}
