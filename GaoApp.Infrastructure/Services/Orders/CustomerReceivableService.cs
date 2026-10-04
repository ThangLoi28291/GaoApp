using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Orders;

public sealed partial class CustomerReceivableService(AppDbContext db, IAppUnitOfWork uow, ICurrentPOSContext pos) : ICustomerReceivableService
{
    private int StoreId => db.CurrentStoreId ?? throw new BusinessRuleException("Chưa chọn cửa hàng.");

    public Task<decimal> GetBalanceAsync(int customerId, CancellationToken ct)
        => db.Set<CustomerReceivableEntry>().Where(x => x.StoreId == StoreId && x.CustomerId == customerId).SumAsync(x => x.Amount, ct);



    public async Task LockOrderAsync(int orderId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Receivable posting requires a transaction.");
        await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id]={orderId} AND [StoreId]={StoreId}")
            .AsNoTracking().Select(x => x.Id).FirstOrDefaultAsync(ct);
    }

    public async Task ValidateCreditAsync(Order order, FinalizeCreditRequest request, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || order.CustomerId != request.ExpectedCustomerId || request.ExpectedBalance != order.BalanceDue)
            throw new ConflictAppException("Khách hàng hoặc số còn thiếu đã thay đổi. Vui lòng kiểm tra lại đơn.");
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == order.CustomerId && x.StoreId == StoreId, ct);
        if (customer is not { IsActive: true, HaveDebt: true })
            throw new BusinessRuleException("Khách đang chọn chưa được phép công nợ.");
        if (order.BalanceDue <= 0) throw new BusinessRuleException("Đơn đã thanh toán đủ, không cần ghi nợ.");
        if (request.DueDate.HasValue && (request.DueDate.Value.Date < DateTime.UtcNow.AddHours(7).Date || request.DueDate.Value.Date > DateTime.UtcNow.AddYears(10).Date))
            throw new BusinessRuleException("Ngày hẹn trả không hợp lệ.");
        if (request.Note?.Length > 500) throw new BusinessRuleException("Ghi chú tối đa 500 ký tự.");
        // Pending bank callbacks must not settle a just-completed credit sale invisibly.
        if (await db.PosPaymentQrRequests.AnyAsync(x => x.OrderId == order.Id && x.Status == PosPaymentQrStatus.Pending, ct))
            throw new BusinessRuleException("Đơn còn QR chờ thanh toán. Hủy hoặc đối soát QR trước khi ghi nợ.");
        order.IsCreditSale = true;
        order.CreditInitialBalance = order.BalanceDue;
        order.CreditDueDate = request.DueDate?.Date;
        order.CreditRequestId = request.ClientRequestId;
        order.CreditNote = request.Note?.Trim();
    }

    public Task PostSaleAsync(Order order, CancellationToken ct)
    {
        if (order.IsCreditSale && order.CustomerId.HasValue && order.BalanceDue > 0)
            db.Set<CustomerReceivableEntry>().Add(new() { StoreId = order.StoreId, CustomerId = order.CustomerId.Value,
                OrderId = order.Id, Kind = "Sale", Amount = order.BalanceDue, Note = order.CreditNote });
        return Task.CompletedTask;
    }

    public async Task PostReturnAsync(Order order, SalesReturn salesReturn, CancellationToken ct)
    {
        if (!order.IsCreditSale) return;
        var reduction = Math.Min(order.BalanceDue, salesReturn.ReturnSubtotal);
        var cashRefund = salesReturn.ReturnSubtotal - reduction;
        if (salesReturn.RefundTotal + salesReturn.DepositRestoredTotal != cashRefund)
            throw new BusinessRuleException($"Đơn công nợ: trừ nợ {reduction:N0}đ và hoàn tiền {cashRefund:N0}đ. Vui lòng nhập đúng số hoàn.");
        if (reduction > 0)
        {
            db.Set<CustomerReceivableEntry>().Add(new() { StoreId = order.StoreId, CustomerId = order.CustomerId!.Value,
                OrderId = order.Id, SalesReturnId = salesReturn.Id, Kind = "Return", Amount = -reduction, Note = salesReturn.Reason });
            order.BalanceDue -= reduction;
            order.PaymentStatus = order.BalanceDue > 0 ? (order.PaidTotal > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid) : PaymentStatus.Paid;
        }
        await Task.CompletedTask;
    }

    public async Task VoidAsync(Order order, CancellationToken ct)
    {
        if (!order.IsCreditSale) return;
        if (await db.Set<CustomerReceivableEntry>().AnyAsync(x => x.OrderId == order.Id && x.ReceiptId != null, ct))
            throw new BusinessRuleException("Đơn đã thu nợ. Dùng Trả hàng / hoàn tiền để lập chứng từ hoàn tại ca hiện tại.");
        if (order.BalanceDue > 0)
            db.Set<CustomerReceivableEntry>().Add(new() { StoreId = order.StoreId, CustomerId = order.CustomerId!.Value,
                OrderId = order.Id, Kind = "Void", Amount = -order.BalanceDue, Note = "Hủy đơn bán nợ" });
        order.BalanceDue = 0;
    }

    public async Task<int> CollectAsync(CollectCustomerDebtRequest request, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.Amount <= 0 || decimal.Truncate(request.Amount) != request.Amount || request.Amount >= 10000000000000000m)
            throw new BusinessRuleException("Thiếu mã lần thu hoặc số tiền thu không hợp lệ.");
        if (request.Method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
            throw new BusinessRuleException("Thu nợ chỉ hỗ trợ tiền mặt hoặc chuyển khoản.");
        if (request.Note?.Length > 500 || request.Reference?.Length > 100)
            throw new BusinessRuleException("Ghi chú hoặc mã tham chiếu quá dài.");
        var payload = JsonSerializer.Serialize(request);
        await using var tx = await uow.BeginTransactionAsync(ct);
        var resource = $"customer-debt:{StoreId}:{request.CustomerId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r<0 THROW 51000,'Customer debt is busy.',1;", ct);
        var existing = await db.Set<CustomerDebtReceipt>().IgnoreQueryFilters().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.ClientRequestId == request.ClientRequestId, ct);
        if (existing != null)
        {
            if (existing.RequestJson != payload || existing.IsDeleted) throw new ConflictAppException("Mã lần thu đã được dùng với nội dung khác.");
            await tx.CommitAsync(ct);
            return existing.Id;
        }
        if (!pos.IsAvailable || pos.StoreId != StoreId || !pos.UserId.HasValue)
            throw new BusinessRuleException("Cần đăng nhập quầy và mở ca để thu nợ.");
        var shift = await db.POSShifts.FromSqlInterpolated($"SELECT * FROM [POSShifts] WITH (UPDLOCK, ROWLOCK) WHERE [StoreId]={StoreId} AND [TerminalId]={pos.TerminalId}")
            .SingleOrDefaultAsync(x => x.Status == POSShiftStatus.Open, ct)
            ?? throw new BusinessRuleException("Chưa có ca đang mở tại quầy này.");
        if (shift.OpenedByUserId != pos.UserId) throw new ForbiddenAppException("Chỉ người đang phụ trách ca được thu nợ tại ca này.");
        if (request.Method == PaymentMethod.BankTransfer)
        {
            if (string.IsNullOrWhiteSpace(request.Reference) || !await db.StoreBankAccounts.AnyAsync(x => x.Id == request.StoreBankAccountId && x.IsActive && x.StoreId == StoreId, ct))
                throw new BusinessRuleException("Chọn tài khoản nhận đang hoạt động và nhập mã giao dịch đã đối soát.");
        }
        else if (request.StoreBankAccountId != null) throw new BusinessRuleException("Thu tiền mặt không dùng tài khoản ngân hàng.");
        var ids = await db.Orders.Where(x => x.StoreId == StoreId && x.CustomerId == request.CustomerId && x.IsCreditSale && x.Status == OrderStatus.Completed && x.BalanceDue > 0 && (!request.OrderId.HasValue || x.Id == request.OrderId))
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids) await LockOrderAsync(id, ct);
        var orders = await db.Orders.Where(x => ids.Contains(x.Id) && x.Status == OrderStatus.Completed && x.BalanceDue > 0)
            .OrderBy(x => x.CreditDueDate).ThenBy(x => x.Id).ToListAsync(ct);
        if (request.Amount > orders.Sum(x => x.BalanceDue)) throw new BusinessRuleException("Số tiền thu vượt công nợ còn lại. Vui lòng tải lại sổ công nợ.");
        var receipt = new CustomerDebtReceipt { StoreId = StoreId, CustomerId = request.CustomerId, POSShiftId = shift.Id,
            ClientRequestId = request.ClientRequestId, Amount = request.Amount, Method = request.Method, StoreBankAccountId = request.StoreBankAccountId,
            Reference = request.Reference?.Trim(), Note = request.Note?.Trim(), RequestJson = payload };
        db.Set<CustomerDebtReceipt>().Add(receipt);
        var remaining = request.Amount;
        foreach (var order in orders)
        {
            var amount = Math.Min(remaining, order.BalanceDue);
            if (amount <= 0) break;
            receipt.Entries.Add(new() { StoreId = StoreId, CustomerId = request.CustomerId, OrderId = order.Id, Kind = "Collection", Amount = -amount, Note = receipt.Note });
            // Real money only. Provider marks collections so original-sale shift reconciliation excludes them.
            db.OrderPayments.Add(new() { StoreId = StoreId, OrderId = order.Id, Method = request.Method, Amount = amount,
                IsDebtCollection = true, Provider = "CUSTOMER_DEBT", ReferenceCode = request.ClientRequestId.ToString(), PaidAtUtc = DateTime.UtcNow });
            order.PaidTotal += amount;
            order.BalanceDue -= amount;
            order.PaymentStatus = order.BalanceDue == 0 ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
            remaining -= amount;
        }
        if (request.Method == PaymentMethod.Cash)
        {
            shift.AddCashIn(request.Amount);
            db.POSShiftCashTransactions.Add(new() { StoreId = StoreId, POSShiftId = shift.Id, Type = POSShiftCashTransactionType.CashIn,
                Amount = request.Amount, Reason = "Thu công nợ khách hàng", Note = $"Khách #{request.CustomerId}; mã thu {request.ClientRequestId}", CreatedByUserId = pos.UserId.Value });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return receipt.Id;
    }

    public async Task<ReceivablePageDto> GetAsync(int? customerId, string? search, CancellationToken ct, int pageNumber = 1, string? status = null)
    {
        var storeId = StoreId;
        var today = DateTime.UtcNow.AddHours(7).Date;
        var debts = db.Orders.AsNoTracking().Where(x => x.StoreId == storeId && x.IsCreditSale && x.Status == OrderStatus.Completed);
        var customers = db.Customers.AsNoTracking().Where(x => x.StoreId == storeId);
        if (status is not (null or "" or "open" or "settled" or "overdue")) throw new BusinessRuleException("Trạng thái công nợ không hợp lệ.");
        var selectedName = customerId.HasValue ? await customers.Where(x => x.Id == customerId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Không tìm thấy khách hàng tại cửa hàng này.") : null;
        search = search?.Trim();
        if (!string.IsNullOrWhiteSpace(search)) customers = customers.Where(x => x.Name.Contains(search) || (x.Phone != null && x.Phone.Contains(search)) || (x.Code != null && x.Code.Contains(search)));
        var page = new ReceivablePageDto { CustomerId = customerId };
        var customerRows = customers.Where(c => db.Set<CustomerReceivableEntry>().Any(e => e.StoreId == storeId && e.CustomerId == c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone,
                Balance = debts.Where(o => o.CustomerId == c.Id).Sum(o => o.BalanceDue),
                Overdue = debts.Where(o => o.CustomerId == c.Id && o.CreditDueDate < today).Sum(o => o.BalanceDue) });
        if (status == "open") customerRows = customerRows.Where(x => x.Balance > 0);
        if (status == "settled") customerRows = customerRows.Where(x => x.Balance == 0);
        if (status == "overdue") customerRows = customerRows.Where(x => x.Overdue > 0);
        page.TotalCustomers = await customerRows.CountAsync(ct);
        page.OpenCustomers = await customerRows.CountAsync(x => x.Balance > 0, ct);
        page.SettledCustomers = await customerRows.CountAsync(x => x.Balance == 0, ct);
        var matchingDebts = debts.Where(x => customerRows.Select(c => (int?)c.Id).Contains(x.CustomerId));
        page.TotalBalance = await matchingDebts.SumAsync(x => x.BalanceDue, ct);
        page.TotalOverdue = await matchingDebts.Where(x => x.CreditDueDate < today).SumAsync(x => x.BalanceDue, ct);
        page.Page = Math.Clamp(pageNumber, 1, Math.Max(1, (page.TotalCustomers + page.PageSize - 1) / page.PageSize));
        page.Customers = await customerRows.OrderByDescending(x => x.Balance).ThenBy(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(x => new ReceivableCustomerDto(x.Id, x.Name, x.Phone, x.Balance, x.Overdue)).ToListAsync(ct);
        if (!customerId.HasValue) return page;
        page.CustomerName = selectedName;
        page.Orders = await debts.Where(x => x.CustomerId == customerId).OrderBy(x => x.CreditDueDate)
            .Select(x => new ReceivableOrderDto(x.Id, x.OrderNumber, x.GrandTotal, x.PaidTotal, x.BalanceDue, x.CreditDueDate)).ToListAsync(ct);
        page.Journal = await (from e in db.Set<CustomerReceivableEntry>().AsNoTracking()
            join o in db.Orders on e.OrderId equals o.Id
            where e.StoreId == storeId && e.CustomerId == customerId
            orderby e.Id descending
            select new ReceivableJournalDto(e.Id, e.CreatedAtUtc, e.Kind, o.OrderNumber, e.ReceiptId, e.Amount, e.Note, e.CreatedBy)).Take(500).ToListAsync(ct);
        page.Receipts = await (from r in db.Set<CustomerDebtReceipt>().AsNoTracking()
            join b in db.StoreBankAccounts on r.StoreBankAccountId equals b.Id into banks
            from b in banks.DefaultIfEmpty()
            where r.StoreId == storeId && r.CustomerId == customerId
            orderby r.Id descending
            select new DebtReceiptDto(r.Id, r.CreatedAtUtc, r.Amount, r.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản",
                b == null ? null : b.BankName + " · " + b.AccountNumber, r.Reference, r.Note, r.CreatedBy, r.POSShiftId)).Take(200).ToListAsync(ct);
        page.Banks = await db.StoreBankAccounts.Where(x => x.StoreId == storeId && x.IsActive)
            .Select(x => new DebtBankDto(x.Id, x.BankName + " · " + x.AccountNumber)).ToListAsync(ct);
        page.Refunds = await (from p in db.SalesReturnPayments.AsNoTracking()
            join r in db.SalesReturns on p.SalesReturnId equals r.Id
            join o in db.Orders on r.OrderId equals o.Id
            where p.StoreId == storeId && o.CustomerId == customerId && o.IsCreditSale && r.Status == SalesReturnStatus.Completed
            orderby p.Id descending
            select new DebtRefundDto(r.ReturnNumber, p.PaidAtUtc, p.Amount,
                p.Method == PaymentMethod.Cash ? "Tiền mặt" : "Không tiền mặt", p.ReferenceCode, r.POSShiftId)).Take(200).ToListAsync(ct);
        return page;
    }
}
