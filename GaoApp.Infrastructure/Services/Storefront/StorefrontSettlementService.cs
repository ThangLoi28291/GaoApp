using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontSettlementService(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions,
    IInventoryBalanceRepository balances, IInventoryMovementService movement, IStorefrontSaleBridge sale,
    IStorefrontInvoiceProcessor invoices, TimeProvider clock) : IStorefrontSettlementService
{
    private readonly StorefrontFulfillmentService operations = new(db, user, permissions, balances, movement, clock);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private int Actor => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Cần đăng nhập.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static void Need(bool value, string message) { if (!value) throw new ConflictAppException(message); }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public async Task<StorefrontSettlementSummary> GetAsync(int orderId, CancellationToken ct = default)
    {
        await operations.Guard(StorefrontPermissions.ViewOrders, ct);
        var order = await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == orderId, ct) ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
        return await Summary(order, ct);
    }
    private async Task<StorefrontSettlementSummary> Summary(StorefrontOrder order, CancellationToken ct)
    {
        var qr = order.PaymentMethod == StorefrontPaymentMethod.Qr;
        var paid = qr ? await db.Set<StorefrontPaymentAllocation>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct)
            : await db.Set<StorefrontCodCollection>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        var received = qr ? await StorefrontFulfillmentService.BankReceiptsForOrder(db, Store, order.Id).Where(x => x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct &&
            (x.Status == "PAID" || x.Status == "SUCCESS" || x.Status == "COMPLETED" || x.Status == "APPROVED")).SumAsync(x => x.Amount, ct)
            : await db.Set<StorefrontCodReceipt>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        var withheld = await db.Set<StorefrontCarrierWithholding>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        var posted = await db.Set<StorefrontSaleBinding>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).Select(x => (int?)x.SaleOrderId).SingleOrDefaultAsync(ct);
        string? reason = posted.HasValue ? "Đã ghi nhận bán." : order.Phase != StorefrontOrderPhase.Delivered ? "Chỉ ghi bán sau khi mọi nguồn giao thành công." :
            order.PaymentState == StorefrontPaymentState.Review ? "Khoản tiền đang cần đối soát." : paid != order.TotalDue ? "Chưa có đủ bằng chứng khách đã trả toàn bộ." :
            received + withheld != order.TotalDue ? "Tiền thực nhận và khoản giữ lại có chứng từ chưa khớp số phải thu." :
            await db.Set<StorefrontCustodyIncident>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == order.Id && x.ReconciledByEvidenceId == null, ct) ? "Còn sự cố hàng thực tế chưa đối soát." : null;
        if (reason is null)
        {
            var cancellation = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, ct);
            if (cancellation.PendingRequest is not null || cancellation.ReturnAuthorised || cancellation.NeedsDeliveryReview)
                reason = "Yêu cầu hủy, thu hồi hoặc xung đột giao / hủy còn chờ xử lý; chưa được ghi bán.";
        }
        StorefrontServiceCollectionProof? service = null;
        if (reason is null && order.ShippingCharge > 0)
        {
            try { service = await ServiceProof(order, ct); } catch (ConflictAppException ex) { reason = ex.Message; }
        }
        if (reason is null && !qr && withheld > 0)
        {
            var deductions = await db.Set<StorefrontCarrierWithholding>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).ToListAsync(ct);
            foreach (var owner in deductions.GroupBy(x => x.LegalEntityId))
            {
                var goods = await db.DeliveryOrders.Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id && x.SourceLegalEntityId == owner.Key).SumAsync(x => x.QuotedTotal, ct);
                if (owner.Sum(x => x.Amount) > goods + (service?.LegalEntityId == owner.Key ? order.ShippingCharge : 0))
                    reason = "Chứng từ giữ lại không khớp ngân sách chính chủ thể hàng/dịch vụ; cần đối soát trước khi ghi bán.";
            }
        }
        return new(paid, received, withheld, Math.Max(0, paid - received - withheld), reason is null, reason, posted);
    }
    public Task<StorefrontOperationAck> RecordCustomerCollectionAsync(int id, StorefrontCodCollectionRequest request, CancellationToken ct = default)
        => operations.Command(id, request, "RecordCodCustomerCollection", StorefrontPermissions.Reconcile, async (order, token) =>
        {
            Need(order.PaymentMethod == StorefrontPaymentMethod.Cod && order.Phase is StorefrontOrderPhase.Shipping or StorefrontOrderPhase.Delivered, "Chỉ ghi khách trả COD cho hàng đã giao đi.");
            StorefrontValues.Money(request.Amount); Need(request.Amount > 0 && !string.IsNullOrWhiteSpace(request.EvidenceReference) && request.EvidenceReference.Length <= 500,
                "Cần số tiền thực thu và chứng cứ khách trả.");
            Need(request.CustomerPaidAt != default && request.CustomerPaidAt.UtcDateTime >= order.CreatedAtUtc && request.CustomerPaidAt.UtcDateTime <= Now.AddMinutes(2), "Thời điểm khách trả không hợp lệ.");
            var work = await db.Set<StorefrontFulfillmentWork>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.StorefrontOrderId == id && x.FulfillmentSourceId == request.FulfillmentSourceId, token);
            Need(work?.Stage == StorefrontFulfillmentStage.Delivered, "Nguồn thu cần có bằng chứng giao thành công và bên giao thực tế.");
            var paid = await db.Set<StorefrontCodCollection>().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).SumAsync(x => x.Amount, token);
            Need(request.Amount <= order.TotalDue - paid, "Số khách trả vượt ngân sách phải thu còn lại.");
            db.Add(new StorefrontCodCollection { StoreId = Store, StorefrontOrderId = id, FulfillmentSourceId = request.FulfillmentSourceId,
                RecordedByUserId = Actor, Amount = request.Amount, CustomerPaidAtUtc = request.CustomerPaidAt.UtcDateTime, EvidenceReference = request.EvidenceReference.Trim() });
            if (paid + request.Amount == order.TotalDue && order.PaymentState != StorefrontPaymentState.Review) order.PaymentState = StorefrontPaymentState.Confirmed;
        }, ct);

    public Task<StorefrontOperationAck> ReceiveBankRemittanceAsync(int id, StorefrontCodBankReceiptRequest request, CancellationToken ct = default)
        => operations.Command(id, request, "ReceiveCodBankRemittance", StorefrontPermissions.Reconcile, async (order, token) =>
        {
            await CodOpen(order, token);
            Need(!string.IsNullOrWhiteSpace(request.EvidenceReference) && request.EvidenceReference.Length <= 500, "Cần chứng cứ đối soát hãng giao/nhân viên nộp tiền.");
            var receipt = await db.Set<BankVerifiedReceipt>().SingleOrDefaultAsync(x => x.OriginStoreId == Store && x.Id == request.GlobalBankReceiptId, token)
                ?? throw new NotFoundAppException("Không tìm thấy khoản ngân hàng của cửa hàng.");
            await db.Entry(receipt).ReloadAsync(token);
            Need(receipt.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && receipt.Channel == BankPaymentChannel.Unknown &&
                receipt.RoutingProvenance == BankReceiptRoutingProvenance.UnmatchedOnly && receipt.NativeCollectionAuthorityProofJson is not null &&
                receipt.NeedsReview && receipt.ReviewCode == "UNMATCHED_CHANNEL_BINDING" && receipt.TimePrecision != BankTimePrecision.Unknown && receipt.OccurredAtUtc.HasValue &&
                new[] { "PAID", "SUCCESS", "COMPLETED", "APPROVED" }.Contains(receipt.Status), "Khoản này chưa có nguồn thu độc lập hợp lệ hoặc đã thuộc POS/QR; không thể chuyển cho COD.");
            Need(!await db.Set<BankReceiptConflict>().AnyAsync(x => x.BankVerifiedReceiptId == receipt.Id, token) &&
                !await db.Set<BankReceiptConsumption>().AnyAsync(x => x.BankVerifiedReceiptId == receipt.Id, token) &&
                !await db.Set<StorefrontCodBankRouting>().AnyAsync(x => x.GlobalBankReceiptId == receipt.Id, token), "Khoản ngân hàng đã có sử dụng/đối soát mâu thuẫn.");
            Need(!await db.Set<BankPaymentReference>().AnyAsync(x => x.Provider == receipt.Provider && x.Reference == receipt.ProviderReference, token), "Nguồn gốc có mã thanh toán POS/QR; không được chuyển sang COD dù chưa dùng.");
            var account = await db.StoreBankAccounts.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == receipt.BankAccountId && x.IsActive, token);
            Need(account is not null && GlobalBankReceiptLedger.AccountIdentity(account.BankCode, account.AccountNumber) == receipt.AccountIdentity && Convert.ToBase64String(account.RowVersion) == receipt.AccountVersion, "Tài khoản hiện tại không còn khớp bằng chứng gốc.");
            Need(receipt.TimePrecision == BankTimePrecision.BankDay ? receipt.OccurredAtUtc!.Value.AddDays(1) > order.CreatedAtUtc : receipt.OccurredAtUtc >= order.CreatedAtUtc.AddMinutes(-2), "Khoản ngân hàng có thời điểm trước nguồn đơn; cần đối soát riêng.");
            var owners = await ActualOwners(order, token);
            var authorities = await db.Set<StorefrontBankCollectionAuthority>().AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.BankAccountId == receipt.BankAccountId &&
                x.ReceivingLegalEntityId == request.ReceivingLegalEntityId && owners.Contains(x.SourceLegalEntityId)).ToListAsync(token);
            var originalAuthorities = JsonSerializer.Deserialize<List<BankCollectionAuthorityProof>>(receipt.NativeCollectionAuthorityProofJson!) ?? [];
            Need(authorities.All(x => originalAuthorities.Any(p => p.Id == x.Id && p.BankAccountId == x.BankAccountId && p.ReceivingLegalEntityId == x.ReceivingLegalEntityId && p.SourceLegalEntityId == x.SourceLegalEntityId && p.Version == Convert.ToBase64String(x.RowVersion))), "Quyền thu phải có từ lúc tiếp nhận tiền, còn nguyên phiên bản; không thể bổ sung sau để hợp thức hóa khoản cũ.");
            Need(receipt.IdentityKind != BankExternalIdentityKind.BankNotification || await db.Set<StorefrontNotificationCertification>().AnyAsync(x => x.StoreId == Store && x.GlobalBankReceiptId == receipt.Id, token), "Thông báo chưa có liên kết chứng nhận nguồn tiếp nhận.");
            Need(authorities.Select(x => x.SourceLegalEntityId).Distinct().Count() == owners.Count && await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == request.ReceivingLegalEntityId && x.IsActive, token), "Thiếu quyền thu tiền cho chủ thể hàng/phí dịch vụ thực tế.");
            await CheckRemaining(order, receipt.Amount, token);
            var routing = new StorefrontCodBankRouting { StoreId = Store, StorefrontOrderId = id, GlobalBankReceiptId = receipt.Id, ReceivingLegalEntityId = request.ReceivingLegalEntityId,
                ActorUserId = Actor, EvidenceReference = request.EvidenceReference.Trim(), AuthoritySnapshotJson = Serialize(new { receipt.AccountIdentity, owners, authorities = authorities.Select(x => new { x.Id, x.SourceLegalEntityId, version = Convert.ToBase64String(x.RowVersion) }) }) };
            db.Add(routing); await db.SaveChangesAsync(token);
            db.Add(new StorefrontCodReceipt { StoreId = Store, StorefrontOrderId = id, GlobalBankReceiptId = receipt.Id, CodBankRoutingId = routing.Id,
                ActorUserId = Actor, Amount = receipt.Amount, SourceSnapshotJson = Serialize(new { receipt.Id, receipt.AccountIdentity, receipt.ExternalTransactionId, receipt.Fingerprint, routingId = routing.Id }) });
        }, ct, async token =>
        {
            await operations.LockQrEvidence(id, token, request.GlobalBankReceiptId);
        });

    public Task<StorefrontOperationAck> ReceiveCashRemittanceAsync(int id, StorefrontCodCashReceiptRequest request, CancellationToken ct = default)
        => operations.Command(id, request, "ReceiveCodCashRemittance", StorefrontPermissions.Reconcile, async (order, token) =>
        {
            await CodOpen(order, token);
            var cashId = request.POSShiftCashTransactionId;
            TreasuryEntry? treasury = null;
            if (request.TreasuryEntryId.HasValue)
            {
                treasury = await db.TreasuryEntries.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.TreasuryEntryId && x.Fund == "cash" &&
                    x.ReversalOfId == null && x.ReversedAtUtc == null && x.TargetFund == null && x.Amount > 0 && x.POSShiftCashTransactionId != null && x.IsVoucherLink, token);
                Need(treasury is not null && (cashId is null || cashId == treasury.POSShiftCashTransactionId), "Phiếu quỹ phải gắn đúng phiếu thực thu tiền mặt tại quầy; phiếu ngân hàng không phải bằng chứng tiền COD.");
                cashId = treasury!.POSShiftCashTransactionId;
            }
            var cash = await db.POSShiftCashTransactions.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == cashId && x.Type == POSShiftCashTransactionType.CashIn &&
                x.Amount > 0 && x.CustomerDebtReceiptId == null && x.CustomerDepositEntryId == null, token);
            Need(cash is not null && !await db.Set<POSCashAdjustmentRequest>().AnyAsync(x => x.StoreId == Store && x.TransactionId == cashId && x.Status == POSCashAdjustmentStatus.Pending, token), "Thiếu phiếu thực nộp két hoặc phiếu đang có yêu cầu điều chỉnh.");
            var linked = await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == Store && x.POSShiftCashTransactionId == cashId).ToListAsync(token);
            Need(linked.Count <= 1 && linked.All(x => !x.IsDeleted && x.IsVoucherLink && x.Fund == "cash" && x.TargetFund == null && x.ReversalOfId == null && x.ReversedAtUtc == null && x.Amount == cash!.Amount && x.OperatingExpenseId == null && x.PurchasePayableId == null), "Phiếu này có chuyển quỹ, đảo phiếu hoặc thanh toán khác; không phải tiền COD nộp từ bên giao.");
            treasury ??= linked.SingleOrDefault();
            Need(!await db.Set<StorefrontCodReceipt>().AnyAsync(x => x.StoreId == Store && x.POSShiftCashTransactionId == cashId, token), "Phiếu tiền mặt đã được đối soát cho đơn khác.");
            await CheckRemaining(order, cash!.Amount, token);
            db.Add(new StorefrontCodReceipt { StoreId = Store, StorefrontOrderId = id, TreasuryEntryId = treasury?.Id, POSShiftCashTransactionId = cash.Id,
                ActorUserId = Actor, Amount = cash.Amount, SourceSnapshotJson = Serialize(new { cash.Id, cash.POSShiftId, cash.Amount, cash.CreatedAtUtc, treasuryId = treasury?.Id }) });
        }, ct, async token =>
        {
            var cashId = request.POSShiftCashTransactionId;
            if (request.TreasuryEntryId is int treasuryId)
                cashId = await db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == Store && x.Id == treasuryId).Select(x => x.POSShiftCashTransactionId).SingleOrDefaultAsync(token);
            await operations.LockQrEvidence(id, token, incomingCashId: cashId);
        });

    public Task<StorefrontOperationAck> RecordCarrierWithholdingAsync(int id, StorefrontCarrierWithholdingRequest request, CancellationToken ct = default)
        => operations.Command(id, request, "RecordCarrierWithholding", StorefrontPermissions.Reconcile, async (order, token) =>
        {
            await CodOpen(order, token);
            Need(!string.IsNullOrWhiteSpace(request.CarrierEvidenceReference) && request.CarrierEvidenceReference.Length <= 500 && !string.IsNullOrWhiteSpace(request.Note), "Cần chứng từ hãng giao giữ lại và lý do.");
            var expense = await db.OperatingExpenses.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.OperatingExpenseId, token) ?? throw new NotFoundAppException("Không tìm thấy chi phí giao thực tế.");
            Need(expense.Status == "confirmed" && !expense.IsPaid && expense.Amount > 0 &&
                !await db.TreasuryEntries.AnyAsync(x => x.StoreId == Store && x.OperatingExpenseId == expense.Id, token) &&
                !await db.Set<StorefrontCarrierWithholding>().AnyAsync(x => x.StoreId == Store && x.OperatingExpenseId == expense.Id, token), "Chi phí phải đã ghi nhận, chưa trả/quyết toán, không trùng chứng từ.");
            Need((await ActualOwners(order, token)).Contains(request.LegalEntityId), "Chủ thể chi phí phải là chủ thể hàng/phí thực tế của đơn.");
            var goodsBudget = await db.Set<GaoApp.Domain.Delivery.DeliveryOrder>().Where(x => x.StoreId == Store && x.StorefrontOrderId == id && x.SourceLegalEntityId == request.LegalEntityId).SumAsync(x => x.QuotedTotal, token);
            var serviceBudget = order.ShippingCharge > 0 && (await ServiceProof(order, token)).LegalEntityId == request.LegalEntityId ? order.ShippingCharge : 0;
            var ownerUsed = await db.Set<StorefrontCarrierWithholding>().Where(x => x.StoreId == Store && x.StorefrontOrderId == id && x.LegalEntityId == request.LegalEntityId).SumAsync(x => x.Amount, token);
            Need(ownerUsed + expense.Amount <= goodsBudget + serviceBudget, "Khoản giữ lại vượt tiền hàng/phí thuộc chính chủ thể chi phí; cần đối soát nguồn khác, không lấy tiền của chủ thể khác.");
            StorefrontValues.Money(expense.Amount); await CheckRemaining(order, expense.Amount, token);
            db.Add(new StorefrontCarrierWithholding { StoreId = Store, StorefrontOrderId = id, OperatingExpenseId = expense.Id, LegalEntityId = request.LegalEntityId,
                Amount = expense.Amount, ActorUserId = Actor, CarrierEvidenceReference = request.CarrierEvidenceReference.Trim() });
            await db.SaveChangesAsync(token);
            expense.IsPaid = true; expense.PaymentMethod = "carrier-withholding"; expense.ReceiptReference = request.CarrierEvidenceReference.Trim();
        }, ct, token => operations.LockQrEvidence(id, token, incomingExpenseId: request.OperatingExpenseId));

    public async Task<StorefrontOperationAck> PostSaleAsync(int id, StorefrontOperationRequest request, CancellationToken ct = default)
    {
        var result = await operations.Command(id, request, "PostStorefrontSale", StorefrontPermissions.PostSale, async (order, token) =>
        {
            var status = await Summary(order, token); Need(status.CanPostSale, status.BlockReason ?? "Đơn chưa sẵn sàng ghi nhận bán.");
            _ = await sale.PostAsync(id, request.ClientRequestId, token);
            order.Phase = StorefrontOrderPhase.Completed;
        }, ct);
        // Durable work was inserted in the sale transaction. A failed after-commit attempt never reverses/repeats the sale.
        try { await invoices.ProcessAsync(id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            // The sale and durable invoice work have committed. Recovery owns this failure;
            // never turn a successfully committed command into a second financial attempt.
        }
        return result;
    }
    private async Task CodOpen(StorefrontOrder order, CancellationToken ct)
    {
        Need(order.PaymentMethod == StorefrontPaymentMethod.Cod && order.Phase == StorefrontOrderPhase.Delivered && order.PaymentState != StorefrontPaymentState.Review &&
            !await db.Set<StorefrontSaleBinding>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == order.Id, ct), "Đối soát COD cần đơn giao thành công, chưa ghi bán và không có tiền mâu thuẫn.");
    }
    private async Task CheckRemaining(StorefrontOrder order, decimal amount, CancellationToken ct)
    {
        var received = await db.Set<StorefrontCodReceipt>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        var withheld = await db.Set<StorefrontCarrierWithholding>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).SumAsync(x => x.Amount, ct);
        Need(amount > 0 && amount <= order.TotalDue - received - withheld, "Chứng từ vượt khoản cần đối soát còn lại; không dùng một chứng từ cho nhiều đơn.");
    }
    private async Task<List<int>> ActualOwners(StorefrontOrder order, CancellationToken ct)
    {
        var owners = await db.Set<StorefrontFulfillmentSource>().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).Select(x => x.SourceLegalEntityId).Distinct().ToListAsync(ct);
        if (order.ShippingCharge > 0) owners.Add((await ServiceProof(order, ct)).LegalEntityId);
        return owners.Distinct().Order().ToList();
    }
    internal Task<StorefrontServiceCollectionProof> ServiceProof(StorefrontOrder order, CancellationToken ct) => StorefrontServiceSource.ReadAsync(db, order, ct);
    public async Task<StorefrontServiceRevenueSettingDto?> GetServiceSettingAsync(CancellationToken ct = default)
    {
        await SettingsGuard(StorefrontPermissions.ViewSettings, ct); var x = await db.Set<StorefrontServiceRevenueSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
        return x is null ? null : new(Convert.ToBase64String(x.RowVersion), x.LegalEntityId, x.TaxId, x.Name, x.InvoiceRoute);
    }
    public async Task<StorefrontServiceRevenueSettingDto> SaveServiceSettingAsync(StorefrontServiceRevenueSettingRequest request, CancellationToken ct = default)
    {
        await SettingsGuard(StorefrontPermissions.ManageSettings, ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.InvoiceRoute is not (InvoiceIssuanceRoute.Automatic or InvoiceIssuanceRoute.Manual))
            throw new ValidationAppException("Tên dịch vụ hoặc tuyến hóa đơn không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await StorefrontFulfillmentService.Lock(db, "GaoApp:Storefront:Configuration:" + Store, ct);
        Need(await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == request.LegalEntityId && x.IsActive, ct) &&
            await db.Taxes.AnyAsync(x => x.StoreId == Store && x.Id == request.TaxId && x.IsActive, ct), "Chọn chủ thể và thuế đang hoạt động trong cửa hàng.");
        var row = await db.Set<StorefrontServiceRevenueSetting>().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
        if (row is null) { Need(string.IsNullOrEmpty(request.Version), "Cấu hình đã thay đổi."); row = new() { StoreId = Store }; db.Add(row); }
        else Need(Convert.ToBase64String(row.RowVersion) == request.Version, "Cấu hình đã thay đổi; tải lại.");
        row.Name = request.Name.Trim(); row.LegalEntityId = request.LegalEntityId; row.TaxId = request.TaxId; row.InvoiceRoute = request.InvoiceRoute;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(Convert.ToBase64String(row.RowVersion), row.LegalEntityId, row.TaxId, row.Name, row.InvoiceRoute);
    }
    private async Task SettingsGuard(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !await db.UserInStores.AnyAsync(x => x.StoreId == Store && x.UserId == Actor && x.IsActive && x.User.IsActive, ct) ||
            !await permissions.HasPermissionAsync(Store, Actor, permission, ct)) throw new ForbiddenAppException("Không có quyền cấu hình doanh thu dịch vụ website.");
    }
}
