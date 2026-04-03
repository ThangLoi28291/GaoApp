using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.POSShifts;

public class POSShiftService : IPOSShiftService
{
    private readonly IPOSShiftRepository _shiftRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly IWarehouseRepository _warehouseRepo;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public POSShiftService(
        IPOSShiftRepository shiftRepo,
        IOrderRepository orderRepo,
        IWarehouseRepository warehouseRepo,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _shiftRepo = shiftRepo;
        _orderRepo = orderRepo;
        _warehouseRepo = warehouseRepo;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    private static bool HasMeaningfulWork(Order order)
    {
        var hasLines = order.Lines.Any(x => !x.IsDeleted);
        var hasPayments = order.Payments.Any(x => !x.IsDeleted);
        return hasLines || hasPayments;
    }

    private static bool IsEmptyDraft(Order order)
        => order.Status == OrderStatus.Draft && !HasMeaningfulWork(order);

    public async Task<POSShiftDto?> GetCurrentOpenAsync(CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        return shift == null ? null : Map(shift);
    }

    public async Task<POSShiftDto> OpenAsync(OpenShiftRequest req, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var userId = RequireUserId();
        var terminalId = RequireTerminalId();

        var open = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        if (open != null)
            throw new InvalidOperationException("Terminal hiện tại đang có ca mở. Vui lòng đóng ca trước.");

        if (req.WarehouseId <= 0)
            throw new InvalidOperationException("Vui lòng chọn kho xuất bán cho ca POS.");

        var warehouse = await _warehouseRepo.GetByIdAsync(req.WarehouseId, ct);
        if (warehouse == null)
            throw new InvalidOperationException("Kho được chọn không tồn tại.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho được chọn đã ngưng hoạt động.");

        var shift = new POSShift
        {
            StoreId = storeId,
            TerminalId = terminalId,
            OpenedByUserId = userId,
            OpenedAtUtc = DateTime.UtcNow,
            Status = POSShiftStatus.Open,
            ShiftCode = $"SHIFT-{DateTime.Now:yyyyMMddHHmmss}",
            OpeningCash = req.OpeningCash,
            OpenNote = req.Note,
            WarehouseId = warehouse.Id
        };

        shift.RecalcExpected();

        await _shiftRepo.AddAsync(shift, ct);
        await _shiftRepo.SaveChangesAsync(ct);

        shift.Warehouse = warehouse;

        return Map(shift);
    }

    public async Task<POSShiftDto> CloseAsync(CloseShiftRequest req, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();
        var userId = RequireUserId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct)
            ?? throw new InvalidOperationException("Không có ca nào đang mở để đóng.");

        var orders = await _orderRepo.GetByShiftIdAsync(shift.Id, ct);

        var heldOrders = orders.Where(x => x.Status == OrderStatus.OnHold).ToList();
        if (heldOrders.Any())
            throw new InvalidOperationException($"Không thể đóng ca vì còn {heldOrders.Count} đơn đang giữ.");

        var draftOrdersWithData = orders
            .Where(x => x.Status == OrderStatus.Draft &&
                        (x.Lines.Any(l => !l.IsDeleted) || x.Payments.Any(p => !p.IsDeleted)))
            .ToList();

        if (draftOrdersWithData.Any())
            throw new InvalidOperationException($"Không thể đóng ca vì còn {draftOrdersWithData.Count} giỏ nháp có dữ liệu.");

        shift.Close(userId, req.ClosingCashActual, req.Note);

        await _shiftRepo.SaveChangesAsync(ct);

        return Map(shift);
    }

    public async Task<POSShiftCashTransactionDto> AddCashTransactionAsync(
        CreatePosShiftCashTransactionRequest dto,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();
        var userId = RequireUserId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct)
            ?? throw new InvalidOperationException("Không có ca POS đang mở.");

        if (dto.Amount <= 0)
            throw new InvalidOperationException("Số tiền phải > 0.");

        var transaction = new POSShiftCashTransaction
        {
            StoreId = storeId,
            POSShiftId = shift.Id,
            Type = dto.Type,
            Amount = dto.Amount,
            Reason = dto.Reason?.Trim() ?? throw new InvalidOperationException("Lý do không được để trống."),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (dto.Type == POSShiftCashTransactionType.CashIn)
            shift.AddCashIn(dto.Amount);
        else
            shift.AddCashOut(dto.Amount);

        await _shiftRepo.AddCashTransactionAsync(transaction, ct);
        await _shiftRepo.SaveChangesAsync(ct);

        return new POSShiftCashTransactionDto
        {
            Id = transaction.Id,
            POSShiftId = transaction.POSShiftId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount,
            Reason = transaction.Reason,
            Note = transaction.Note,
            CreatedAtUtc = transaction.CreatedAtUtc
        };
    }

    public async Task<List<POSShiftCashTransactionDto>> GetCashTransactionsAsync(CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct)
            ?? throw new InvalidOperationException("Không có ca POS đang mở.");

        var items = await _shiftRepo.GetCashTransactionsByShiftIdAsync(shift.Id, ct);

