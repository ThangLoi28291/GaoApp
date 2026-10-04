using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Infrastructure.Services.Orders;

public sealed class CustomerDepositService(AppDbContext db, IAppUnitOfWork uow, ICurrentPOSContext pos,
    ILocalVietQrGenerator vietQr) : ICustomerDepositService
{
    private int StoreId => db.CurrentStoreId ?? throw new BusinessRuleException("Chưa chọn cửa hàng.");
    private static void ValidateMoney(decimal amount)
    {
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount >= 10000000000000000m)
            throw new BusinessRuleException("Số tiền phải là số nguyên dương hợp lệ.");
    }
    private async Task LockAsync(string resource, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r<0 THROW 51000,'Deposit is busy.',1;", ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 51000)
        { throw new ConflictAppException("Phiếu cọc đang được xử lý. Vui lòng thử lại cùng yêu cầu."); }
    }
    private async Task<POSShift> ShiftAsync(CancellationToken ct)
    {
        if (!pos.IsAvailable || pos.StoreId != StoreId || !pos.UserId.HasValue)
            throw new BusinessRuleException("Cần đăng nhập quầy và mở ca để xử lý tiền cọc.");
        var shift = await db.POSShifts.FromSqlInterpolated($"SELECT * FROM [POSShifts] WITH (UPDLOCK, ROWLOCK) WHERE [StoreId]={StoreId} AND [TerminalId]={pos.TerminalId}")
            .SingleOrDefaultAsync(x => x.Status == POSShiftStatus.Open, ct)
            ?? throw new BusinessRuleException("Chưa mở ca tại quầy này.");
        if (shift.OpenedByUserId != pos.UserId) throw new ForbiddenAppException("Chỉ nhân viên phụ trách ca được xử lý tiền cọc.");
        return shift;
    }
    private Task<StoreBankAccount?> DefaultBankAsync(CancellationToken ct)
        => db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.IsActive && x.IsDefault && x.ConfirmMode == BankQrConfirmMode.Manual, ct);
    private async Task<int?> BankAsync(PaymentMethod method, string? reference, CancellationToken ct)
    {
        if (method == PaymentMethod.Cash) return null;
        if (method != PaymentMethod.BankTransfer) throw new BusinessRuleException("Chỉ hỗ trợ tiền mặt hoặc chuyển khoản.");
        if (string.IsNullOrWhiteSpace(reference)) throw new BusinessRuleException("Nhập mã giao dịch đã kiểm tra nhận / chi tiền.");
        return (await DefaultBankAsync(ct))?.Id
            ?? throw new BusinessRuleException("Cần cấu hình ngân hàng mặc định xác nhận thủ công. Không sử dụng ngân hàng thanh toán tự động để nhận / hoàn cọc.");
    }
    private async Task<CustomerDepositEntry?> ReplayAsync(Guid id, string payload, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new BusinessRuleException("Thiếu mã lần giao dịch.");
        await LockAsync($"deposit-request:{StoreId}:{id}", ct);
        var old = await db.Set<CustomerDepositEntry>().IgnoreQueryFilters().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.ClientRequestId == id, ct);
        if (old != null && (old.IsDeleted || old.RequestJson != payload)) throw new ConflictAppException("Mã giao dịch đã được dùng với nội dung khác.");
        return old;
    }
    private void CashEntry(POSShift shift, decimal amount, bool refund, int depositId, int entryId)
    {
        if (refund)
        {
            if (shift.ClosingCashExpected < amount) throw new BusinessRuleException("Tiền mặt trong ca không đủ để hoàn cọc.");
            shift.AddCashOut(amount);
        }
        else shift.AddCashIn(amount);
        db.POSShiftCashTransactions.Add(new() { StoreId = StoreId, POSShiftId = shift.Id,
            Type = refund ? POSShiftCashTransactionType.CashOut : POSShiftCashTransactionType.CashIn,
            Amount = amount, Reason = refund ? "Hoàn cọc khách hàng" : "Nhận cọc khách hàng",
            Note = $"Phiếu cọc DC-{depositId}", CreatedByUserId = pos.UserId!.Value, CustomerDepositEntryId = entryId });
    }
    public async Task<int> ReceiveAsync(ReceiveDepositRequest request, CancellationToken ct)
    {
        ValidateMoney(request.Amount);
        if (string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 500 || request.Note?.Length > 500 || request.Reference?.Length > 100)
            throw new BusinessRuleException("Nhập nội dung đặt hàng tối đa 500 ký tự và mã giao dịch tối đa 100 ký tự.");
        var payload = "Receive:" + JsonSerializer.Serialize(request);
        await using var tx = await uow.BeginTransactionAsync(ct);
        var old = await ReplayAsync(request.ClientRequestId, payload, ct);
        if (old != null) { await tx.CommitAsync(ct); return old.CustomerDepositId; }
        if (!await db.Customers.AnyAsync(x => x.StoreId == StoreId && x.Id == request.CustomerId && x.IsActive, ct))
            throw new BusinessRuleException("Khách hàng không tồn tại hoặc đã khóa.");
        var shift = await ShiftAsync(ct);
        var bankId = await BankAsync(request.Method, request.Reference, ct);
        var deposit = new CustomerDeposit { StoreId = StoreId, CustomerId = request.CustomerId, Balance = request.Amount,
            Purpose = request.Purpose.Trim(), ExpectedDeliveryDate = request.ExpectedDeliveryDate?.Date };
        db.Set<CustomerDeposit>().Add(deposit);
        await db.SaveChangesAsync(ct);
        var entry = new CustomerDepositEntry { StoreId = StoreId, CustomerDepositId = deposit.Id, POSShiftId = shift.Id,
            Kind = "Receive", Amount = request.Amount, Method = request.Method, StoreBankAccountId = bankId,
            Reference = request.Reference?.Trim(), Note = request.Note?.Trim(), ClientRequestId = request.ClientRequestId, RequestJson = payload };
        db.Set<CustomerDepositEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
        if (request.Method == PaymentMethod.Cash) CashEntry(shift, request.Amount, false, deposit.Id, entry.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return deposit.Id;
    }
    public async Task<DepositQrPreviewDto> CreateReceiveQrAsync(CreateDepositQrRequest request, CancellationToken ct)
    {
        ValidateMoney(request.Amount);
        if (request.ClientRequestId == Guid.Empty) throw new BusinessRuleException("Thiếu mã lần nhận cọc.");
        if (!await db.Customers.AnyAsync(x => x.StoreId == StoreId && x.Id == request.CustomerId && x.IsActive, ct))
            throw new BusinessRuleException("Khách hàng không tồn tại hoặc đã khóa.");
        _ = await ShiftAsync(ct);
        var bank = await DefaultBankAsync(ct)
            ?? throw new BusinessRuleException("Cần cấu hình ngân hàng mặc định xác nhận thủ công để tạo QR nhận cọc.");
        var rawContent = $"COC{request.CustomerId}-{request.ClientRequestId:N}";
        var content = rawContent[..Math.Min(40, rawContent.Length)].ToUpperInvariant();
        var payload = vietQr.BuildPayload(bank.VietQrBankBin ?? string.Empty, bank.AccountNumber, request.Amount, content);
        return new DepositQrPreviewDto(bank.BankName, bank.AccountNumber, bank.AccountName, request.Amount,
            content, vietQr.GeneratePngDataUrl(payload));
    }
    public async Task<int> RefundAsync(RefundDepositRequest request, CancellationToken ct)
    {
        ValidateMoney(request.Amount);
        if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Length > 500 || request.Reference?.Length > 100)
            throw new BusinessRuleException("Nhập lý do hoàn cọc tối đa 500 ký tự.");
        var payload = "Refund:" + JsonSerializer.Serialize(request);
        await using var tx = await uow.BeginTransactionAsync(ct);
        var old = await ReplayAsync(request.ClientRequestId, payload, ct);
        if (old != null) { await tx.CommitAsync(ct); return old.Id; }
        await LockAsync($"deposit:{StoreId}:{request.DepositId}", ct);
        var deposit = await db.Set<CustomerDeposit>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == request.DepositId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu cọc.");
        if (request.Amount > deposit.Balance) throw new BusinessRuleException("Số hoàn vượt tiền cọc còn lại.");
        var shift = await ShiftAsync(ct);
        var bankId = await BankAsync(request.Method, request.Reference, ct);
        deposit.Balance -= request.Amount;
        var entry = new CustomerDepositEntry { StoreId = StoreId, CustomerDepositId = deposit.Id, POSShiftId = shift.Id,
            Kind = "Refund", Amount = -request.Amount, Method = request.Method, StoreBankAccountId = bankId,
            Reference = request.Reference?.Trim(), Note = request.Note.Trim(), ClientRequestId = request.ClientRequestId, RequestJson = payload };
        db.Set<CustomerDepositEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
        if (request.Method == PaymentMethod.Cash) CashEntry(shift, request.Amount, true, deposit.Id, entry.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return entry.Id;
    }
    public async Task SelectAsync(int orderId, SelectDepositRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id]={orderId} AND [StoreId]={StoreId}")
            .Include(x => x.Payments).SingleOrDefaultAsync(ct) ?? throw new BusinessRuleException("Không tìm thấy đơn.");
        var shift = await ShiftAsync(ct);
        if (order.Status != OrderStatus.Draft || order.POSShiftId != shift.Id || shift.CurrentOrderId != order.Id || order.CustomerId != request.ExpectedCustomerId)
            throw new ConflictAppException("Giỏ hàng hoặc khách đã thay đổi. Vui lòng tải lại.");
        if (await db.PosPaymentQrRequests.AnyAsync(x => x.OrderId == order.Id && x.Status == PosPaymentQrStatus.Pending, ct))
            throw new BusinessRuleException("Hủy QR đang chờ trước khi thay đổi tiền cọc sử dụng.");
        if (request.DepositId.HasValue)
        {
            ValidateMoney(request.Amount);
            var deposit = await db.Set<CustomerDeposit>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == request.DepositId && x.CustomerId == order.CustomerId, ct);
            var remaining = Math.Max(0, order.GrandTotal - order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount));
            if (deposit == null || request.Amount > deposit.Balance || request.Amount > remaining)
                throw new BusinessRuleException("Số sử dụng vượt tiền cọc hoặc số còn phải thanh toán.");
        }
        else if (request.Amount != 0) throw new BusinessRuleException("Số sử dụng phải bằng 0 khi bỏ chọn cọc.");
        order.CustomerDepositId = request.DepositId;
        order.DepositAmount = request.Amount;
        order.PaidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount) + order.DepositAmount;
        order.BalanceDue = Math.Max(0, order.GrandTotal - order.PaidTotal);
        order.ChangeDue = Math.Max(0, order.PaidTotal - order.GrandTotal);
        order.PaymentStatus = order.BalanceDue == 0 ? PaymentStatus.Paid : order.PaidTotal > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ConsumeAsync(Order order, CancellationToken ct)
    {
        if (order.DepositAmount <= 0) return;
        await LockAsync($"deposit:{StoreId}:{order.CustomerDepositId}", ct);
        var deposit = await db.Set<CustomerDeposit>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == order.CustomerDepositId && x.CustomerId == order.CustomerId, ct);
        if (deposit == null || deposit.Balance < order.DepositAmount)
            throw new ConflictAppException("Tiền cọc đã thay đổi. Vui lòng chọn lại tiền cọc trước khi chốt đơn.");
        if (order.DepositAmount > order.GrandTotal || order.Payments.Where(x => !x.IsDeleted && x.Method != PaymentMethod.Cash).Sum(x => x.Amount) + order.DepositAmount > order.GrandTotal)
            throw new BusinessRuleException("Tiền thanh toán vượt tổng đơn sau khi trừ cọc. Điều chỉnh tiền cọc / tiền thu trước khi chốt.");
        if (await db.PosPaymentQrRequests.AnyAsync(x => x.OrderId == order.Id && x.Status == PosPaymentQrStatus.Pending, ct))
            throw new BusinessRuleException("Hủy hoặc đối soát QR đang chờ trước khi chốt đơn dùng cọc.");
        deposit.Balance -= order.DepositAmount;
        db.Set<CustomerDepositEntry>().Add(new() { StoreId = StoreId, CustomerDepositId = deposit.Id, OrderId = order.Id,
            POSShiftId = order.POSShiftId, Kind = "Apply", Amount = -order.DepositAmount, Note = "Sử dụng cọc khi chốt đơn" });
    }
    public async Task<decimal> GetReturnableAsync(Order order, CancellationToken ct)
    {
        var restored = await db.Set<CustomerDepositEntry>().Where(x => x.StoreId == StoreId && x.OrderId == order.Id && x.Kind == "Return").SumAsync(x => x.Amount, ct);
        var refunded = await db.SalesReturns.Where(x => x.OrderId == order.Id && x.Status == SalesReturnStatus.Completed).SumAsync(x => x.RefundTotal + x.DepositRestoredTotal, ct);
        return Math.Max(0, Math.Min(order.DepositAmount - restored, order.PaidTotal - refunded));
    }
    public async Task RestoreReturnAsync(Order order, SalesReturn salesReturn, CancellationToken ct)
    {
        var amount = salesReturn.DepositRestoredTotal;
        if (amount <= 0) return;
        ValidateMoney(amount);
        if (!order.CustomerDepositId.HasValue || amount > order.DepositAmount)
            throw new BusinessRuleException("Đơn không có đủ tiền cọc để hoàn vào số dư.");
        await LockAsync($"deposit:{StoreId}:{order.CustomerDepositId}", ct);
        var previous = await db.Set<CustomerDepositEntry>().Where(x => x.StoreId == StoreId && x.OrderId == order.Id && x.Kind == "Return").SumAsync(x => x.Amount, ct);
        if (previous + amount > order.DepositAmount) throw new BusinessRuleException("Số hoàn vào cọc vượt phần cọc đã dùng còn có thể hoàn.");
        var deposit = await db.Set<CustomerDeposit>().SingleAsync(x => x.StoreId == StoreId && x.Id == order.CustomerDepositId && x.CustomerId == order.CustomerId, ct);
        deposit.Balance += amount;
        db.Set<CustomerDepositEntry>().Add(new() { StoreId = StoreId, CustomerDepositId = deposit.Id, OrderId = order.Id,
            SalesReturnId = salesReturn.Id, POSShiftId = salesReturn.POSShiftId, Kind = "Return", Amount = amount, Note = "Hoàn vào cọc: " + salesReturn.ReturnNumber });
    }
    public async Task RestoreVoidAsync(Order order, CancellationToken ct)
    {
        if (order.DepositAmount <= 0) return;
        await LockAsync($"deposit:{StoreId}:{order.CustomerDepositId}", ct);
        var deposit = await db.Set<CustomerDeposit>().SingleAsync(x => x.StoreId == StoreId && x.Id == order.CustomerDepositId, ct);
        deposit.Balance += order.DepositAmount;
        db.Set<CustomerDepositEntry>().Add(new() { StoreId = StoreId, CustomerDepositId = deposit.Id, OrderId = order.Id,
            POSShiftId = order.POSShiftId, Kind = "Void", Amount = order.DepositAmount, Note = "Khôi phục cọc do hủy đơn" });
    }
    public Task<List<DepositBalanceDto>> GetAvailableAsync(int customerId, CancellationToken ct)
        => db.Set<CustomerDeposit>().AsNoTracking().Where(x => x.StoreId == StoreId && x.CustomerId == customerId && x.Balance > 0)
            .OrderBy(x => x.Id).Select(x => new DepositBalanceDto(x.Id, x.Purpose, x.Balance, x.ExpectedDeliveryDate)).ToListAsync(ct);
    public async Task<DepositPageDto> GetAsync(int? customerId, CancellationToken ct)
    {
        var bank = await DefaultBankAsync(ct);
        var page = new DepositPageDto { CustomerId = customerId, DefaultBank = bank == null ? null : bank.BankName + " · " + bank.AccountNumber };
        if (!customerId.HasValue) return page;
        page.CustomerName = await db.Customers.Where(x => x.StoreId == StoreId && x.Id == customerId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Không tìm thấy khách hàng.");
        page.Deposits = await GetAvailableAsync(customerId.Value, ct);
        page.Journal = await (from e in db.Set<CustomerDepositEntry>().AsNoTracking()
            join d in db.Set<CustomerDeposit>() on e.CustomerDepositId equals d.Id
            join b in db.StoreBankAccounts on e.StoreBankAccountId equals b.Id into banks
            from b in banks.DefaultIfEmpty()
            where e.StoreId == StoreId && d.CustomerId == customerId
            orderby e.Id descending
            select new DepositJournalDto(e.Id, d.Id, e.CreatedAtUtc, e.Kind, e.Amount, e.Method,
                b == null ? null : b.BankName + " · " + b.AccountNumber, e.Reference, e.Note, e.OrderId, e.POSShiftId, e.CreatedBy)).Take(500).ToListAsync(ct);
        return page;
    }
}
