using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontRefundService : IStorefrontRefundService
{
    private readonly AppDbContext db;
    private readonly ICurrentUser user;
    private readonly ICurrentStorePermissionService permissions;
    private readonly AcbProtocolCore protocol;
    private readonly TimeProvider clock;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public StorefrontRefundService(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions,
        AcbProtocolCore protocol, TimeProvider clock)
    { this.db = db; this.user = user; this.permissions = permissions; this.protocol = protocol; this.clock = clock; }
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private int Actor => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Cần đăng nhập.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static void Need(bool valid, string message) { if (!valid) throw new ConflictAppException(message); }
    private Task Guard(string permission, CancellationToken ct) => StorefrontStaffAuthorization.RequireAsync(db, user, permissions, permission, ct);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static void Reason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ValidationAppException("Cần lý do rõ ràng, tối đa 1.000 ký tự.");
    }
    private async Task<StorefrontOrder> Order(int id, CancellationToken ct) => await db.StorefrontOrders.AsNoTracking()
        .SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
    private async Task<StorefrontRefund> Refund(int id, int refundId, CancellationToken ct) => await db.Set<StorefrontRefund>()
        .SingleOrDefaultAsync(x => x.StoreId == Store && x.StorefrontOrderId == id && x.Id == refundId, ct)
        ?? throw new NotFoundAppException("Khoản hoàn không thuộc đơn này.");
    private Task<StorefrontRefundFunding?> Funding(int id, int refundId, CancellationToken ct) => db.Set<StorefrontRefundFunding>()
        .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.StorefrontOrderId == id && x.RefundId == refundId, ct);
    private async Task<string?> PhysicalBlock(StorefrontOrder order, CancellationToken ct)
    {
        if (order.Phase != StorefrontOrderPhase.Cancelled) return "Chỉ xử lý hoàn theo luồng hủy khi đơn đã kết thúc hủy thực tế.";
        var facts = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, ct);
        if (facts.HasDeliveredGoods) return "Đã có hàng giao khách; trả hàng sau giao cần quy trình riêng.";
        if (facts.HasIncident || facts.PendingRequest is not null || facts.NeedsDeliveryReview) return "Còn yêu cầu hoặc sự cố hàng thực tế cần xử lý trước.";
        if (facts.HasDispatch && (!facts.CompleteDispatchProof || !facts.AllDispatchReturned)) return "Chưa có đủ chứng cứ nhận lại toàn bộ hàng đã xuất.";
        return null;
    }
    private Task<decimal> Executed(int id, int refundId, CancellationToken ct) => db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters()
        .Where(x => x.StoreId == Store && x.StorefrontOrderId == id && x.RefundId == refundId).SumAsync(x => x.Amount, ct);
    private async Task<StorefrontRefundSourceResult> Source(StorefrontOrder order, StorefrontRefundFunding? funding, CancellationToken ct)
        => funding is null ? new(false, "Nguồn hoàn cũ chưa có chứng cứ gắn khoản nhận; giữ khoản cần xử lý để đối soát.", null, default)
            : await StorefrontRefundSourceProof.ReadAsync(db, protocol, order, funding, ct);

    public async Task<IReadOnlyList<StorefrontRefundRead>> GetAsync(int orderId, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewOrders, ct);
        var order = await Order(orderId, ct); var physical = await PhysicalBlock(order, ct);
        var canApprove = await permissions.HasPermissionAsync(Store, Actor, StorefrontPermissions.ApproveRefund, ct);
        var canConfirm = await permissions.HasPermissionAsync(Store, Actor, StorefrontPermissions.ConfirmRefund, ct) &&
            await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Report.Treasury.Manage, ct);
        var rows = await db.Set<StorefrontRefund>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == orderId).OrderBy(x => x.Id).Take(201).ToListAsync(ct);
        if (rows.Count > 200) throw new ConflictAppException("Hồ sơ có quá nhiều khoản nhận; cần đối soát riêng trước khi xử lý thêm.");
        var results = new List<StorefrontRefundRead>();
        foreach (var row in rows)
        {
            var funding = await Funding(orderId, row.Id, ct); var proof = await Source(order, funding, ct);
            var paid = await Executed(orderId, row.Id, ct);
            var executionRows = await db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == Store && x.StorefrontOrderId == orderId && x.RefundId == row.Id).OrderBy(x => x.Id).ToListAsync(ct);
            var executions = executionRows.Select(x => new StorefrontRefundExecutionRead(x.Id, x.Amount, x.Fund ?? "", Utc(x.ExecutedAtUtc),
                x.VerificationMethod ?? "Bằng chứng cũ cần đối soát", x.EvidenceReference ?? "", x.ConfirmedByUserId)).ToList();
            var blocked = physical ?? (proof.Eligible ? null : proof.BlockedReason ?? "Nguồn hoàn chưa đủ chứng cứ.");
            if (paid > row.Amount) blocked = "Bằng chứng thực hoàn vượt khoản cần hoàn; dừng để đối soát.";
            var explanation = blocked ?? (row.State == StorefrontRefundState.Rejected
                ? "Hồ sơ chưa chấp thuận. Khoản cần hoàn vẫn được giữ; bổ sung đối soát và duyệt lại trước khi xác nhận thực trả." : null);
            var description = funding?.Kind == StorefrontRefundSourceKind.QrBankReceipt ? "Nguồn tiền ngân hàng gắn với đơn website."
                : funding?.Kind == StorefrontRefundSourceKind.CodReceipt ? "Nguồn thực nhận từ đối soát COD." : "Nguồn cũ đang chờ kiểm tra.";
            var reason = row.Reason.StartsWith("LATE_BANK_RECEIPT:", StringComparison.Ordinal) ? "Tiền nhận sau khi đơn đã hủy."
                : row.Reason.StartsWith("CANCELLED_BANK_RECEIPT:", StringComparison.Ordinal) ? "Hoàn khoản tiền nhận gắn với đơn đã hủy."
                : row.Reason.StartsWith("CANCELLED_COD_RECEIPT:", StringComparison.Ordinal) ? "Khoản COD của đơn hủy cần được đối soát." : row.Reason;
            results.Add(new(row.Id, orderId, order.Code, row.State, row.Amount, paid, reason, description,
                canApprove && blocked is null && paid < row.Amount && (row.State is StorefrontRefundState.Requested or StorefrontRefundState.Rejected),
                canConfirm && blocked is null && paid < row.Amount && row.State == StorefrontRefundState.Approved,
                explanation, executions));
        }
        return results;
    }

    private Task<StorefrontOperationAck> Command<T>(int id, T request, string operation, string permission,
        Func<StorefrontOrder, CancellationToken, Task> change, CancellationToken ct,
        Func<CancellationToken, Task>? payoutLock = null, bool treasuryAuthority = false) where T : StorefrontOperationRequest
        => StorefrontOrderCommand.RunAsync(db, Store, id, request, operation, Actor, new { id, operation, actor = Actor, request },
            async token => { await Guard(permission, token); if (treasuryAuthority) await Guard(PermissionCodes.Report.Treasury.Manage, token); },
            async token => { if (payoutLock is null) await StorefrontOrderProofLocks.LockAsync(db, Store, id, token); else await payoutLock(token); },
            change, clock, ct);

    public Task<StorefrontOperationAck> ApproveAsync(int id, StorefrontRefundDecisionRequest request, CancellationToken ct = default)
    {
        Reason(request.Reason);
        return Command(id, request, "ApproveRefund", StorefrontPermissions.ApproveRefund, async (order, token) =>
        {
            Need(await PhysicalBlock(order, token) is null, "Hàng và trạng thái hủy chưa đủ điều kiện duyệt hoàn.");
            var refund = await Refund(id, request.RefundId, token);
            Need(refund.State is StorefrontRefundState.Requested or StorefrontRefundState.Rejected, "Khoản hoàn đã được duyệt hoặc thực hiện; tải lại hồ sơ.");
            var funding = await Funding(id, refund.Id, token); var proof = await Source(order, funding, token);
            Need(proof.Eligible, proof.BlockedReason ?? "Chưa xác minh đủ nguồn hoàn.");
            Need(funding!.Amount == refund.Amount && await Executed(id, refund.Id, token) < refund.Amount, "Ngân sách nguồn hoàn không khớp hoặc đã thực hiện đủ.");
            db.Add(new StorefrontRefundDecision { StoreId = Store, StorefrontOrderId = id, RefundId = refund.Id,
                Kind = StorefrontRefundDecisionKind.Approved, ActorUserId = Actor, Reason = request.Reason.Trim(), DecidedAtUtc = Now });
            await db.SaveChangesAsync(token);
            refund.State = StorefrontRefundState.Approved; refund.ApprovedByUserId = Actor;
            await db.SaveChangesAsync(token); await StorefrontRefundObligations.RefreshStateAsync(db, order, token);
        }, ct);
    }
    public Task<StorefrontOperationAck> RejectAsync(int id, StorefrontRefundDecisionRequest request, CancellationToken ct = default)
    {
        Reason(request.Reason);
        return Command(id, request, "RejectRefund", StorefrontPermissions.ApproveRefund, async (order, token) =>
        {
            Need(order.Phase == StorefrontOrderPhase.Cancelled, "Chỉ quyết định hồ sơ hoàn của đơn đã hủy.");
            var refund = await Refund(id, request.RefundId, token);
            Need(refund.State is StorefrontRefundState.Requested or StorefrontRefundState.Approved, "Hồ sơ đã được quyết định; tải lại trước khi xử lý.");
            Need(await Executed(id, refund.Id, token) < refund.Amount, "Khoản này đã thực hoàn đủ; không từ chối để xóa lịch sử.");
            db.Add(new StorefrontRefundDecision { StoreId = Store, StorefrontOrderId = id, RefundId = refund.Id,
                Kind = StorefrontRefundDecisionKind.Rejected, ActorUserId = Actor, Reason = request.Reason.Trim(), DecidedAtUtc = Now });
            await db.SaveChangesAsync(token);
            refund.State = StorefrontRefundState.Rejected; refund.ApprovedByUserId = null;
            await db.SaveChangesAsync(token); await StorefrontRefundObligations.RefreshStateAsync(db, order, token);
        }, ct);
    }

    public Task<StorefrontOperationAck> ConfirmExecutionAsync(int id, StorefrontRefundExecutionRequest request, CancellationToken ct = default)
    {
        ValidatePayoutRequest(request);
        PayoutPlan? plan = null;
        return Command(id, request, "ConfirmRefundExecution", StorefrontPermissions.ConfirmRefund, async (order, token) =>
        {
            Need(await PhysicalBlock(order, token) is null, "Hàng và trạng thái hủy chưa đủ điều kiện xác nhận hoàn.");
            var refund = await Refund(id, request.RefundId, token);
            Need(refund.State == StorefrontRefundState.Approved && refund.ApprovedByUserId is > 0, "Cần duyệt hồ sơ trước khi xác nhận thực trả.");
            var last = await db.Set<StorefrontRefundDecision>().AsNoTracking().Where(x => x.StoreId == Store && x.RefundId == refund.Id)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(token);
            Need(last is { Kind: StorefrontRefundDecisionKind.Approved } && last.ActorUserId == refund.ApprovedByUserId, "Thiếu quyết định duyệt còn hiệu lực của chính hồ sơ.");
            var funding = await Funding(id, refund.Id, token); var proof = await Source(order, funding, token);
            Need(proof.Eligible && funding is not null && funding.Amount == refund.Amount, proof.BlockedReason ?? "Nguồn hoàn chưa đủ chứng cứ.");
            var validatedFunding = funding ?? throw new ConflictAppException("Thiếu nguồn hoàn đã xác minh.");
            var source = await ReadPayout(request.SourceKind, request.SourceId, token, plan);
            Need(source.Version == request.SourceVersion, "Chứng từ thực chi đã thay đổi; tải lại trước khi xác nhận.");
            var executed = await Executed(id, refund.Id, token);
            Need(source.Amount > 0 && source.Amount <= refund.Amount - executed, "Phải nhận trọn chứng từ thực chi và không vượt số còn cần hoàn; không chia rồi dùng lại một phiếu.");
            var payout = await AttestPayout(request, source, proof, plan, token);
            Need(!await db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters().AnyAsync(x => x.ExternalPayoutKey == payout.ExternalKey, token), "Mã thực chi đã được xác nhận cho một khoản hoàn khác.");
            int? localReceipt = validatedFunding.GlobalBankReceiptId is int global ? await db.Set<StorefrontBankReceipt>().AsNoTracking()
                .Where(x => x.StoreId == Store && x.GlobalBankReceiptId == global).Select(x => (int?)x.Id).SingleOrDefaultAsync(token) : null;
            db.Add(new StorefrontRefundEvidence { StoreId = Store, StorefrontOrderId = id, RefundId = refund.Id, FundingId = validatedFunding.Id,
                BankReceiptId = localReceipt, PayoutSourceKind = request.SourceKind, TreasuryEntryId = source.TreasuryId, POSShiftCashTransactionId = source.CashId,
                PayerLegalEntityId = proof.Owner, PayerBankAccountId = payout.BankAccountId, Fund = source.Fund, Amount = source.Amount,
                VerificationMethod = payout.Method, EvidenceReference = request.EvidenceReference.Trim(), SourceSnapshotJson = payout.Snapshot,
                ProtectedPayoutEvidence = protocol.Protect(Store, Serialize(new { order.Id, refundId = refund.Id, fundingId = validatedFunding.Id,
                    actualPayout = payout.Snapshot, evidenceContext = payout.ProtectedContext, evidence = request.EvidenceReference.Trim(), confirmedBy = Actor, confirmedAtUtc = Now })),
                ExternalPayoutKey = payout.ExternalKey, EvidenceFingerprint = GlobalBankReceiptLedger.Hash(payout.Snapshot + "|" + request.EvidenceReference.Trim()),
                ExecutedAtUtc = payout.AtUtc, ConfirmedByUserId = Actor });
            await db.SaveChangesAsync(token);
            if (executed + source.Amount == refund.Amount) refund.State = StorefrontRefundState.Executed;
            await db.SaveChangesAsync(token); await StorefrontRefundObligations.RefreshStateAsync(db, order, token);
        }, ct, async token => { plan = await LockPayout(id, request, token); }, treasuryAuthority: true);
    }
}
