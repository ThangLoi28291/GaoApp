using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontCancellationService(AppDbContext db, ICurrentUser user, IInventoryBalanceRepository balances,
    TimeProvider clock, ICurrentStorePermissionService? permissions = null) : IStorefrontCancellationService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new NotFoundAppException("Không tìm thấy đơn.");
    private int Actor => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Cần đăng nhập.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private ICurrentStorePermissionService StaffPermissions => permissions ?? throw new ForbiddenAppException("Chưa cấu hình quyền nhân viên.");
    private Task Guard(string permission, CancellationToken ct) => StorefrontStaffAuthorization.RequireAsync(db, user, permissions, permission, ct);
    private async Task<StorefrontOperationAck> Command<T>(int id, T request, string operation, string permission,
        Func<StorefrontOrder, CancellationToken, Task> change, CancellationToken ct,
        Func<CancellationToken, Task>? additionalAuthorize = null) where T : StorefrontOperationRequest
    {
        await Guard(permission, ct);
        return await StorefrontOrderCommand.RunAsync(db, Store, id, request, operation, Actor,
            new { id, operation, actor = Actor, request }, async token =>
            {
                await Guard(permission, token);
                if (additionalAuthorize is not null) await additionalAuthorize(token);
            }, token => StorefrontOrderProofLocks.LockAsync(db, Store, id, token), change, clock, ct);
    }
    private static string? TokenHash(string? token)
    {
        if (!Regex.IsMatch(token ?? "", "^[A-Za-z0-9_-]{43}$")) return null;
        try { var value = WebEncoders.Base64UrlDecode(token!); if (value.Length != 32 || WebEncoders.Base64UrlEncode(value) != token) return null; }
        catch (FormatException) { return null; }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token!)));
    }
    private async Task<StorefrontOrder?> GuestOrder(string token, CancellationToken ct)
    {
        var hash = TokenHash(token);
        if (hash is null || db.CurrentStoreId is not > 0 || !await db.Stores.AsNoTracking().AnyAsync(x => x.Id == Store && x.IsActive, ct)) return null;
        return await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.GuestTokenHash == hash, ct);
    }
    private async Task AuthorizeGuest(int id, string hash, CancellationToken ct)
    {
        if (!await db.Stores.AsNoTracking().AnyAsync(x => x.Id == Store && x.IsActive, ct) ||
            !await db.StorefrontOrders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == id && x.GuestTokenHash == hash, ct))
            throw new NotFoundAppException("Không tìm thấy đơn.");
    }
    private static void Reason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000) throw new ValidationAppException("Cần lý do rõ ràng, tối đa 1.000 ký tự.");
    }
    private static StorefrontTransition Apply(StorefrontOrder order, StorefrontCommand command, StorefrontCancellationPhysicalFacts physical,
        StorefrontActor actor, bool received = false, bool rejectionReason = false)
    {
        try
        {
            return StorefrontWorkflowPolicy.Apply(order.Phase, command, new StorefrontWorkflowFacts
            {
                StoreId = order.StoreId, OrderId = order.Id, Revision = order.Revision, Method = order.PaymentMethod,
                PaymentConfirmed = order.PaymentState == StorefrontPaymentState.Confirmed,
                PaymentNeedsReview = order.PaymentState == StorefrontPaymentState.Review, HasReceivedFunds = received,
                PhysicalDispatchRecorded = physical.HasDispatch || physical.HasIncident, HasDeliveredGoods = physical.HasDeliveredGoods,
                CancellationRequested = physical.PendingRequest is not null, ReturnAuthorised = physical.ReturnAuthorised,
                CancellationRejectionReasonRecorded = rejectionReason
            }, actor, order.Revision);
        }
        catch (StorefrontRuleException ex) { throw new ConflictAppException(ex.Message); }
    }
    public async Task<StorefrontOrderControlPage> ListAsync(StorefrontOrderControlQuery query, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewOrders, ct);
        if (query is null || query.Page is < 1 or > 100000 || query.PageSize is < 1 or > 100 || !Enum.IsDefined(query.Filter) || (query.Search?.Length ?? 0) > 100)
            throw new ValidationAppException("Bộ lọc hoặc trang danh sách không hợp lệ.");
        var requests = db.Set<StorefrontCancellationRequest>().IgnoreQueryFilters().Where(r => r.StoreId == Store && !r.IsImmediate &&
            !db.Set<StorefrontCancellationDecision>().IgnoreQueryFilters().Any(d => d.StoreId == Store && d.CancellationRequestId == r.Id));
        var conflicts = db.Set<StorefrontCancellationDecision>().IgnoreQueryFilters().Where(d => d.StoreId == Store && d.Kind == StorefrontCancellationDecisionKind.SupersededByActualDelivery &&
            !db.Set<StorefrontCancellationReviewAcknowledgement>().IgnoreQueryFilters().Any(a => a.StoreId == Store && a.CancellationDecisionId == d.Id));
        var rows = db.StorefrontOrders.AsNoTracking().Where(x => x.StoreId == Store);
        rows = query.Filter switch
        {
            StorefrontOrderControlFilter.PendingCancellation => rows.Where(x => requests.Any(r => r.StorefrontOrderId == x.Id)),
            StorefrontOrderControlFilter.RefundOutstanding => rows.Where(x => x.RefundState != StorefrontRefundState.None && x.RefundState != StorefrontRefundState.Executed),
            StorefrontOrderControlFilter.DeliveryConflictReview => rows.Where(x => conflicts.Any(d => d.StorefrontOrderId == x.Id)),
            _ => rows
        };
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Code.Contains(search) || EF.Functions.Collate(x.RecipientName, "Vietnamese_CI_AI").Contains(search));
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => new
        {
            Order = x, Pending = requests.Any(r => r.StorefrontOrderId == x.Id), Conflict = conflicts.Any(d => d.StorefrontOrderId == x.Id),
            Amount = db.Set<StorefrontRefund>().Where(r => r.StoreId == Store && r.StorefrontOrderId == x.Id).Sum(r => (decimal?)r.Amount) ?? 0,
            Executed = db.Set<StorefrontRefundEvidence>().Where(e => e.StoreId == Store && e.StorefrontOrderId == x.Id && e.FundingId != null).Sum(e => (decimal?)e.Amount) ?? 0,
            MissingProof = db.Set<StorefrontRefund>().Any(r => r.StoreId == Store && r.StorefrontOrderId == x.Id &&
                !db.Set<StorefrontRefundFunding>().Any(f => f.StoreId == Store && f.RefundId == r.Id)) ||
                x.RefundState == StorefrontRefundState.Requested && !db.Set<StorefrontRefund>().Any(r => r.StoreId == Store && r.StorefrontOrderId == x.Id)
        }).ToListAsync(ct);
        return new(query.Page, query.PageSize, total, page.Select(x => new StorefrontOrderControlRow(x.Order.Id, x.Order.Code, x.Order.RecipientName,
            DateTime.SpecifyKind(x.Order.CreatedAtUtc, DateTimeKind.Utc), x.Order.Phase, x.Order.RefundState, x.Pending, x.Conflict, x.Amount, x.Executed, x.MissingProof)).ToList());
    }
    public async Task<StorefrontGuestCancellationView?> GetGuestAsync(string token, CancellationToken ct = default)
    {
        var order = await GuestOrder(token, ct); if (order is null) return null;
        var physical = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, ct);
        var self = order.Phase is StorefrontOrderPhase.AwaitingPayment or StorefrontOrderPhase.AwaitingPreparation &&
            !physical.HasDispatch && !physical.HasDeliveredGoods && !physical.HasIncident;
        var request = order.Phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping &&
            !physical.HasDeliveredGoods && !physical.ReturnAuthorised && physical.PendingRequest is null;
        var message = physical.HasDeliveredGoods ? "Đã có hàng giao thành công. Không thể hủy trực tuyến; cửa hàng sẽ kiểm tra yêu cầu đã gửi." :
            physical.HasIncident ? "Hàng thực tế đang được đối soát. Không tự coi hàng đã trở về kho." :
            physical.ReturnAuthorised && order.Phase != StorefrontOrderPhase.Cancelled ? "Đã chấp thuận thu hồi; đơn chỉ kết thúc hủy khi nhận đủ hàng thực tế." :
            physical.PendingRequest is not null ? "Cửa hàng đã nhận yêu cầu hủy, đang kiểm tra hàng thực tế." :
            order.Phase == StorefrontOrderPhase.Cancelled ? "Đơn đã hủy. Tiền cần hoàn và tiền thực hoàn được theo dõi riêng; tiền về muộn không mở lại đơn." :
            self ? "Có thể tự hủy trước khi chuẩn bị. Nếu ngân hàng chưa rõ kết quả, cửa hàng tiếp tục kiểm tra tiền về." :
            request ? "Gửi yêu cầu để cửa hàng kiểm tra và duyệt. Chưa coi đơn đã hủy hoặc tiền đã hoàn." : "Không thể hủy ở trạng thái hiện tại.";
        return new(order.Code, order.Phase, order.Revision, Convert.ToBase64String(order.RowVersion), self, request,
            physical.PendingRequest is not null, physical.ReturnAuthorised, physical.HasDeliveredGoods, physical.HasIncident,
            message, History(physical), await StorefrontRefundObligations.ReadGuestAsync(db, order, ct))
        {
            RefundState = order.RefundState
        };
    }
    public async Task<StorefrontOperationAck> CancelGuestAsync(StorefrontGuestCancellationRequest request, CancellationToken ct = default)
    {
        var original = await GuestOrder(request.Token, ct) ?? throw new NotFoundAppException("Không tìm thấy đơn."); Reason(request.Reason);
        var hash = TokenHash(request.Token)!; const string operation = "CancelGuestOrder";
        return await StorefrontOrderCommand.RunAsync(db, Store, original.Id, request, operation, null,
            new { id = original.Id, operation, guestTokenHash = hash, request.ClientRequestId, request.ExpectedRevision, request.ExpectedVersion, request.Reason, request.Note },
            token => AuthorizeGuest(original.Id, hash, token), token => StorefrontOrderProofLocks.LockAsync(db, Store, original.Id, token), async (order, token) =>
            {
                var physical = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, token);
                if (physical.PendingRequest is not null) throw new ConflictAppException("Đã có yêu cầu hủy đang chờ xử lý.");
                var immediate = order.Phase is StorefrontOrderPhase.AwaitingPayment or StorefrontOrderPhase.AwaitingPreparation;
                var transition = Apply(order, immediate ? StorefrontCommand.SelfCancel : StorefrontCommand.RequestCancellation,
                    physical, new(Store, StorefrontActorKind.Guest, null, StorefrontCapability.None, order.Id, true));
                db.Add(new StorefrontCancellationRequest { StoreId = Store, StorefrontOrderId = order.Id, ClientRequestId = request.ClientRequestId,
                    GuestTokenHash = hash, Reason = request.Reason.Trim(), RequestedAtUtc = Now, IsImmediate = immediate });
                await db.SaveChangesAsync(token);
                if (transition.ReleaseOwnHolds)
                {
                    await StorefrontCancellationFacts.ReleaseOwnHoldsAsync(db, balances, order, Now, "Khách hủy: " + request.Reason.Trim(), token);
                    await db.SaveChangesAsync(token);
                    order.Phase = transition.NextPhase; order.DeliveryState = StorefrontDeliveryState.NotPlanned;
                    await db.SaveChangesAsync(token);
                    await StorefrontRefundObligations.EnsureAsync(db, order, clock, token);
                }
            }, clock, ct);
    }
    public async Task<StorefrontCancellationRead> GetAsync(int orderId, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewOrders, ct);
        var order = await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == orderId, ct)
            ?? throw new NotFoundAppException("Không tìm thấy đơn.");
        var facts = await StorefrontCancellationFacts.ReadAsync(db, Store, orderId, ct);
        var allowed = await StaffPermissions.HasPermissionAsync(Store, Actor, StorefrontPermissions.ApproveCancellation, ct);
        var approve = allowed && facts.PendingRequest is not null && !facts.HasDeliveredGoods && !facts.HasIncident &&
            (order.Phase == StorefrontOrderPhase.Preparing && !facts.HasDispatch || order.Phase == StorefrontOrderPhase.Shipping && facts.CompleteDispatchProof);
        var reject = allowed && facts.PendingRequest is not null && !facts.HasDeliveredGoods && !facts.ReturnAuthorised &&
            order.Phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping;
        var acknowledge = allowed && await StaffPermissions.HasPermissionAsync(Store, Actor, StorefrontPermissions.Reconcile, ct) &&
            facts.NeedsDeliveryReview && facts.AllChildrenDelivered && facts.CompleteDispatchProof && !facts.HasIncident && !facts.ReturnAuthorised &&
            order.Phase == StorefrontOrderPhase.Delivered && order.RefundState == StorefrontRefundState.None;
        return new(order.Id, order.Code, order.Phase, order.Revision, Convert.ToBase64String(order.RowVersion), facts.PendingRequest?.Id,
            facts.PendingRequest?.Reason, facts.ReturnAuthorised, facts.HasDispatch, facts.HasDeliveredGoods, facts.HasIncident, approve, reject,
            facts.HasDeliveredGoods ? "Đã có hàng giao khách; không hủy/thu hồi trong phạm vi bản đầu." : facts.HasIncident ? "Đang có sự cố: chưa thể duyệt hủy; có thể từ chối yêu cầu với lý do. Sự cố vẫn cần đối soát." : null,
            History(facts), facts.NeedsDeliveryReview, acknowledge, facts.DeliveryConflictRequestId);
    }
    public Task<StorefrontOperationAck> ApproveAsync(int orderId, StorefrontCancellationDecisionRequest request, CancellationToken ct = default)
        => Decide(orderId, request, true, ct);
    public Task<StorefrontOperationAck> RejectAsync(int orderId, StorefrontCancellationDecisionRequest request, CancellationToken ct = default)
        => Decide(orderId, request, false, ct);
    private Task<StorefrontOperationAck> Decide(int id, StorefrontCancellationDecisionRequest request, bool approve, CancellationToken ct)
    {
        Reason(request.Reason);
        return Command(id, request, approve ? "ApproveCancellation" : "RejectCancellation", StorefrontPermissions.ApproveCancellation, async (order, token) =>
        {
            var facts = await StorefrontCancellationFacts.ReadAsync(db, Store, id, token);
            if (facts.PendingRequest?.Id != request.CancellationRequestId || approve && (facts.HasIncident || facts.HasDispatch && !facts.CompleteDispatchProof))
                throw new ConflictAppException("Yêu cầu hoặc chứng cứ bàn giao không còn phù hợp; cần đối soát thực tế.");
            var transition = Apply(order, approve ? StorefrontCommand.ApproveCancellation : StorefrontCommand.RejectCancellation, facts,
                new(Store, StorefrontActorKind.Staff, Actor, StorefrontCapability.ApproveCancellation), rejectionReason: true);
            db.Add(new StorefrontCancellationDecision { StoreId = Store, StorefrontOrderId = id, CancellationRequestId = request.CancellationRequestId,
                Kind = approve ? StorefrontCancellationDecisionKind.Approved : StorefrontCancellationDecisionKind.Rejected, ActorUserId = Actor,
                Reason = request.Reason.Trim(), DecidedAtUtc = Now, AuthorisesPhysicalReturn = transition.Cancellation == StorefrontCancellationEffect.AwaitPhysicalReturn });
            await db.SaveChangesAsync(token);
            if (transition.ReleaseOwnHolds)
            {
                await StorefrontCancellationFacts.ReleaseOwnHoldsAsync(db, balances, order, Now, "Duyệt hủy: " + request.Reason.Trim(), token);
                await db.SaveChangesAsync(token);
                order.Phase = transition.NextPhase; order.DeliveryState = StorefrontDeliveryState.NotPlanned;
                var children = await db.DeliveryOrders.Where(x => x.StoreId == Store && x.SourceKind == DeliverySourceKind.Storefront && x.StorefrontOrderId == id).ToListAsync(token);
                foreach (var child in children) child.State = DeliveryState.Cancelled;
                await db.SaveChangesAsync(token); await StorefrontRefundObligations.EnsureAsync(db, order, clock, token);
            }
            else if (transition.Cancellation == StorefrontCancellationEffect.AwaitPhysicalReturn)
            {
                var works = await db.Set<StorefrontFulfillmentWork>().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).ToListAsync(token);
                foreach (var work in works.Where(x => x.Stage == StorefrontFulfillmentStage.Dispatched)) work.Stage = StorefrontFulfillmentStage.ReturnPending;
                var children = await db.DeliveryOrders.Where(x => x.StoreId == Store && x.SourceKind == DeliverySourceKind.Storefront && x.StorefrontOrderId == id).ToListAsync(token);
                foreach (var child in children) child.State = DeliveryState.AwaitingReconciliation;
                order.DeliveryState = StorefrontDeliveryState.ReturnPending;
            }
        }, ct);
    }
    public Task<StorefrontOperationAck> AcknowledgeDeliveryConflictAsync(int id, StorefrontCancellationDecisionRequest request, CancellationToken ct = default)
    {
        Reason(request.Reason);
        return Command(id, request, "AcknowledgeCancellationDeliveryConflict", StorefrontPermissions.ApproveCancellation, async (order, token) =>
        {
            var facts = await StorefrontCancellationFacts.ReadAsync(db, Store, id, token);
            var decision = facts.Decisions.SingleOrDefault(x => x.CancellationRequestId == request.CancellationRequestId && x.Kind == StorefrontCancellationDecisionKind.SupersededByActualDelivery);
            if (decision is null || !facts.NeedsDeliveryReview || !facts.AllChildrenDelivered || !facts.CompleteDispatchProof || facts.HasIncident || facts.ReturnAuthorised ||
                order.Phase != StorefrontOrderPhase.Delivered || order.RefundState != StorefrontRefundState.None)
                throw new ConflictAppException("Chỉ xác nhận kiểm tra khi toàn bộ hàng đã giao thực tế, không sự cố, thu hồi hoặc hoàn tiền treo.");
            if (await db.Set<StorefrontCancellationReviewAcknowledgement>().AnyAsync(x => x.StoreId == Store && x.CancellationDecisionId == decision.Id, token))
                throw new ConflictAppException("Tình huống này đã được xác nhận kiểm tra.");
            db.Add(new StorefrontCancellationReviewAcknowledgement { StoreId = Store, StorefrontOrderId = id,
                CancellationDecisionId = decision.Id, ActorUserId = Actor, Reason = request.Reason.Trim(), AcknowledgedAtUtc = Now });
        }, ct, additionalAuthorize: token => Guard(StorefrontPermissions.Reconcile, token));
    }
    private static IReadOnlyList<StorefrontCancellationHistory> History(StorefrontCancellationPhysicalFacts facts)
    {
        var rows = facts.Requests.Select(x => new StorefrontCancellationHistory(x.IsImmediate ? "Đã tự hủy" : "Đã gửi yêu cầu hủy", DateTime.SpecifyKind(x.RequestedAtUtc, DateTimeKind.Utc), x.Reason)).ToList();
        rows.AddRange(facts.Decisions.Select(x => new StorefrontCancellationHistory(x.Kind switch
        {
            StorefrontCancellationDecisionKind.Approved => x.AuthorisesPhysicalReturn ? "Chấp thuận thu hồi, chờ hàng thực về" : "Đã duyệt hủy",
            StorefrontCancellationDecisionKind.Rejected => "Không chấp thuận yêu cầu hủy",
            StorefrontCancellationDecisionKind.SupersededByActualReturn => "Đã nhận lại đủ hàng và kết thúc hủy",
            _ => "Hàng đã giao thực tế, cửa hàng kiểm tra yêu cầu hủy"
        }, DateTime.SpecifyKind(x.DecidedAtUtc, DateTimeKind.Utc), x.Reason)));
        return rows.OrderBy(x => x.AtUtc).ToList();
    }
}
