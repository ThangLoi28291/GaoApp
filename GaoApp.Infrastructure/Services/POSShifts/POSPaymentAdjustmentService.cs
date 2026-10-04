using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.POSShifts;

public sealed partial class POSPaymentAdjustmentService(AppDbContext db, ITenantContext tenant, ICurrentUser user, IStoreAdminAccess admin)
    : IPOSPaymentAdjustmentService
{
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value : throw new ForbiddenAppException("Chưa chọn cửa hàng.");
    private int UserId => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Vui lòng đăng nhập lại.");
    private IQueryable<POSPaymentAdjustmentRequest> Requests => db.Set<POSPaymentAdjustmentRequest>().Where(x => x.StoreId == StoreId && !x.IsDeleted);
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Reason(string? s) => Trim(s) is {Length: <= 500} value ? value : throw new ValidationAppException("Nhập lý do từ 1 đến 500 ký tự.");
    private Task<string> Actor(CancellationToken ct) => db.Users.Where(x => x.Id == UserId).Select(x => x.FullName ?? x.UserName).SingleAsync(ct);
    private async Task<POSPaymentAdjustmentRequest> Request(int id, CancellationToken ct) =>
        await Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy yêu cầu sửa thanh toán.");
    private async Task<Order> Order(int id, bool locked, CancellationToken ct) => await (locked
        ? db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id} AND [StoreId]={StoreId}").IgnoreQueryFilters()
        : db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == id && x.StoreId == StoreId)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundAppException("Không tìm thấy đơn POS.");
    private async Task<POSShift> Shift(int id, bool locked, CancellationToken ct) => await (locked
        ? db.POSShifts.FromSqlInterpolated($"SELECT * FROM [POSShifts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id} AND [StoreId]={StoreId}")
        : db.POSShifts.AsNoTracking().Where(x => x.Id == id && x.StoreId == StoreId)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundAppException("Không tìm thấy ca POS.");

    private async Task<HashSet<int>> Protected(int[] ids, CancellationToken ct)
    {
        var automatic = await db.Set<AcbQrSession>().IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.PaymentId.HasValue && ids.Contains(x.PaymentId.Value)
            && (x.ConfirmationSource == null || x.ConfirmationSource != AcbConfirmationSource.OfflineManual))
            .Select(x => x.PaymentId!.Value).ToListAsync(ct);
        var callbacks = await db.PosPaymentQrRequests.IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.PaymentId.HasValue && ids.Contains(x.PaymentId.Value)
            && x.ConfirmMode != BankQrConfirmMode.Manual).Select(x => x.PaymentId!.Value).ToListAsync(ct);
        return automatic.Concat(callbacks).ToHashSet();
    }
    private static string? Unavailable(Order o, OrderPayment? p, bool bankProtected) => o.IsDeleted || o.Status is not (OrderStatus.Completed or OrderStatus.Refunded)
        ? "Chỉ điều chỉnh khoản thanh toán của đơn đã chốt, chưa hủy."
        : p == null || p.IsDeleted ? "Khoản thanh toán đã bị hủy hoặc không còn hợp lệ."
        : p.IsDebtCollection ? "Khoản thu công nợ cần đối soát qua sổ công nợ."
        : p.Amount <= 0 ? "Khoản thanh toán phải có số tiền dương."
        : bankProtected ? "Khoản này có chứng từ ngân hàng tự động liên kết. Hãy kiểm tra giao dịch ngân hàng; không đổi phương thức của chứng từ này." : null;
    private static (decimal Cash, decimal NonCash) Buckets(Order o, IEnumerable<OrderPayment> payments, int? id = null, PaymentMethod? method = null)
    {
        var live = payments.Where(x => !x.IsDeleted && !x.IsDebtCollection && x.Amount > 0).ToArray();
        PaymentMethod Method(OrderPayment p) => p.Id == id && method.HasValue ? method.Value : p.Method;
        var budget = Math.Max(0, o.GrandTotal - o.DepositAmount);
        var nonCash = Math.Min(budget, live.Where(x => Method(x) != PaymentMethod.Cash).Sum(x => x.Amount));
        return (Math.Min(budget - nonCash, live.Where(x => Method(x) == PaymentMethod.Cash).Sum(x => x.Amount)), nonCash);
    }
    private static PaymentAdjustmentPosition Position(POSShift s, decimal delta = 0) => new(s.CashSalesTotal + delta,
        s.NonCashSalesTotal - delta, s.ClosingCashExpected + delta, s.ClosingCashActual, s.CashReceivedAmount,
        s.ClosingCashActual - s.ClosingCashExpected - delta, s.NeedsCashReconciliation);
    private static bool Matches(POSPaymentAdjustmentRequest r, Order o, OrderPayment? p) => p != null &&
        Version(p.RowVersion) == r.PaymentVersion && Version(o.RowVersion) == r.OrderVersion &&
        p.Method == r.OldMethod && p.Amount == r.Amount && p.ReferenceCode == r.OldReference && p.Provider == r.OldProvider && o.POSShiftId == r.POSShiftId;
    private async Task<List<OrderPayment>> Payments(int orderId, bool tracked, CancellationToken ct)
    {
        var q = db.OrderPayments.IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.OrderId == orderId);
        return await (tracked ? q : q.AsNoTracking()).ToListAsync(ct);
    }
    private async Task<Dictionary<int, string>> Numbers(int[] ids, CancellationToken ct) => await db.Orders.IgnoreQueryFilters().AsNoTracking()
        .Where(x => x.StoreId == StoreId && ids.Contains(x.Id)).Select(x => new {x.Id,x.OrderNumber})
        .ToDictionaryAsync(x => x.Id, x => x.OrderNumber ?? $"Đơn #{x.Id}", ct);
    private async Task<Dictionary<int, string?>> Labels(int[] ids, CancellationToken ct) => await db.POSShifts.AsNoTracking()
        .Where(x => x.StoreId == StoreId && ids.Contains(x.Id)).Select(x => new {x.Id,x.ShiftCode}).ToDictionaryAsync(x => x.Id,x => x.ShiftCode,ct);
    private static PaymentAdjustmentItemDto Map(POSPaymentAdjustmentRequest r, string number, string? code, int? customerId = null, string? customerName = null) => new(r.Id,r.PaymentId ?? 0,r.OrderId ?? 0,number,
        r.POSShiftId,code,r.RequestedByName,r.CreatedAtUtc,r.Status.ToString(),r.RequestReason,r.Amount,r.OldMethod.ToString(),r.NewMethod.ToString(),
        r.OldReference,r.NewReference,r.ExpectedDelta,Version(r.RowVersion),r.ReviewedByName,r.ReviewedAtUtc,r.ReviewNote,
        r.BeforeShiftJson,r.AfterShiftJson,r.AppliedToClosedShift,r.ReconciledAtUtc,r.ReconciledByName,r.ReconciliationNote,r.DepositEntryId,customerId,customerName);

    public async Task<PagedResult<PaymentAdjustmentCandidateDto>> PaymentsAsync(int page, string? keyword, int? paymentId, int? orderId, int? shiftId, CancellationToken ct)
    {
        page = Math.Clamp(page,1,100000000);
        var q = from p in db.OrderPayments.AsNoTracking() join o in db.Orders.AsNoTracking() on p.OrderId equals o.Id
                join s in db.POSShifts.AsNoTracking() on o.POSShiftId equals s.Id
                where p.StoreId == StoreId && o.StoreId == StoreId && s.StoreId == StoreId && s.OpenedByUserId == UserId
                    && !p.IsDebtCollection && (o.Status == OrderStatus.Completed || o.Status == OrderStatus.Refunded)
                select new {Payment=p,Order=o,Shift=s};
        if(paymentId.HasValue)q=q.Where(x=>x.Payment.Id==paymentId);
        if(orderId.HasValue)q=q.Where(x=>x.Order.Id==orderId);
        if(shiftId.HasValue)q=q.Where(x=>x.Shift.Id==shiftId);
        if(Trim(keyword) is {} k)q=q.Where(x=>(x.Order.OrderNumber!=null && x.Order.OrderNumber.Contains(k)) || (x.Payment.ReferenceCode!=null && x.Payment.ReferenceCode.Contains(k)));
        var total=await q.CountAsync(ct);var rows=await q.OrderByDescending(x=>x.Payment.Id).Skip((page-1)*20).Take(20).ToListAsync(ct);
        var protectedIds=await Protected(rows.Select(x=>x.Payment.Id).ToArray(),ct);
        var orderIds=rows.Select(x=>x.Order.Id).ToArray();
        var pending=await Requests.Where(x=>x.OrderId.HasValue&&orderIds.Contains(x.OrderId.Value)&&x.Status==POSCashAdjustmentStatus.Pending).ToDictionaryAsync(x=>x.OrderId!.Value,x=>x.Id,ct);
        return new(page,20,total,rows.Select(x=>{
            var unavailable=Unavailable(x.Order,x.Payment,protectedIds.Contains(x.Payment.Id));
            var pendingId=pending.GetValueOrDefault(x.Order.Id);
            return new PaymentAdjustmentCandidateDto(x.Payment.Id,x.Order.Id,x.Order.OrderNumber??$"Đơn #{x.Order.Id}",x.Shift.Id,x.Shift.ShiftCode,
                x.Shift.Status.ToString(),x.Payment.Method.ToString(),x.Payment.Amount,x.Payment.ReferenceCode,x.Payment.PaidAtUtc,Version(x.Payment.RowVersion),
                unavailable==null&&pendingId==0,unavailable,pendingId==0?null:pendingId);
        }).ToList());
    }
    public async Task<PagedResult<PaymentAdjustmentItemDto>> ListAsync(int page, string? status, int? shiftId, CancellationToken ct)
    {
        page=Math.Clamp(page,1,100000000);var q=Requests.AsNoTracking();
        if(!await admin.IsAdminAsync(ct))q=q.Where(x=>x.RequestedByUserId==UserId);
        if(shiftId.HasValue)q=q.Where(x=>x.POSShiftId==shiftId);
        if(status=="NeedsReconciliation")q=q.Where(x=>x.Status==POSCashAdjustmentStatus.Approved&&x.AppliedToClosedShift&&x.ReconciledAtUtc==null);
        else if(Trim(status) is {} text){if(!Enum.TryParse<POSCashAdjustmentStatus>(text,out var value)||!Enum.IsDefined(value))throw new ValidationAppException("Trạng thái không hợp lệ.");q=q.Where(x=>x.Status==value);}
        var total=await q.CountAsync(ct);var rows=await q.OrderByDescending(x=>x.Id).Skip((page-1)*20).Take(20).ToListAsync(ct);
        var numbers=await Numbers(rows.Where(x=>x.OrderId.HasValue).Select(x=>x.OrderId!.Value).ToArray(),ct);var labels=await Labels(rows.Select(x=>x.POSShiftId).ToArray(),ct);
        var deposits=await DepositLabels(rows.Where(x=>x.DepositEntryId.HasValue).Select(x=>x.DepositEntryId!.Value).ToArray(),ct);
        return new(page,20,total,rows.Select(x=>{
            var deposit=x.DepositEntryId.HasValue?deposits.GetValueOrDefault(x.DepositEntryId.Value):null;
            return Map(x,deposit?.Number??numbers.GetValueOrDefault(x.OrderId??0)??$"Đơn #{x.OrderId}",labels.GetValueOrDefault(x.POSShiftId),deposit?.CustomerId,deposit?.CustomerName);
        }).ToList());
    }
    public async Task<PaymentAdjustmentDetailDto> DetailAsync(int id, CancellationToken ct)
    {
        var r=await Request(id,ct);var isAdmin=await admin.IsAdminAsync(ct);
        if(!isAdmin&&r.RequestedByUserId!=UserId)throw new ForbiddenAppException("Bạn chỉ được xem yêu cầu của mình.");
        if(r.DepositEntryId.HasValue)return await DepositDetail(r,isAdmin,ct);
        var o=await Order(r.OrderId!.Value,false,ct);var s=await Shift(r.POSShiftId,false,ct);var payments=await Payments(o.Id,false,ct);var p=payments.SingleOrDefault(x=>x.Id==r.PaymentId);
        var unavailable=Unavailable(o,p,(await Protected([r.PaymentId!.Value],ct)).Contains(r.PaymentId.Value));
        if(r.Status!=POSCashAdjustmentStatus.Pending)unavailable=null;
        else if(unavailable==null&&!Matches(r,o,p))unavailable="Đơn hoặc khoản thanh toán đã thay đổi. Hãy từ chối và lập yêu cầu mới sau khi kiểm tra lại.";
        var before=Buckets(o,payments);var after=Buckets(o,payments,r.PaymentId,r.NewMethod);
        var delta=r.Status==POSCashAdjustmentStatus.Pending&&unavailable==null?after.Cash-before.Cash:0;
        return new(Map(r,o.OrderNumber??$"Đơn #{o.Id}",s.ShiftCode),Position(s),Position(s,delta),Version(s.RowVersion),
            isAdmin&&r.Status==POSCashAdjustmentStatus.Pending&&unavailable==null,r.RequestedByUserId==UserId&&r.Status==POSCashAdjustmentStatus.Pending,unavailable);
    }
    public async Task<PaymentAdjustmentPreviewDto> PreviewAsync(int paymentId, PaymentMethod method, CancellationToken ct)
    {
        if(!Enum.IsDefined(method))throw new ValidationAppException("Phương thức không hợp lệ.");
        var p=await db.OrderPayments.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==StoreId&&x.Id==paymentId,ct)
            ??throw new NotFoundAppException("Không tìm thấy khoản thanh toán.");
        var o=await Order(p.OrderId,false,ct);var s=await Shift(o.POSShiftId,false,ct);
        if(s.OpenedByUserId!=UserId)throw new ForbiddenAppException("Bạn chỉ được đề nghị sửa thanh toán trong ca của mình.");
        if(Unavailable(o,p,(await Protected([paymentId],ct)).Contains(paymentId)) is {} error)throw new ConflictAppException(error);
        var payments=await Payments(o.Id,false,ct);var before=Buckets(o,payments);var after=Buckets(o,payments,paymentId,method);
        var delta=after.Cash-before.Cash;return new(Position(s),Position(s,delta),delta);
    }
    public async Task<int> CreateAsync(int paymentId, CreatePaymentAdjustmentRequest input, CancellationToken ct)
    {
        var reason=Reason(input.RequestReason);var reference=Trim(input.Reference);
        if(input.ClientRequestId==Guid.Empty||!Enum.IsDefined(input.Method)||reference?.Length>100)
            throw new ValidationAppException("Kiểm tra mã yêu cầu, phương thức và mã giao dịch.");
        if(input.Method==PaymentMethod.Cash)reference=null;
        var seed=await db.OrderPayments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==StoreId&&x.Id==paymentId,ct)
            ??throw new NotFoundAppException("Không tìm thấy khoản thanh toán.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var o=await Order(seed.OrderId,true,ct);var s=await Shift(o.POSShiftId,true,ct);
        if(s.OpenedByUserId!=UserId)throw new ForbiddenAppException("Bạn chỉ được đề nghị sửa thanh toán trong ca của mình.");
        var existing=await Requests.SingleOrDefaultAsync(x=>x.ClientRequestId==input.ClientRequestId,ct);
        if(existing!=null){if(existing.RequestedByUserId==UserId&&existing.PaymentId==paymentId&&existing.NewMethod==input.Method&&existing.NewReference==reference&&existing.RequestReason==reason&&existing.PaymentVersion==input.RowVersion)return existing.Id;
            throw new ConflictAppException("Mã yêu cầu đã được sử dụng cho nội dung khác.");}
        var payments=await Payments(o.Id,true,ct);var p=payments.SingleOrDefault(x=>x.Id==paymentId);
        if(Unavailable(o,p,(await Protected([paymentId],ct)).Contains(paymentId)) is {} error)throw new ConflictAppException(error);
        if(Version(p!.RowVersion)!=input.RowVersion)throw new ConflictAppException("Khoản thanh toán đã thay đổi. Vui lòng tải lại.");
        if(await Requests.AnyAsync(x=>x.OrderId==o.Id&&x.Status==POSCashAdjustmentStatus.Pending,ct))throw new ConflictAppException("Đơn đã có yêu cầu đang chờ duyệt. Hãy xử lý yêu cầu đó trước.");
        if(p.Method==input.Method&&p.ReferenceCode==reference)throw new ValidationAppException("Chưa có nội dung thay đổi.");
        var before=Buckets(o,payments);var after=Buckets(o,payments,paymentId,input.Method);
        var r=new POSPaymentAdjustmentRequest {StoreId=StoreId,ClientRequestId=input.ClientRequestId,PaymentId=p.Id,OrderId=o.Id,POSShiftId=s.Id,
            RequestedByUserId=UserId,RequestedByName=await Actor(ct),RequestReason=reason,PaymentVersion=Version(p.RowVersion),OrderVersion=Version(o.RowVersion),
            Amount=p.Amount,OldMethod=p.Method,NewMethod=input.Method,OldReference=p.ReferenceCode,NewReference=reference,OldProvider=p.Provider,ExpectedDelta=after.Cash-before.Cash};
        db.Add(r);await Save(ct);await tx.CommitAsync(ct);return r.Id;
    }
    public async Task DecideAsync(int id, string action, PaymentAdjustmentDecision input, CancellationToken ct)
    {
        if(action is not ("approve" or "reject" or "withdraw"))throw new ValidationAppException("Thao tác không hợp lệ.");
        if(action!="withdraw")await admin.RequireAsync(ct);
        var seed=await Request(id,ct);
        if(action=="withdraw"&&seed.RequestedByUserId!=UserId)throw new ForbiddenAppException("Bạn chỉ được rút yêu cầu của mình.");
        var note=action=="reject"?Reason(input.Note):Trim(input.Note);if(note?.Length>500)throw new ValidationAppException("Ghi chú tối đa 500 ký tự.");
        if(seed.DepositEntryId.HasValue){await DecideDeposit(seed,action,input,note,ct);return;}
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Match the sale/debt/refund lock order, then serialize every change to this shift.
        var o=await Order(seed.OrderId!.Value,true,ct);var s=await Shift(seed.POSShiftId,true,ct);var r=await Requests.SingleAsync(x=>x.Id==id,ct);
        var next=action=="approve"?POSCashAdjustmentStatus.Approved:action=="reject"?POSCashAdjustmentStatus.Rejected:POSCashAdjustmentStatus.Withdrawn;
        if(r.Status==next)return;
        if(r.Status!=POSCashAdjustmentStatus.Pending||Version(r.RowVersion)!=input.RowVersion)throw new ConflictAppException("Yêu cầu đã được xử lý hoặc thay đổi. Vui lòng tải lại.");
        if(action=="approve")
        {
            var payments=await Payments(o.Id,true,ct);var p=payments.SingleOrDefault(x=>x.Id==r.PaymentId);
            if(Unavailable(o,p,(await Protected([r.PaymentId!.Value],ct)).Contains(r.PaymentId.Value)) is {} error)throw new ConflictAppException(error);
            if(!Matches(r,o,p))throw new ConflictAppException("Đơn hoặc khoản thanh toán gốc đã thay đổi. Hãy từ chối và lập yêu cầu mới.");
            if(Version(s.RowVersion)!=input.ShiftRowVersion)throw new ConflictAppException("Số liệu ca đã thay đổi. Mở lại chi tiết để kiểm tra ảnh hưởng trước khi duyệt.");
            var before=Buckets(o,payments);var after=Buckets(o,payments,r.PaymentId,r.NewMethod);var delta=after.Cash-before.Cash;
            if(s.CashSalesTotal+delta<0||s.NonCashSalesTotal-delta<0||s.ClosingCashExpected!=s.OpeningCash+s.CashSalesTotal+s.CashInTotal-s.CashOutTotal-s.CashRefundTotal)
                throw new ConflictAppException("Tổng tiền bán của ca chưa khớp chứng từ. Cần đối soát trước khi duyệt.");
            r.BeforeShiftJson=JsonSerializer.Serialize(Position(s),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            s.CashSalesTotal+=delta;s.NonCashSalesTotal-=delta;s.RecalcExpected();
            s.UpdatedAtUtc=DateTime.UtcNow;s.UpdatedBy=UserId;
            r.ExpectedDelta=delta;r.AppliedToClosedShift=s.Status==POSShiftStatus.Closed;if(r.AppliedToClosedShift)s.NeedsCashReconciliation=true;
            p!.Method=r.NewMethod;p.ReferenceCode=r.NewReference;p.Provider="POS_ADJUSTMENT";
            o.UpdatedAtUtc=DateTime.UtcNow;o.UpdatedBy=UserId;
            r.AfterShiftJson=JsonSerializer.Serialize(Position(s),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            // Never change Amount, PaidAtUtc, paid/debt totals, refunds, counted cash or closing-slip snapshots.
        }
        r.Status=next;r.ReviewedByUserId=UserId;r.ReviewedByName=await Actor(ct);r.ReviewedAtUtc=DateTime.UtcNow;r.ReviewNote=note;
        await Save(ct);await tx.CommitAsync(ct);
    }
    private async Task Save(CancellationToken ct)
    {
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){throw new ConflictAppException("Dữ liệu vừa thay đổi. Vui lòng tải lại trước khi thao tác.");}
        catch(DbUpdateException e) when(e.InnerException is Microsoft.Data.SqlClient.SqlException {Number:2601 or 2627})
        {throw new ConflictAppException("Đơn đã có yêu cầu chờ duyệt hoặc mã yêu cầu đã được sử dụng.");}
    }
}
