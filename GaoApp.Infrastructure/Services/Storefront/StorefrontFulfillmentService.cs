using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Delivery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions, IInventoryBalanceRepository balances,
    IInventoryMovementService movement, TimeProvider clock) : IStorefrontFulfillmentService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private int Actor => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Cần đăng nhập.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Qty(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static Guid ChildKey(Guid key, int child) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString("N") + ":" + child))[..16]);
    private static void Need(bool value, string message) { if (!value) throw new ConflictAppException(message); }
    private static decimal Quantity(string text)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(text ?? "", "^(0|[1-9][0-9]{0,13})(\\.[0-9]{1,4})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value >= StorefrontValues.QuantityLimit)
            throw new ValidationAppException("Số lượng cần không âm, tối đa 4 số lẻ và trong giới hạn.");
        return value;
    }
    internal Task Guard(string permission, CancellationToken ct) => StorefrontStaffAuthorization.RequireAsync(db, user, permissions, permission, ct);
    internal static async Task Lock(AppDbContext db, string resource, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Storefront operations require SQL transaction.");
        await using var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.Transaction = tx.GetDbTransaction();
        cmd.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@name,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;";
        var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = resource; cmd.Parameters.Add(p);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) < 0) throw new StorefrontRetryableCommandException("Đơn đang được xử lý. Hãy thử lại cùng mã yêu cầu.");
    }
    private async Task<StorefrontOrder> Load(int id, CancellationToken ct) => await db.StorefrontOrders.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct)
        ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
    private async Task<bool> Incident(int id, CancellationToken ct) => await db.Set<StorefrontCustodyIncident>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == id && x.ReconciledByEvidenceId == null, ct);
    private async Task<List<StorefrontFulfillmentWork>> Works(int id, CancellationToken ct) => await db.Set<StorefrontFulfillmentWork>()
        .Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderBy(x => x.FulfillmentSourceId).ToListAsync(ct);
    private async Task<List<StorefrontFulfillmentSource>> Sources(int id, CancellationToken ct) => await db.Set<StorefrontFulfillmentSource>()
        .Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderBy(x => x.SourceWarehouseId).ThenBy(x => x.Id).ToListAsync(ct);
    internal async Task<StorefrontOperationAck> Command<T>(int id, T request, string operation, string permission,
        Func<StorefrontOrder, CancellationToken, Task> change, CancellationToken ct, Func<CancellationToken, Task>? prelock = null,
        Func<CancellationToken, Task>? additionalAuthorize = null) where T : StorefrontOperationRequest
    {
        await Guard(permission, ct);
        return await StorefrontOrderCommand.RunAsync(db, Store, id, request, operation, Actor,
            new { id, operation, actor = Actor, request }, async token =>
            {
                await Guard(permission, token);
                if (additionalAuthorize is not null) await additionalAuthorize(token);
            }, async token =>
            {
                if (prelock is not null) await prelock(token);
                await LockQrEvidence(id, token);
            }, async (order, token) =>
            {
                if (operation is "StartPreparation" or "RecordPicking" or "Dispatch" or "PostStorefrontSale")
                    await RequireCurrentPaymentProof(order, token);
                await change(order, token);
            }, clock, ct);
    }
    private StorefrontFulfillmentEvidence Evidence(StorefrontOrder order, StorefrontOperationRequest request, string kind, object snapshot, int? sourceId = null)
    {
        var evidence = new StorefrontFulfillmentEvidence { StoreId = Store, StorefrontOrderId = order.Id, FulfillmentSourceId = sourceId,
            ActorUserId = Actor, ClientRequestId = request.ClientRequestId, Kind = kind, OccurredAtUtc = Now,
            Note = request.Note?.Trim() ?? "", SnapshotJson = Serialize(snapshot) };
        db.Add(evidence); return evidence;
    }
    private static bool PaymentReady(StorefrontOrder order) => order.PaymentState != StorefrontPaymentState.Review &&
        (order.PaymentMethod == StorefrontPaymentMethod.Cod || order.PaymentState == StorefrontPaymentState.Confirmed);
}
