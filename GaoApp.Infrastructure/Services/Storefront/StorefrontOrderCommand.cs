using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

// One transaction/idempotency implementation for real staff and verified guest actors.
// Actor authorization and physical/financial proof are server-owned callbacks, never DTO flags.
internal static class StorefrontOrderCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static async Task<StorefrontOperationAck> RunAsync<T>(AppDbContext db, int store, int id, T request,
        string operation, int? actorUserId, object requestIdentity, Func<CancellationToken, Task> authorize,
        Func<CancellationToken, Task> prelock, Func<StorefrontOrder, CancellationToken, Task> change,
        TimeProvider clock, CancellationToken ct) where T : StorefrontOperationRequest
    {
        await authorize(ct);
        if (request.ClientRequestId == Guid.Empty || request.ExpectedRevision <= 0 || (request.Note?.Length ?? 0) > 2000)
            throw new ValidationAppException("Thiếu mã yêu cầu/phiên bản hoặc ghi chú quá dài.");
        byte[] version;
        try { version = Convert.FromBase64String(request.ExpectedVersion ?? ""); }
        catch (FormatException) { throw new ValidationAppException("Phiên bản đơn không hợp lệ."); }
        if (version.Length != 8 || Convert.ToBase64String(version) != request.ExpectedVersion)
            throw new ValidationAppException("Phiên bản đơn không hợp lệ.");
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.Entries().Any(x => x.State is EntityState.Modified or EntityState.Added or EntityState.Deleted))
            throw new ConflictAppException("Cần gửi thao tác trong phiên dữ liệu sạch.");
        db.ChangeTracker.Clear();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestIdentity, Json))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await StorefrontFulfillmentService.Lock(db, $"GaoApp:Storefront:Command:{store}:{request.ClientRequestId:N}", ct);
            await prelock(ct);
            await StorefrontFulfillmentService.Lock(db, $"GaoApp:Storefront:Order:{store}:{id}", ct);
            await authorize(ct);
            var receipt = await db.Set<StorefrontCommandReceipt>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.StoreId == store && x.Operation == operation && x.ClientRequestId == request.ClientRequestId, ct);
            if (receipt is not null)
            {
                if (receipt.RequestHash != hash || receipt.ActorUserId != actorUserId || receipt.StorefrontOrderId != id)
                    throw new ConflictAppException("Mã yêu cầu đã dùng cho thao tác hoặc nội dung khác.");
                var previous = JsonSerializer.Deserialize<StorefrontOperationAck>(receipt.OutcomeJson, Json)
                    ?? throw new ConflictAppException("Kết quả trước chưa đọc được.");
                await tx.CommitAsync(ct); return previous with { Replayed = true };
            }
            var order = await db.StorefrontOrders.SingleOrDefaultAsync(x => x.StoreId == store && x.Id == id, ct)
                ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
            if (order.Revision != request.ExpectedRevision || !order.RowVersion.SequenceEqual(version))
                throw new ConflictAppException("Đơn đã thay đổi; tải lại trước khi thao tác.");
            await change(order, ct);
            order.Revision++; await db.SaveChangesAsync(ct);
            var result = new StorefrontOperationAck(id, operation, order.Revision, Convert.ToBase64String(order.RowVersion), false);
            db.Add(new StorefrontCommandReceipt { StoreId = store, ClientRequestId = request.ClientRequestId, Operation = operation,
                RequestHash = hash, StorefrontOrderId = id, ActorUserId = actorUserId, OutcomeJson = JsonSerializer.Serialize(result, Json) });
            var eventId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new { eventId, id, order.Code, order.Revision, operation, actor = actorUserId, atUtc = clock.GetUtcNow().UtcDateTime }, Json);
            db.Add(new StorefrontEvent { StoreId = store, StorefrontOrderId = id, EventId = eventId, CausationId = request.ClientRequestId,
                ActorUserId = actorUserId, Revision = order.Revision, Kind = operation, PayloadJson = payload });
            db.Add(new StorefrontOutboxMessage { StoreId = store, StorefrontOrderId = id, EventId = eventId,
                Revision = order.Revision, Kind = operation, PayloadJson = payload });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new ConflictAppException("Đơn hoặc kho vừa thay đổi. Tải lại và thử lại cùng mã yêu cầu.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or -2 or 51140)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new StorefrontRetryableCommandException("Kho/đơn đang được cập nhật. Thử lại cùng mã yêu cầu.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 or -2 or 51140 })
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new StorefrontRetryableCommandException("Kho/đơn đang được cập nhật. Thử lại cùng mã yêu cầu.");
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
