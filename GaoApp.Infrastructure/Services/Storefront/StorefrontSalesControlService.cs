using System.Data;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontSalesControlService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions, IConfiguration configuration, TimeProvider? clock = null) : IStorefrontSalesControlService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async Task Guard(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not > 0 ||
            !await permissions.HasPermissionAsync(Store, user.UserId.Value, permission, ct) ||
            !await db.Stores.AsNoTracking().AnyAsync(x => x.Id == Store && x.IsActive, ct) ||
            !await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == user.UserId && x.IsActive && x.User.IsActive, ct))
            throw new ForbiddenAppException("Không có quyền điều khiển nhận đơn tại cửa hàng này.");
    }

    public async Task<StorefrontSalesControlState> GetAsync(CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewSettings, ct);
        var entity = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
        var canManage = await permissions.HasPermissionAsync(Store, user.UserId!.Value, StorefrontPermissions.ManageSettings, ct);
        var blockers = await new StorefrontActivationPrerequisites(db, configuration, clock).EvaluateAsync(ct);
        return new(entity?.IsEnabled == true, entity?.Revision ?? 0, Token(entity?.RowVersion),
            canManage && entity is { IsEnabled: false } && blockers.Count == 0,
            canManage && entity?.IsEnabled == true, blockers);
    }

    public Task<StorefrontSalesControlState> ActivateAsync(StorefrontSalesControlRequest request, CancellationToken ct = default)
        => Change(request, true, ct);
    public Task<StorefrontSalesControlState> StopNewOrdersAsync(StorefrontSalesControlRequest request, CancellationToken ct = default)
        => Change(request, false, ct);

    private async Task<StorefrontSalesControlState> Change(StorefrontSalesControlRequest request, bool enable, CancellationToken ct)
    {
        await Guard(StorefrontPermissions.ManageSettings, ct);
        ArgumentNullException.ThrowIfNull(request);
        var supplied = Version(request.Version);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ValidationAppException("Cần lý do từ 1 đến 1.000 ký tự.");
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new ConflictAppException("Hãy hoàn tất thay đổi hiện tại trước khi điều khiển nhận đơn.");

        StorefrontSettings? entity = null; AuditLog? audit = null;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await Lock(ct);
            await Guard(StorefrontPermissions.ManageSettings, ct);
            entity = await db.StorefrontSettings.SingleOrDefaultAsync(x => x.StoreId == Store, ct)
                ?? throw new NotFoundAppException("Chưa có cấu hình website để điều khiển nhận đơn.");
            await db.Entry(entity).ReloadAsync(ct);
            if (entity.RowVersion is not { Length: 8 } current || !current.AsSpan().SequenceEqual(supplied))
                throw new ConflictAppException("Cấu hình đã thay đổi. Hãy tải lại trạng thái trước khi thao tác.");

            IReadOnlyList<string> blockers = ["ACTIVATION_REQUIRES_VALIDATION"];
            if (enable)
            {
                blockers = await new StorefrontActivationPrerequisites(db, configuration, clock).EvaluateAsync(ct);
                if (blockers.Count > 0) throw new ValidationAppException("Chưa thể cho phép nhận đơn: " + string.Join("; ", blockers.Select(StorefrontSalesControlMessages.Describe)) + ".");
            }
            if (entity.IsEnabled == enable)
            {
                await tx.CommitAsync(ct);
                return Result(entity, blockers);
            }

            var old = new { entity.IsEnabled, entity.Revision };
            entity.IsEnabled = enable; entity.Revision = checked(entity.Revision + 1);
            entity.UpdatedAtUtc = Now; entity.UpdatedBy = user.UserId;
            audit = new AuditLog
            {
                StoreId = Store, ActorUserId = user.UserId, ActorUserName = user.UserName,
                Module = AuditModuleType.System, ActionType = AuditActionType.Update,
                EntityName = nameof(StorefrontSettings), EntityId = entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Summary = enable ? "Cho phép cửa hàng nhận đơn website mới." : "Dừng nhận đơn website mới; tiếp tục xử lý đơn và tiền cũ.",
                OldValuesJson = JsonSerializer.Serialize(old),
                NewValuesJson = JsonSerializer.Serialize(new { entity.IsEnabled, entity.Revision, Reason = reason }),
                ChangedColumnsJson = JsonSerializer.Serialize(new[] { nameof(entity.IsEnabled), nameof(entity.Revision) }),
                CreatedAtUtc = Now, IsSuccess = true
            };
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result(entity, blockers);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Cấu hình đã thay đổi. Hãy tải lại trạng thái."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or -2)
        { throw new ConflictAppException("Cấu hình đang được xử lý. Hãy tải lại trạng thái và thử lại."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 or -2 })
        { throw new ConflictAppException("Cấu hình đang được xử lý. Hãy tải lại trạng thái và thử lại."); }
        finally
        {
            // Never clear unrelated tracked entities or any shared deferred work.
            if (entity is not null) db.Entry(entity).State = EntityState.Detached;
            if (audit is not null) db.Entry(audit).State = EntityState.Detached;
        }
    }

    private static StorefrontSalesControlState Result(StorefrontSettings entity, IReadOnlyList<string> blockers)
        => new(entity.IsEnabled, entity.Revision, Token(entity.RowVersion), false, entity.IsEnabled, blockers);
    private static string Token(byte[]? value) => value is { Length: 8 } ? Convert.ToBase64String(value) : "";
    private static byte[] Version(string? token)
    {
        try
        {
            var bytes = Convert.FromBase64String(token ?? "");
            if (bytes.Length != 8 || Convert.ToBase64String(bytes) != token) throw new FormatException();
            return bytes;
        }
        catch (FormatException) { throw new ValidationAppException("Phiên bản phải là rowversion 8 byte đúng định dạng. Hãy tải lại trang."); }
    }
    private async Task Lock(CancellationToken ct)
    {
        if (!db.CanWriteStoreData) throw new ForbiddenAppException("Không có phạm vi ghi dữ liệu cửa hàng.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = "GaoApp:Storefront:Configuration:" + Store; command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new ConflictAppException("Cấu hình đang được cập nhật. Hãy tải lại và thử lại.");
    }
}
