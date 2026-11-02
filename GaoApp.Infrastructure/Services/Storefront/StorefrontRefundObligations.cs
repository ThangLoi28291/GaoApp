using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal sealed record StorefrontRefundSourceSnapshot(int? GlobalReceiptId, int? CodReceiptId, string SourceFingerprint,
    string? OriginalAccountIdentity, string? OriginalAccountVersion, bool OriginalBankTimeUnknown, DateTime TrustedObservedAtUtc,
    decimal Amount, int? ReceivingLegalEntityId, string? BindingSnapshotJson, string? OriginalCodSnapshotJson);

// Creates liabilities, never money. Shared by cancellation, actual returns and late bank allocation.
internal static class StorefrontRefundObligations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Source(int? Global, int? Cod, int? Owner, decimal Amount, string Snapshot, bool Allocated);
    internal static async Task EnsureAsync(AppDbContext db, StorefrontOrder order, TimeProvider clock, CancellationToken ct)
    {
        if (order.Phase != StorefrontOrderPhase.Cancelled) return;
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Refund obligations require the owning order transaction.");
        var sources = new List<Source>();
        var intents = await db.Set<StorefrontPaymentIntent>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).Take(2).ToListAsync(ct);
        if (intents.Count > 1) throw new ConflictAppException("Đơn có nhiều nguồn QR chưa đối soát; không tự tạo ngân sách hoàn.");
        var bank = await StorefrontFulfillmentService.BankReceiptsForOrder(db, order.StoreId, order.Id).IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct &&
                (x.Status == "PAID" || x.Status == "SUCCESS" || x.Status == "COMPLETED" || x.Status == "APPROVED"))
            .OrderBy(x => x.Id).Take(201).ToListAsync(ct);
        if (bank.Count > 200) throw new ConflictAppException("Có quá nhiều khoản nhận; cần đối soát nguồn trước khi tạo yêu cầu hoàn.");
        foreach (var receipt in bank)
        {
            var intent = intents.SingleOrDefault(x => string.Equals(x.ProviderReference, receipt.ProviderReference, StringComparison.Ordinal));
            var binding = intent is null ? null : JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson, Json);
            if (binding is null || receipt.Channel != BankPaymentChannel.Storefront || receipt.AccountIdentity != binding.AccountIdentity || receipt.BankAccountId != binding.BankAccountId) continue;
            var allocated = await db.Set<StorefrontPaymentAllocation>().AsNoTracking().AnyAsync(a => a.StoreId == order.StoreId && a.StorefrontOrderId == order.Id &&
                a.PaymentIntentId == intent!.Id && a.Amount == receipt.Amount && db.Set<StorefrontBankReceipt>().Any(r => r.StoreId == order.StoreId && r.Id == a.BankReceiptId && r.GlobalBankReceiptId == receipt.Id), ct);
            var snapshot = new StorefrontRefundSourceSnapshot(receipt.Id, null, receipt.Fingerprint, receipt.AccountIdentity, receipt.AccountVersion,
                receipt.TimePrecision == BankTimePrecision.Unknown, DateTime.SpecifyKind(receipt.VerifiedAtUtc, DateTimeKind.Utc), receipt.Amount,
                binding.ReceivingLegalEntityId, intent!.BindingSnapshotJson, null);
            sources.Add(new(receipt.Id, null, binding.ReceivingLegalEntityId, receipt.Amount, JsonSerializer.Serialize(snapshot, Json), allocated && receipt.Amount == order.TotalDue));
        }
        var cod = await db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id)
            .OrderBy(x => x.Id).Take(201).ToListAsync(ct);
        if (cod.Count > 200) throw new ConflictAppException("Có quá nhiều chứng từ COD; cần đối soát nguồn.");
        foreach (var receipt in cod)
        {
            // A bank receipt reached through COD and QR remains the same underlying source.
            if (receipt.GlobalBankReceiptId is int global && sources.Any(x => x.Global == global)) continue;
            int? owner = null; DateTime observed = receipt.CreatedAtUtc; string? identity = null; string? version = null; var fingerprint = "COD-" + receipt.Id; var unknownTime = false;
            if (receipt.GlobalBankReceiptId is int globalId)
            {
                var row = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == globalId && x.OriginStoreId == order.StoreId, ct);
                var route = await db.Set<StorefrontCodBankRouting>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == order.StoreId && x.Id == receipt.CodBankRoutingId && x.GlobalBankReceiptId == globalId && x.StorefrontOrderId == order.Id, ct);
                if (row is null || route is null || row.AssetConfidence != BankReceiptAssetConfidence.CertifiedDistinct) continue;
                owner = route.ReceivingLegalEntityId; observed = row.VerifiedAtUtc; identity = row.AccountIdentity; version = row.AccountVersion; fingerprint = row.Fingerprint;
                unknownTime = row.TimePrecision == BankTimePrecision.Unknown;
            }
            // Legacy COD cash has no sealed receiving owner. Keep its liability visible,
            // with null owner and a blocked proof; never infer it from a warehouse/shift.
            var snapshot = new StorefrontRefundSourceSnapshot(receipt.GlobalBankReceiptId, receipt.Id, fingerprint, identity, version,
                unknownTime, DateTime.SpecifyKind(observed, DateTimeKind.Utc), receipt.Amount, owner, null, receipt.SourceSnapshotJson);
            sources.Add(new(receipt.GlobalBankReceiptId, receipt.Id, owner, receipt.Amount, JsonSerializer.Serialize(snapshot, Json), false));
        }
        foreach (var source in sources)
        {
            StorefrontValues.Money(source.Amount);
            if (source.Amount <= 0) continue;
            var existing = await db.Set<StorefrontRefundFunding>().IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                source.Global.HasValue ? x.GlobalBankReceiptId == source.Global : x.StoreId == order.StoreId && x.CodReceiptId == source.Cod, ct);
            if (existing is not null)
            {
                if (existing.StoreId != order.StoreId || existing.StorefrontOrderId != order.Id || existing.Amount != source.Amount)
                    throw new ConflictAppException("Khoản tiền đã có nghĩa vụ hoàn thuộc nguồn khác; cần đối soát.");
                continue;
            }
            var legacyReason = source.Global.HasValue ? "LATE_BANK_RECEIPT:" + source.Global.Value.ToString(CultureInfo.InvariantCulture) : null;
            var legacy = legacyReason is null ? new List<StorefrontRefund>() : await db.Set<StorefrontRefund>().IgnoreQueryFilters()
                .Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.Reason == legacyReason).Take(2).ToListAsync(ct);
            if (legacy.Count > 1) throw new ConflictAppException("Có nhiều yêu cầu cũ cho cùng khoản nhận; cần đối soát.");
            StorefrontRefund refund;
            if (legacy.Count == 1)
            {
                refund = legacy[0];
                if (refund.IsDeleted || refund.State is StorefrontRefundState.Executed || refund.Amount != source.Amount ||
                    refund.UnallocatedReceiptAmount != source.Amount || refund.GoodsAmount != 0 || refund.ShippingAmount != 0 ||
                    await db.Set<StorefrontRefundEvidence>().AnyAsync(x => x.StoreId == order.StoreId && x.RefundId == refund.Id, ct))
                    throw new ConflictAppException("Yêu cầu hoàn cũ thiếu chứng cứ nguồn thực trả; không tự ghi đè lịch sử.");
            }
            else
            {
                refund = new StorefrontRefund { StoreId = order.StoreId, StorefrontOrderId = order.Id, State = StorefrontRefundState.Requested,
                    Reason = source.Global.HasValue ? "CANCELLED_BANK_RECEIPT:" + source.Global : "CANCELLED_COD_RECEIPT:" + source.Cod,
                    Amount = source.Amount, GoodsAmount = source.Allocated ? order.GoodsAmount : 0,
                    ShippingAmount = source.Allocated ? order.ShippingCharge : 0, UnallocatedReceiptAmount = source.Allocated ? 0 : source.Amount };
                db.Add(refund); await db.SaveChangesAsync(ct);
            }
            db.Add(new StorefrontRefundFunding { StoreId = order.StoreId, StorefrontOrderId = order.Id, RefundId = refund.Id,
                Kind = source.Cod.HasValue ? StorefrontRefundSourceKind.CodReceipt : StorefrontRefundSourceKind.QrBankReceipt,
                GlobalBankReceiptId = source.Global, CodReceiptId = source.Cod, ReceivingLegalEntityId = source.Owner,
                Amount = source.Amount, SourceSnapshotJson = source.Snapshot });
            await db.SaveChangesAsync(ct);
        }
        await RefreshStateAsync(db, order, ct);
    }
    internal static async Task RefreshStateAsync(AppDbContext db, StorefrontOrder order, CancellationToken ct)
    {
        var rows = await db.Set<StorefrontRefund>().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).ToListAsync(ct);
        if (rows.Count == 0) return;
        var executed = await db.Set<StorefrontRefundEvidence>().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.FundingId != null && x.PayoutSourceKind != null).SumAsync(x => x.Amount, ct);
        var total = rows.Sum(x => x.Amount);
        var customerCod = await db.Set<StorefrontCodCollection>().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        order.RefundState = executed == total && executed >= customerCod ? StorefrontRefundState.Executed :
            rows.Any(x => x.State == StorefrontRefundState.Approved) && rows.All(x => x.State is StorefrontRefundState.Approved or StorefrontRefundState.Executed)
                ? StorefrontRefundState.Approved : StorefrontRefundState.Requested;
    }
    internal static async Task<IReadOnlyList<StorefrontGuestRefund>> ReadGuestAsync(AppDbContext db, StorefrontOrder order, CancellationToken ct)
    {
        var rows = await db.Set<StorefrontRefund>().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).OrderBy(x => x.Id).Take(200).ToListAsync(ct);
        var paid = await db.Set<StorefrontRefundEvidence>().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.FundingId != null && x.PayoutSourceKind != null)
            .GroupBy(x => x.RefundId).Select(x => new { Id = x.Key, Amount = x.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        return rows.Select(x => new StorefrontGuestRefund(x.Id, x.State == StorefrontRefundState.Executed && paid.GetValueOrDefault(x.Id) < x.Amount ? StorefrontRefundState.Requested : x.State,
            x.Amount, paid.GetValueOrDefault(x.Id), x.State switch
        {
            StorefrontRefundState.Executed when paid.GetValueOrDefault(x.Id) >= x.Amount => "Đã xác nhận thực hoàn",
            StorefrontRefundState.Executed => "Cần đối soát chứng cứ hoàn cũ", StorefrontRefundState.Approved => "Đã duyệt, còn chờ thực hoàn",
            StorefrontRefundState.Rejected => "Cần đối soát lại", _ => "Đang xem xét khoản cần hoàn"
        }, x.State == StorefrontRefundState.Rejected ? "Quyết định trước chưa chấp thuận; khoản còn phải xử lý vẫn được giữ để đối soát, không coi là đã hoàn." :
            "Chỉ xác nhận đã hoàn sau khi có chứng cứ thực trả. Việc hủy đơn không tự chuyển tiền." )).ToList();
    }
}