        return items.Select(x => new POSShiftCashTransactionDto
        {
            Id = x.Id,
            POSShiftId = x.POSShiftId,
            Type = x.Type.ToString(),
            Amount = x.Amount,
            Reason = x.Reason,
            Note = x.Note,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();
    }

    public async Task<POSShiftSummaryDto> GetSummaryAsync(int? shiftId = null, CancellationToken ct = default)
    {
        POSShift? shift;

        if (shiftId.HasValue)
        {
            shift = await _shiftRepo.GetByIdAsync(shiftId.Value, ct);
        }
        else
        {
            var storeId = _currentStore.StoreId;
            var terminalId = RequireTerminalId();
            shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        }

        if (shift == null)
            throw new InvalidOperationException("Không tìm thấy ca POS.");

        var orders = await _orderRepo.GetByShiftIdAsync(shift.Id, ct);
        var cashTransactions = await _shiftRepo.GetCashTransactionsByShiftIdAsync(shift.Id, ct);

        shift.RecalcExpected();

        return new POSShiftSummaryDto
        {
            Id = shift.Id,
            ShiftCode = shift.ShiftCode,
            Status = shift.Status,
            OpenedByUserId = shift.OpenedByUserId,
            OpenedAtUtc = shift.OpenedAtUtc,
            ClosedByUserId = shift.ClosedByUserId,
            ClosedAtUtc = shift.ClosedAtUtc,

            OpeningCash = shift.OpeningCash,
            CashSalesTotal = shift.CashSalesTotal,
            NonCashSalesTotal = shift.NonCashSalesTotal,

            CashRefundTotal = shift.CashRefundTotal,
            NonCashRefundTotal = shift.NonCashRefundTotal,
            RefundTotal = shift.RefundTotal,
            RefundCount = shift.RefundCount,
            VoidCount = shift.VoidCount,
            CashInTotal = shift.CashInTotal,
            CashOutTotal = shift.CashOutTotal,
            ClosingCashExpected = shift.ClosingCashExpected,
            ClosingCashActual = shift.ClosingCashActual,

            OpenNote = shift.OpenNote,
            CloseNote = shift.CloseNote,

            TotalOrders = orders.Count,
            DraftOrders = orders.Count(x => x.Status == OrderStatus.Draft),
            CompletedOrders = orders.Count(x => x.Status == OrderStatus.Completed),
            CancelledOrders = orders.Count(x => x.Status == OrderStatus.Cancelled),

            CompletedSalesTotal = orders
                .Where(x => x.Status == OrderStatus.Completed)
                .Sum(x => x.GrandTotal),

            Orders = orders.Select(x => new POSShiftSummaryOrderDto
            {
                Id = x.Id,
                OrderNumber = x.OrderNumber,
                Status = x.Status,
                PaymentStatus = x.PaymentStatus,
                GrandTotal = x.GrandTotal,
                CreatedAtUtc = x.CreatedAtUtc,
                CompletedAtUtc = x.CompletedAtUtc
            }).ToList(),

            CashTransactions = cashTransactions.Select(x => new POSShiftCashTransactionDto
            {
                Id = x.Id,
                POSShiftId = x.POSShiftId,
                Type = x.Type.ToString(),
                Amount = x.Amount,
                Reason = x.Reason,
                Note = x.Note,
                CreatedAtUtc = x.CreatedAtUtc
            }).ToList()
        };
    }

    public async Task<PagedResult<POSShiftHistoryItemDto>> QueryHistoryAsync(
        QueryPOSShiftHistoryRequest req,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var page = req.Page <= 0 ? 1 : req.Page;
        var pageSize = req.PageSize <= 0 ? 20 : req.PageSize;

        var (items, total) = await _shiftRepo.QueryHistoryAsync(
            storeId,
            req.UserId,
            req.TerminalId,
            req.Status,
            req.FromUtc,
            req.ToUtcExclusive,
            page,
            pageSize,
            ct);

        var mapped = items.Select(x => new POSShiftHistoryItemDto
        {
            Id = x.Id,
            ShiftCode = x.ShiftCode,
            Status = x.Status,
            OpenedAtUtc = x.OpenedAtUtc,
            ClosedAtUtc = x.ClosedAtUtc,
            OpeningCash = x.OpeningCash,
            CashSalesTotal = x.CashSalesTotal,
            NonCashSalesTotal = x.NonCashSalesTotal,
            CashInTotal = x.CashInTotal,
            CashOutTotal = x.CashOutTotal,
            ClosingCashExpected = x.ClosingCashExpected,
            ClosingCashActual = x.ClosingCashActual,
            OpenNote = x.OpenNote,
            CloseNote = x.CloseNote
        }).ToList();

        return new PagedResult<POSShiftHistoryItemDto>(page, pageSize, total, mapped);
    }

    private int RequireUserId()
        => _currentUser.UserId ?? throw new InvalidOperationException("Phiên đăng nhập không hợp lệ.");

    private int RequireTerminalId()
        => _currentUser.TerminalId ?? throw new InvalidOperationException("Không xác định được terminal hiện tại.");

    private static POSShiftDto Map(POSShift x) => new()
    {
        Id = x.Id,
        ShiftCode = x.ShiftCode,
        Status = x.Status,
        OpenedByUserId = x.OpenedByUserId,
        OpenedAtUtc = x.OpenedAtUtc,

        OpeningCash = x.OpeningCash,
        CashSalesTotal = x.CashSalesTotal,
        NonCashSalesTotal = x.NonCashSalesTotal,

        CashRefundTotal = x.CashRefundTotal,
        NonCashRefundTotal = x.NonCashRefundTotal,
        RefundTotal = x.RefundTotal,
        RefundCount = x.RefundCount,
        VoidCount = x.VoidCount,
        CashInTotal = x.CashInTotal,
        CashOutTotal = x.CashOutTotal,

        ClosingCashExpected = x.ClosingCashExpected,
        ClosingCashActual = x.ClosingCashActual,

        ClosedByUserId = x.ClosedByUserId,
        ClosedAtUtc = x.ClosedAtUtc,

        OpenNote = x.OpenNote,
        CloseNote = x.CloseNote,

        WarehouseId = x.WarehouseId,
        WarehouseCode = x.Warehouse?.Code,
        WarehouseName = x.Warehouse?.Name
    };
}