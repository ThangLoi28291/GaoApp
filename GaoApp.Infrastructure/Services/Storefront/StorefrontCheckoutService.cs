using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService(AppDbContext db, IStorefrontCatalogService catalog,
    IStorefrontAddressCatalog addresses, IPromotionRepository promotionRepository, IPromotionEngine promotionEngine,
    IInventoryBalanceRepository balances, IOrderLegalEntityAllocationService allocation, IDataProtectionProvider protection,
    TimeProvider clock, IStorefrontBankRegistration? bankRegistration = null) : IStorefrontCheckoutService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Operation = "place-cod-v1";
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    // datetime2 stores UTC ticks without Kind. Mark these known UTC module timestamps; never shift them.
    internal static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(bool value, string message) { if (!value) throw new ValidationAppException(message); }
    private sealed record Source(int WarehouseId, int LegalEntityId, int Priority, string Version);
    private sealed record Data(StorefrontQuoteRequest Input, StorefrontRecipient Recipient, List<StorefrontCheckoutLine> Lines,
        decimal GoodsAmount, decimal ShippingCharge, decimal TotalDue, decimal? TotalWeightKg, int SettingsRevision,
        string SettingsVersion, int ShippingRateId, int RateRevision, StorefrontDeliveryMethod DeliveryMethod, string ZoneName,
        int MinDays, int MaxDays, string AddressFingerprint, bool AllowNegative, List<Source> Sources, bool HasShortage,
        StorefrontBankBinding? BankBinding = null, bool? UseMultiLegalEntity = null, DateTime? LegalEntityActivationAtUtcSnapshot = null);
    private sealed record ProtectedOutcome(string Protected);

    public async Task<StorefrontQuoteDto> QuoteAsync(StorefrontQuoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request); CleanTracking();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await Lock("GaoApp:Storefront:Configuration:" + Store, ct);
            var data = await Build(request, ct); var quote = await SaveQuote(data, ct);
            await tx.CommitAsync(ct); return Dto(quote, data);
        }
        catch (StorefrontRuleException ex) { throw new ValidationAppException(ex.Message); }
        catch (OverflowException) { throw new ValidationAppException("Số lượng, trọng lượng hoặc số tiền vượt giới hạn."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or -2) { throw new ConflictAppException("Dữ liệu đang được cập nhật. Hãy thử lại yêu cầu."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 or -2 })
        { throw new ConflictAppException("Dữ liệu đang được cập nhật. Hãy thử lại yêu cầu."); }
        finally { DetachCheckout(); }
    }

    public async Task<StorefrontPlacementResult> PlaceAsync(StorefrontPlaceOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request); CleanTracking();
        Require(request.ClientRequestId != Guid.Empty, "Yêu cầu đặt hàng thiếu mã yêu cầu.");
        var requestHash = Hash(Serialize(new { request.QuoteToken, Fingerprint = (request.Fingerprint ?? "").ToUpperInvariant() }));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await Lock($"GaoApp:Storefront:Checkout:{Store}:{request.ClientRequestId:N}", ct);
            var receipt = await db.Set<StorefrontCommandReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Operation == Operation && x.ClientRequestId == request.ClientRequestId, ct);
            if (receipt is not null)
            {
                if (receipt.RequestHash != requestHash) throw new ConflictAppException("Mã đặt hàng đã dùng cho nội dung khác. Hãy kiểm tra đơn đã tạo trước khi đặt tiếp.");
                try
                {
                    var envelope = JsonSerializer.Deserialize<ProtectedOutcome>(receipt.OutcomeJson, Json)!;
                    var result = JsonSerializer.Deserialize<StorefrontPlacedOrder>(Protector().Unprotect(envelope.Protected), Json)!;
                    if (result is null) throw new CryptographicException();
                    await tx.CommitAsync(ct); return new(StorefrontPlacementStatus.Placed, result, null, "Đã nhận lại đúng đơn được tạo trước đó.");
                }
                catch (Exception ex) when (ex is CryptographicException or JsonException or NullReferenceException)
                { throw new ConflictAppException("Chưa mở lại được liên kết riêng của đơn. Cửa hàng cần kiểm tra khóa bảo vệ; không đặt lại đơn mới."); }
            }
            Require(request.QuoteToken != Guid.Empty && Regex.IsMatch(request.Fingerprint ?? "", "^[0-9A-Fa-f]{64}$"), "Yêu cầu đặt hàng thiếu phiên bản báo giá hợp lệ.");
            await Lock("GaoApp:Storefront:Configuration:" + Store, ct);
            var quote = await db.StorefrontQuotes.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Token == request.QuoteToken, ct)
                ?? throw new NotFoundAppException("Không tìm thấy báo giá.");
            if (await db.StorefrontOrders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.QuoteId == quote.Id, ct))
                throw new ConflictAppException("Báo giá này đã tạo đơn bằng một mã yêu cầu khác. Hãy kiểm tra đơn đã nhận; không đặt thêm bản sao.");
            if (!string.Equals(quote.Fingerprint, request.Fingerprint, StringComparison.OrdinalIgnoreCase)) throw new ConflictAppException("Phiên bản báo giá không khớp. Hãy lấy lại báo giá.");
            var original = JsonSerializer.Deserialize<Data>(quote.SnapshotJson, Json) ?? throw new ConflictAppException("Báo giá không hợp lệ.");
            await LockPricingSources(original.Input, ct);
            var fresh = await Build(original.Input, ct);
            if (quote.ExpiresAtUtc <= Now || Fingerprint(fresh) != quote.Fingerprint)
            {
                var replacement = await SaveQuote(fresh, ct); await tx.CommitAsync(ct);
                return new(StorefrontPlacementStatus.QuoteChanged, null, Dto(replacement, fresh), "Giá, ưu đãi hoặc điều kiện giao đã thay đổi. Hãy xem và xác nhận báo giá mới; chưa có đơn được tạo.");
            }
            var locked = await LockBalances(fresh, ct);
            var lockedFresh = await Build(original.Input, ct);
            if (quote.ExpiresAtUtc <= Now || Fingerprint(lockedFresh) != quote.Fingerprint)
            {
                // Missing balance rows may have been created under U-locks. Roll everything back before a replacement quote.
                await tx.RollbackAsync(ct); await tx.DisposeAsync(); DetachCheckout();
                var replacement = await QuoteAsync(original.Input, ct);
                return new(StorefrontPlacementStatus.QuoteChanged, null, replacement, "Giá, ưu đãi hoặc điều kiện giao vừa thay đổi. Hãy xác nhận báo giá mới; chưa có đơn được tạo.");
            }
            fresh = lockedFresh;
            var shortage = Shortage(fresh.Lines, fresh.Sources, locked.Values);
            Require(fresh.AllowNegative || !shortage, "Không đủ tồn khả dụng để nhận đơn. Hãy giảm số lượng hoặc liên hệ cửa hàng.");
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var qr = fresh.Input.PaymentMethod == StorefrontPaymentMethod.Qr;
            var order = new StorefrontOrder { StoreId = Store, Code = "W" + Guid.NewGuid().ToString("N").ToUpperInvariant(), GuestTokenHash = Hash(token),
                UseMultiLegalEntitySnapshot = fresh.UseMultiLegalEntity ?? throw new ConflictAppException("Báo giá cũ thiếu chế độ nguồn pháp lý; hãy lấy và xác nhận báo giá mới."), LegalEntityModeCapturedAtUtc = Now,
                QuoteId = quote.Id, Phase = qr ? StorefrontOrderPhase.AwaitingPayment : StorefrontOrderPhase.AwaitingPreparation, PaymentMethod = fresh.Input.PaymentMethod,
                PaymentState = qr ? StorefrontPaymentState.Pending : StorefrontPaymentState.Unpaid, DeliveryState = StorefrontDeliveryState.NotPlanned, RefundState = StorefrontRefundState.None,
                DeliveryMethod = fresh.DeliveryMethod, RecipientName = fresh.Recipient.Name, RecipientPhone = fresh.Recipient.Phone,
                RecipientAddress = FullAddress(fresh.Recipient), Note = fresh.Recipient.Note, GoodsAmount = fresh.GoodsAmount,
                ShippingCharge = fresh.ShippingCharge, TotalDue = fresh.TotalDue, ShippingFeeTreatment = StorefrontShippingFeeTreatment.StoreServiceFee,
                AddressSnapshotJson = Serialize(new { fresh.Recipient, fresh.AllowNegative, HasInventoryShortage = shortage, fresh.AddressFingerprint, fresh.Sources, fresh.LegalEntityActivationAtUtcSnapshot }) };
            db.StorefrontOrders.Add(order); await db.SaveChangesAsync(ct);
            await Reserve(order, fresh, locked, ct);
            int? paymentIntentId = qr ? await CreateQrIntent(order, fresh, ct) : null;
            var outcome = new StorefrontPlacedOrder(order.Code, "/shop/orders#" + token, order.TotalDue, AsUtc(order.CreatedAtUtc),order.PaymentMethod);
            var eventId = Guid.NewGuid();
            var payload = Serialize(new { order.Id, order.Code, order.TotalDue, HasInventoryShortage = shortage,order.PaymentMethod,PaymentIntentId=paymentIntentId });
            db.Set<StorefrontEvent>().Add(new() { StoreId = Store, EventId = eventId, StorefrontOrderId = order.Id, Revision = 1,
                Kind = "OrderCreated", CausationId = request.ClientRequestId, PayloadJson = payload });
            db.Set<StorefrontOutboxMessage>().Add(new() { StoreId = Store, EventId = eventId, StorefrontOrderId = order.Id, Revision = 1,
                Kind = qr ? "QrIntentCreated" : "OrderCreated", PayloadJson = payload });
            db.Set<StorefrontCommandReceipt>().Add(new() { StoreId = Store, ClientRequestId = request.ClientRequestId, Operation = Operation,
                RequestHash = requestHash, StorefrontOrderId = order.Id, OutcomeJson = Serialize(new ProtectedOutcome(Protector().Protect(Serialize(outcome)))) });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(StorefrontPlacementStatus.Placed, outcome, null, qr ? "Đã nhận đơn chờ QR. Mở liên kết riêng để thanh toán; chưa coi là đã nhận tiền." : "Cửa hàng đã nhận đơn và đang chờ chuẩn bị. Thanh toán COD khi nhận hàng.");
        }
        catch (StorefrontRuleException ex) { throw new ValidationAppException(ex.Message); }
        catch (OverflowException) { throw new ValidationAppException("Số lượng, trọng lượng hoặc số tiền vượt giới hạn."); }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Dữ liệu vừa thay đổi. Hãy kiểm tra và thử lại đúng yêu cầu này."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or -2) { throw new ConflictAppException("Dữ liệu đang được cập nhật. Hãy thử lại cùng mã đặt hàng để tránh tạo trùng."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 or -2 })
        { throw new ConflictAppException("Dữ liệu đang được cập nhật. Hãy thử lại cùng mã đặt hàng để tránh tạo trùng."); }
        finally { DetachCheckout(); }
    }

    private IDataProtector Protector() => protection.CreateProtector("GaoApp.Storefront.Placement.v1", Store.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string FullAddress(StorefrontRecipient value) => $"{value.StreetAddress}, {value.WardName}, {value.ProvinceName}";
    private static string Fingerprint(Data data) => Hash(Serialize(new { data.Input, data.Recipient, data.Lines, data.GoodsAmount,
        data.ShippingCharge, data.TotalDue, data.TotalWeightKg, data.SettingsRevision, data.SettingsVersion, data.ShippingRateId,
        data.RateRevision, data.DeliveryMethod, data.ZoneName, data.MinDays, data.MaxDays, data.AddressFingerprint, data.AllowNegative, data.Sources, data.BankBinding, data.UseMultiLegalEntity, data.LegalEntityActivationAtUtcSnapshot }));
    private async Task<StorefrontQuote> SaveQuote(Data data, CancellationToken ct)
    {
        var quote = new StorefrontQuote { StoreId = Store, Token = Guid.NewGuid(), SettingsRevision = data.SettingsRevision,
            ShippingRateId = data.ShippingRateId, Fingerprint = Fingerprint(data), ExpiresAtUtc = Now.AddMinutes(10), GoodsAmount = data.GoodsAmount,
            ShippingCharge = data.ShippingCharge, TotalDue = data.TotalDue, SnapshotJson = Serialize(data) };
        db.StorefrontQuotes.Add(quote); await db.SaveChangesAsync(ct); return quote;
    }
    private static StorefrontQuoteDto Dto(StorefrontQuote quote, Data data) => new(quote.Token, quote.Fingerprint, AsUtc(quote.ExpiresAtUtc),
        data.Recipient, data.Lines, data.GoodsAmount, data.ShippingCharge, data.TotalDue, data.TotalWeightKg, data.DeliveryMethod,
        data.ZoneName, data.MinDays, data.MaxDays, data.HasShortage, data.Input.PaymentMethod);
    private async Task Lock(string resource, CancellationToken ct)
    {
        if (!db.CanWriteStoreData || db.CurrentStoreId != Store) throw new ForbiddenAppException("Không có phạm vi dữ liệu cửa hàng.");
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Checkout requires a transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = tx.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource; command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new ConflictAppException("Yêu cầu đang được xử lý. Hãy thử lại cùng mã đặt hàng.");
    }
    private void CleanTracking()
    {
        if (db.ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new ConflictAppException("Ngữ cảnh có thay đổi chưa lưu. Hãy gửi lại yêu cầu trong phiên mới.");
        DetachCheckout();
    }
    private void DetachCheckout()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.Entity is StorefrontOrder or StorefrontOrderLine or StorefrontQuote or
            StorefrontFulfillmentSource or StorefrontReservationLink or StorefrontCommandReceipt or StorefrontEvent or StorefrontOutboxMessage or InventoryBalance or InventoryReservation or StorefrontPaymentIntent or StorefrontQrProcessing or BankPaymentReference).ToList())
            entry.State = EntityState.Detached;
    }
}
