using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.POSShiftDashboards;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Text.Json;

namespace GaoApp.Application.Services.POSShifts;

public class POSShiftService : IPOSShiftService
{
    private readonly IPOSShiftRepository _shiftRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly IWarehouseRepository _warehouseRepo;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    // Ghi audit log cho các thao tác quan trọng của ca POS:
    // tiếp quản ca, đóng hộ ca, sau này có thể mở rộng mở/đóng ca.
    private readonly IAuditLogService _auditLogService;
    private readonly IPOSShiftHandoverSlipRepository _handoverSlipRepo;
    private readonly IPOSShiftClosingSlipService _closingSlipService;


    public POSShiftService(
   IPOSShiftRepository shiftRepo,
   IOrderRepository orderRepo,
   IWarehouseRepository warehouseRepo,
   ICurrentStore currentStore,
   ICurrentUser currentUser,
   IUserRepository users,
   IAuditLogService auditLogService,
   IPOSShiftHandoverSlipRepository handoverSlipRepo,
   IPOSShiftClosingSlipService closingSlipService)
    {
        _shiftRepo = shiftRepo;
        _orderRepo = orderRepo;
        _warehouseRepo = warehouseRepo;
        _currentStore = currentStore;
        _currentUser = currentUser;
        _users = users;
        _auditLogService = auditLogService;
        _handoverSlipRepo = handoverSlipRepo;
        _closingSlipService = closingSlipService;
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

        if (shift == null)
            return null;

        EnsureShiftOwner(shift);

        return Map(shift);
    }

    public async Task<POSShiftDto> OpenAsync(OpenShiftRequest req, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var userId = RequireUserId();
        var terminalId = RequireTerminalId();

        var open = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        if (open != null)
        {
            EnsureShiftOwner(open);

            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftAlreadyOpen,
                message: "Terminal này đã có ca POS đang mở.",
                actionHint: "Vui lòng vào ca hiện tại hoặc đóng ca trước khi mở ca mới.",
                metadata: new
                {
                    open.Id,
                    open.ShiftCode,
                    open.OpenedByUserId,
                    open.OpenedAtUtc,
                    TerminalId = terminalId
                });
        }

        POSShiftHandoverSlip? handoverSlip = null;

        // =====================================================
        // Nếu mở ca từ phiếu bàn giao:
        // - Ưu tiên HandoverSlipId
        // - Nếu không có Id thì tìm theo BarcodeValue
        // - Chỉ chấp nhận phiếu Draft / Printed
        // =====================================================
        if (req.HandoverSlipId.HasValue && req.HandoverSlipId.Value > 0)
        {
            handoverSlip = await _handoverSlipRepo.GetUsableByIdAsync(
                storeId,
                req.HandoverSlipId.Value,
                ct);
        }
        else if (!string.IsNullOrWhiteSpace(req.HandoverBarcodeValue))
        {
            handoverSlip = await _handoverSlipRepo.GetUsableByBarcodeAsync(
                storeId,
                req.HandoverBarcodeValue.Trim(),
                ct);
        }

        if ((req.HandoverSlipId.HasValue || !string.IsNullOrWhiteSpace(req.HandoverBarcodeValue))
            && handoverSlip == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftAlreadyOpen,
                message: "Phiếu nhận ca không hợp lệ hoặc đã được sử dụng.",
                actionHint: "Vui lòng kiểm tra lại mã phiếu, hoặc liên hệ quản lý để lập phiếu mới.");
        }

        if (handoverSlip != null)
        {
            if (!IsHandoverSlipUsable(handoverSlip))
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.ShiftAlreadyOpen,
                    message: $"Phiếu {handoverSlip.SlipCode} không còn hiệu lực.",
                    actionHint: "Phiếu có thể đã được dùng hoặc đã bị hủy.");
            }

            // Nếu phiếu có gắn terminal thì chỉ terminal đó được nhận ca.
            if (handoverSlip.TerminalId.HasValue &&
                handoverSlip.TerminalId.Value != terminalId)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.ShiftAlreadyOpen,
                    message: "Phiếu nhận ca không thuộc terminal hiện tại.",
                    actionHint: "Vui lòng kiểm tra lại terminal hoặc liên hệ quản lý.",
                    metadata: new
                    {
                        handoverSlip.Id,
                        handoverSlip.SlipCode,
                        SlipTerminalId = handoverSlip.TerminalId,
                        CurrentTerminalId = terminalId
                    });
            }

            // Nếu phiếu chỉ định nhân viên thì chỉ nhân viên đó được nhận ca.
            if (handoverSlip.AssignedToUserId.HasValue &&
                handoverSlip.AssignedToUserId.Value != userId)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.ShiftAlreadyOpen,
                    message: "Phiếu nhận ca không được cấp cho tài khoản hiện tại.",
                    actionHint: "Vui lòng đăng nhập đúng nhân viên nhận ca hoặc liên hệ quản lý.",
                    metadata: new
                    {
                        handoverSlip.Id,
                        handoverSlip.SlipCode,
                        handoverSlip.AssignedToUserId,
                        CurrentUserId = userId
                    });
            }

            // Dữ liệu trên phiếu là nguồn chính.
            req.WarehouseId = handoverSlip.WarehouseId;
            req.OpeningCash = handoverSlip.OpeningCashTotal;
            req.Denominations = BuildDenominationsFromSlip(handoverSlip);

            var slipNote = $"[NHẬN CA TỪ PHIẾU {handoverSlip.SlipCode}]";
            req.Note = string.IsNullOrWhiteSpace(req.Note)
                ? slipNote
                : $"{slipNote} {req.Note.Trim()}";
        }

        if (req.WarehouseId <= 0)
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftOpenWarehouseRequired,
                message: "Bạn chưa chọn kho xuất bán cho ca POS.",
                actionHint: "Vui lòng chọn kho rồi thử mở ca lại.");
        }

        var warehouse = await _warehouseRepo.GetByIdAsync(req.WarehouseId, ct);
        if (warehouse == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftOpenWarehouseNotFound,
                message: "Kho được chọn không còn tồn tại.",
                actionHint: "Vui lòng tải lại danh sách kho và chọn lại.",
                metadata: new
                {
                    WarehouseId = req.WarehouseId
                });
        }

        if (!warehouse.IsActive)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftOpenWarehouseInactive,
                message: "Kho được chọn đã ngưng hoạt động.",
                actionHint: "Vui lòng chọn kho khác hoặc liên hệ quản lý để kiểm tra cấu hình.",
                metadata: new
                {
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name
                });
        }

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

        var denomItems = (req.Denominations ?? new List<POSShiftDenominationRequest>())
            .Where(x => x.DenominationValue > 0 && x.Quantity > 0)
            .GroupBy(x => x.DenominationValue)
            .Select(g =>
            {
                var quantity = g.Sum(x => x.Quantity);

                var item = new POSShiftCashDenomination
                {
                    StoreId = storeId,
                    POSShift = shift,
                    EntryType = POSShiftCashDenominationEntryType.Opening,
                    DenominationValue = g.Key,
                    Quantity = quantity
                };

                item.Recalc();

                return item;
            })
            .ToList();

        foreach (var item in denomItems)
        {
            shift.CashDenominations.Add(item);
        }

        await _shiftRepo.AddAsync(shift, ct);
        await _shiftRepo.SaveChangesAsync(ct);

        // Sau khi ca đã có Id thì đánh dấu phiếu đã dùng.
        if (handoverSlip != null)
        {
            handoverSlip.MarkUsed(shift.Id, userId);
            await _handoverSlipRepo.SaveChangesAsync(ct);
        }

        shift.Warehouse = warehouse;

        return Map(shift);
    }

    public async Task<POSShiftDto> CloseAsync(CloseShiftRequest req, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();
        var userId = RequireUserId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Terminal này chưa mở ca POS.",
                actionHint: "Vui lòng mở ca trước khi thực hiện thao tác này.",
                metadata: new
                {
                    TerminalId = terminalId
                });
        }
        EnsureShiftOwner(shift);

        var orders = await _orderRepo.GetByShiftIdAsync(shift.Id, ct);

        var heldOrders = orders.Where(x => x.Status == OrderStatus.OnHold).ToList();
        if (heldOrders.Any())
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftCloseBlockedHeldOrders,
                message: "Không thể đóng ca vì còn đơn đang giữ.",
                actionHint: "Hãy xử lý hết các đơn giữ của ca hiện tại trước khi đóng ca.",
                metadata: new
                {
                    shift.Id,
                    shift.ShiftCode,
                    TerminalId = terminalId,
                    HeldOrderCount = heldOrders.Count,
                    HeldOrderIds = heldOrders.Select(x => x.Id).ToList(),
                    HeldOrderNumbers = heldOrders.Select(x => x.OrderNumber).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                });
        }

        var draftOrdersWithData = orders
            .Where(x => x.Status == OrderStatus.Draft &&
                        (x.Lines.Any(l => !l.IsDeleted) || x.Payments.Any(p => !p.IsDeleted)))
            .ToList();

        if (draftOrdersWithData.Any())
        {
            var currentDraft = draftOrdersWithData.FirstOrDefault();

            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftCloseBlockedActiveDrafts,
                message: "Không thể đóng ca vì còn giỏ chưa xử lý.",
                actionHint: "Hãy hoàn tất đơn, giữ đơn hoặc hủy giỏ hiện tại trước khi đóng ca.",
                metadata: new
                {
                    shift.Id,
                    shift.ShiftCode,
                    TerminalId = terminalId,
                    DraftCount = draftOrdersWithData.Count,
                    CurrentDraftId = currentDraft?.Id,
                    CurrentDraftOrderNumber = currentDraft?.OrderNumber,
                    CurrentDraftHasLines = currentDraft != null && currentDraft.Lines.Any(l => !l.IsDeleted),
                    CurrentDraftHasPayments = currentDraft != null && currentDraft.Payments.Any(p => !p.IsDeleted)
                });
        }

        // =====================================================
        // Lưu chi tiết số tờ theo mệnh giá lúc đóng ca.
        // Dùng để in phiếu bàn giao cuối ca và đối chiếu sau này.
        // =====================================================
        var closingDenomItems = (req.Denominations ?? new List<POSShiftDenominationRequest>())
            .Where(x => x.DenominationValue > 0 && x.Quantity > 0)
            .GroupBy(x => x.DenominationValue)
            .Select(g =>
            {
                var quantity = g.Sum(x => x.Quantity);

                var item = new POSShiftCashDenomination
                {
                    StoreId = storeId,
                    POSShift = shift,
                    EntryType = POSShiftCashDenominationEntryType.Closing,
                    DenominationValue = g.Key,
                    Quantity = quantity
                };

                item.Recalc();

                return item;
            })
            .ToList();

        foreach (var item in closingDenomItems)
        {
            shift.CashDenominations.Add(item);
        }

        shift.Close(userId, req.ClosingCashActual, req.Note);

        await _shiftRepo.SaveChangesAsync(ct);
        // Tự tạo phiếu bàn giao cuối ca sau khi đóng ca thành công.
        await _closingSlipService.CreateFromClosedShiftAsync(shift.Id, ct);

        return Map(shift);
    }

    public async Task<POSShiftCashTransactionDto> AddCashTransactionAsync(
        CreatePosShiftCashTransactionRequest dto,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();
        var userId = RequireUserId();

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Terminal này chưa mở ca POS.",
                actionHint: "Vui lòng mở ca trước khi thực hiện thao tác này.",
                metadata: new
                {
                    TerminalId = terminalId
                });
        }
        EnsureShiftOwner(shift);

        if (dto.Amount <= 0)
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.CashAmountInvalid,
                message: "Số tiền phải lớn hơn 0.",
                actionHint: "Vui lòng nhập lại số tiền hợp lệ.",
                metadata: new
                {
                    dto.Amount,
                    dto.Type
                });
        }

        var reason = dto.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.CashReasonRequired,
                message: "Lý do không được để trống.",
                actionHint: "Vui lòng nhập lý do nộp/rút tiền.",
                metadata: new
                {
                    dto.Type
                });
        }

        var transaction = new POSShiftCashTransaction
        {
            StoreId = storeId,
            POSShiftId = shift.Id,
            Type = dto.Type,
            Amount = dto.Amount,
            Reason = reason,
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

        var shift = await _shiftRepo.GetOpenShiftAsync(storeId, terminalId, ct);
        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Terminal này chưa mở ca POS.",
                actionHint: "Vui lòng mở ca trước khi xem giao dịch tiền mặt.",
                metadata: new
                {
                    TerminalId = terminalId
                });
        }
        EnsureShiftOwner(shift);
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
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Không tìm thấy ca POS.",
                actionHint: "Vui lòng kiểm tra lại ca POS cần xem hoặc mở ca trước khi thao tác.");
        }
        var currentUserId = RequireUserId();

        // Nếu xem ca đang mở hiện tại thì vẫn kiểm tra owner như cũ.
        if (!shiftId.HasValue && shift.Status == POSShiftStatus.Open)
        {
            EnsureShiftOwner(shift);
        }

        // Nếu xem summary theo shiftId từ màn lịch sử,
        // chỉ cho xem ca do chính user hiện tại mở.
        if (shiftId.HasValue && shift.OpenedByUserId != currentUserId)
        {
            throw PosAppException.Ownership(
                errorCode: PosErrorCodes.ShiftOwnedByAnotherUser,
                message: "Bạn không có quyền xem ca của nhân viên khác.",
                actionHint: "Màn lịch sử ca POS chỉ hiển thị các ca do chính bạn mở.",
                metadata: new
                {
                    shiftId = shift.Id,
                    shiftCode = shift.ShiftCode,
                    openedByUserId = shift.OpenedByUserId,
                    currentUserId
                });
        }
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

        // Màn "Lịch sử ca POS" là lịch sử ca của chính nhân viên đang đăng nhập.
        // Không dùng req.UserId từ client để tránh nhân viên tự truyền UserId người khác.
        var currentUserId = RequireUserId();

        var (items, total) = await _shiftRepo.QueryHistoryAsync(
            storeId,
            currentUserId,
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
        => _currentUser.UserId ?? throw PosAppException.Context(
            errorCode: PosErrorCodes.ContextUserNotResolved,
            message: "Không xác định được tài khoản đang thao tác.",
            actionHint: "Vui lòng đăng nhập lại rồi thử lại.");

    private int RequireTerminalId()
        => _currentUser.TerminalId ?? throw PosAppException.Context(
            errorCode: PosErrorCodes.ContextTerminalNotResolved,
            message: "Không xác định được máy POS hiện tại.",
            actionHint: "Vui lòng đăng nhập lại hoặc kiểm tra cấu hình terminal của máy này.");

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

    private async Task<string?> ResolveUserDisplayNameAsync(
    int userId,
    CancellationToken ct = default)
    {
        if (userId <= 0)
            return null;

        var users = await _users.GetByIdsAsync(
            new List<int> { userId },
            ct);

        var user = users.FirstOrDefault();

        if (user == null)
            return $"User #{userId}";

        if (!string.IsNullOrWhiteSpace(user.FullName))
            return user.FullName;

        if (!string.IsNullOrWhiteSpace(user.UserName))
            return user.UserName;

        return $"User #{userId}";
    }

    private static string? AppendNote(
        string? current,
        string newNote,
        int maxLength)
    {
        var value = string.IsNullOrWhiteSpace(current)
            ? newNote
            : $"{current}{Environment.NewLine}{newNote}";

        return TrimText(value, maxLength);
    }

    private static string? TrimText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 20)
            return value[..maxLength];

        return value[..(maxLength - 20)] + "... [rút gọn]";
    }
    public async Task<POSShiftOwnershipInfoDto?> GetCurrentOwnershipInfoAsync(
    CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var terminalId = RequireTerminalId();
        var currentUserId = RequireUserId();

        var shift = await _shiftRepo.GetOpenShiftWithDetailsAsync(
            storeId,
            terminalId,
            ct);

        if (shift == null)
            return null;

        shift.RecalcExpected();

        var openedByName = await ResolveUserDisplayNameAsync(
            shift.OpenedByUserId,
            ct);

        var isOwnedByCurrentUser = shift.OpenedByUserId == currentUserId;

        return new POSShiftOwnershipInfoDto
        {
            ShiftId = shift.Id,
            ShiftCode = shift.ShiftCode,

            StoreId = shift.StoreId,

            TerminalId = shift.TerminalId,
            TerminalCode = shift.Terminal?.Code,
            TerminalName = shift.Terminal?.Name,

            OpenedByUserId = shift.OpenedByUserId,
            OpenedByUserName = openedByName,

            OpenedAtUtc = shift.OpenedAtUtc,

            WarehouseId = shift.WarehouseId,
            WarehouseCode = shift.Warehouse?.Code,
            WarehouseName = shift.Warehouse?.Name,

            OpeningCash = shift.OpeningCash,
            ClosingCashExpected = shift.ClosingCashExpected,

            IsOwnedByCurrentUser = isOwnedByCurrentUser,

            // Ghi chú:
            // Đây là khả năng nghiệp vụ.
            // Quyền thật sẽ chặn ở Controller bằng [Authorize(Policy = ...)].
            CanTakeOver = !isOwnedByCurrentUser,
            CanForceClose = !isOwnedByCurrentUser
        };
    }

    public async Task<POSShiftDto> TakeOverAsync(
        TakeOverPOSShiftRequest request,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var currentUserId = RequireUserId();

        if (request.ShiftId <= 0)
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftTakeoverNotAllowed,
                message: "Ca POS cần tiếp quản không hợp lệ.",
                actionHint: "Vui lòng tải lại màn hình POS rồi thử lại.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftTakeoverNotAllowed,
                message: "Bạn chưa nhập lý do tiếp quản ca.",
                actionHint: "Vui lòng nhập lý do để lưu lịch sử bàn giao.");
        }

        var shift = await _shiftRepo.GetOpenShiftByIdAsync(
            storeId,
            request.ShiftId,
            ct);

        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Không tìm thấy ca POS đang mở cần tiếp quản.",
                actionHint: "Ca có thể đã được đóng hoặc thay đổi ở máy khác. Vui lòng tải lại.");
        }

        if (shift.OpenedByUserId == currentUserId)
        {
            return Map(shift);
        }

        var oldUserId = shift.OpenedByUserId;
        var oldUserName = await ResolveUserDisplayNameAsync(oldUserId, ct);
        var newUserName = _currentUser.UserName ?? $"User #{currentUserId}";

        shift.OpenedByUserId = currentUserId;

        var takeoverNote =
            $"[TIẾP QUẢN CA - {DateTime.Now:dd/MM/yyyy HH:mm:ss}] " +
            $"Từ: {oldUserName ?? $"User #{oldUserId}"} → {newUserName}. " +
            $"Lý do: {reason}";

        shift.OpenNote = AppendNote(shift.OpenNote, takeoverNote, 300);

        await _shiftRepo.UpdateAsync(shift, ct);
        await _shiftRepo.SaveChangesAsync(ct);

        // =====================================================
        // AUDIT LOG:
        // Ghi lại thao tác tiếp quản ca POS.
        // Log này giúp truy vết sau này:
        // - Ai tiếp quản
        // - Tiếp quản từ ai
        // - Lý do gì
        // - Ca nào / terminal nào / kho nào
        // =====================================================
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            StoreId = storeId,
            ActorUserId = currentUserId,
            ActorUserName = newUserName,

            Module = AuditModuleType.POS,
            ActionType = AuditActionType.TakeOverShift,

            EntityName = nameof(POSShift),
            EntityId = shift.Id.ToString(),
            EntityDisplay = shift.ShiftCode ?? $"Ca POS #{shift.Id}",

            Summary =
                $"Tiếp quản ca POS {shift.ShiftCode ?? $"#{shift.Id}"} " +
                $"từ {oldUserName ?? $"User #{oldUserId}"} sang {newUserName}. " +
                $"Lý do: {reason}",

            OldValuesJson = JsonSerializer.Serialize(new
            {
                OpenedByUserId = oldUserId,
                OpenedByUserName = oldUserName
            }),

            NewValuesJson = JsonSerializer.Serialize(new
            {
                OpenedByUserId = currentUserId,
                OpenedByUserName = newUserName,
                Reason = reason,
                shift.TerminalId,
                TerminalCode = shift.Terminal?.Code,
                TerminalName = shift.Terminal?.Name,
                shift.WarehouseId,
                WarehouseCode = shift.Warehouse?.Code,
                WarehouseName = shift.Warehouse?.Name
            }),

            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
        nameof(POSShift.OpenedByUserId),
        nameof(POSShift.OpenNote)
    }),

            IsSuccess = true
        }, ct);

        return Map(shift);
    }

    public async Task<POSShiftDto> ForceCloseAsync(
        ForceClosePOSShiftRequest request,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var currentUserId = RequireUserId();

        if (request.ShiftId <= 0)
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftCloseStateConflict,
                message: "Ca POS cần đóng hộ không hợp lệ.",
                actionHint: "Vui lòng tải lại màn hình POS rồi thử lại.");
        }

        if (request.ClosingCashActual < 0)
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.CashAmountInvalid,
                message: "Tiền thực tế kiểm quỹ không được âm.",
                actionHint: "Vui lòng nhập lại số tiền kiểm quỹ.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftCloseStateConflict,
                message: "Bạn chưa nhập lý do đóng hộ ca.",
                actionHint: "Vui lòng nhập lý do để lưu lịch sử kiểm soát.");
        }

        var shift = await _shiftRepo.GetOpenShiftByIdAsync(
            storeId,
            request.ShiftId,
            ct);

        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Không tìm thấy ca POS đang mở cần đóng hộ.",
                actionHint: "Ca có thể đã được đóng hoặc thay đổi ở máy khác. Vui lòng tải lại.");
        }

        var orders = await _orderRepo.GetByShiftIdAsync(shift.Id, ct);

        var heldOrders = orders
            .Where(x => x.Status == OrderStatus.OnHold)
            .ToList();

        if (heldOrders.Any())
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftCloseBlockedHeldOrders,
                message: "Không thể đóng hộ ca vì còn đơn đang giữ.",
                actionHint: "Hãy xử lý hết đơn giữ trước khi đóng ca.",
                metadata: new
                {
                    shift.Id,
                    shift.ShiftCode,
                    HeldOrderCount = heldOrders.Count,
                    HeldOrderIds = heldOrders.Select(x => x.Id).ToList()
                });
        }

        var draftOrdersWithData = orders
            .Where(x =>
                x.Status == OrderStatus.Draft &&
                (
                    x.Lines.Any(l => !l.IsDeleted) ||
                    x.Payments.Any(p => !p.IsDeleted)
                ))
            .ToList();

        if (draftOrdersWithData.Any())
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftCloseBlockedActiveDrafts,
                message: "Không thể đóng hộ ca vì còn giỏ chưa xử lý.",
                actionHint: "Hãy hoàn tất, giữ đơn hoặc hủy giỏ trước khi đóng ca.",
                metadata: new
                {
                    shift.Id,
                    shift.ShiftCode,
                    DraftCount = draftOrdersWithData.Count,
                    DraftOrderIds = draftOrdersWithData.Select(x => x.Id).ToList()
                });
        }

        var oldUserName = await ResolveUserDisplayNameAsync(
            shift.OpenedByUserId,
            ct);

        var closeNote =
            $"[ĐÓNG HỘ CA - {DateTime.Now:dd/MM/yyyy HH:mm:ss}] " +
            $"Ca của: {oldUserName ?? $"User #{shift.OpenedByUserId}"}. " +
            $"Người đóng hộ: {_currentUser.UserName ?? $"User #{currentUserId}"}. " +
            $"Lý do: {reason}";

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            closeNote += $" Ghi chú: {request.Note.Trim()}";
        }

        shift.Close(
            closedByUserId: currentUserId,
            closingCashActual: request.ClosingCashActual,
            closeNote: TrimText(closeNote, 300));

        await _shiftRepo.UpdateAsync(shift, ct);
        await _shiftRepo.SaveChangesAsync(ct);

        // Tự tạo phiếu bàn giao cuối ca khi quản lý đóng hộ.
        await _closingSlipService.CreateFromClosedShiftAsync(shift.Id, ct);

        // =====================================================
        // AUDIT LOG
        // Đóng hộ ca POS
        // =====================================================
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            StoreId = storeId,
            ActorUserId = currentUserId,
            ActorUserName = _currentUser.UserName,

            Module = AuditModuleType.POS,
            ActionType = AuditActionType.ForceCloseShift,

            EntityName = nameof(POSShift),
            EntityId = shift.Id.ToString(),
            EntityDisplay = shift.ShiftCode ?? $"POSShift #{shift.Id}",

            Summary =
                $"Đóng hộ ca POS {shift.ShiftCode ?? $"#{shift.Id}"}. " +
                $"Chủ ca: {oldUserName ?? $"User #{shift.OpenedByUserId}"}. " +
                $"Người đóng hộ: {_currentUser.UserName ?? $"User #{currentUserId}"}. " +
                $"Lý do: {reason}",

            OldValuesJson = JsonSerializer.Serialize(new
            {
                Status = "Open",
                OpenedByUserId = shift.OpenedByUserId,
                OpenedByUserName = oldUserName
            }),

            NewValuesJson = JsonSerializer.Serialize(new
            {
                Status = "Closed",
                ClosedByUserId = currentUserId,
                ClosedByUserName = _currentUser.UserName,
                request.ClosingCashActual,
                Reason = reason
            }),

            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
        nameof(POSShift.Status),
        nameof(POSShift.ClosedByUserId),
        nameof(POSShift.ClosedAtUtc),
        nameof(POSShift.ClosingCashActual),
        nameof(POSShift.CloseNote)
    }),

            IsSuccess = true
        }, ct);

        return Map(shift);
    }
    private void EnsureShiftOwner(POSShift shift)
    {
        var currentUserId = RequireUserId();

        if (shift.OpenedByUserId > 0 && shift.OpenedByUserId != currentUserId)
        {
            shift.RecalcExpected();

            throw PosAppException.Ownership(
                errorCode: PosErrorCodes.ShiftOwnedByAnotherUser,
                message: "Ca POS này đang thuộc nhân viên khác.",
                actionHint: "Vui lòng đổi đúng tài khoản đã mở ca, hoặc dùng chức năng tiếp quản/đóng hộ nếu bạn có quyền.",
                metadata: new
                {
                    shiftId = shift.Id,
                    shiftCode = shift.ShiftCode,
                    openedByUserId = shift.OpenedByUserId,
                    currentUserId,
                    terminalId = shift.TerminalId,
                    terminalCode = shift.Terminal?.Code,
                    terminalName = shift.Terminal?.Name,
                    warehouseId = shift.WarehouseId,
                    warehouseCode = shift.Warehouse?.Code,
                    warehouseName = shift.Warehouse?.Name,
                    openedAtUtc = shift.OpenedAtUtc,
                    openingCash = shift.OpeningCash,
                    closingCashExpected = shift.ClosingCashExpected
                });
        }
    }
    private static List<POSShiftPrintDenominationDto> MapPrintDenominations(
    POSShift shift,
    POSShiftCashDenominationEntryType entryType)
    {
        return shift.CashDenominations
            .Where(x => !x.IsDeleted && x.EntryType == entryType)
            .OrderByDescending(x => x.DenominationValue)
            .Select(x => new POSShiftPrintDenominationDto
            {
                DenominationValue = x.DenominationValue,
                Quantity = x.Quantity,
                Amount = x.Amount
            })
            .ToList();
    }
    public async Task<POSShiftPrintDto> GetPrintAsync(
    int shiftId,
    POSShiftCashDenominationEntryType entryType,
    CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        if (shiftId <= 0)
            throw PosAppException.Validation(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Ca POS cần in không hợp lệ.",
                actionHint: "Vui lòng tải lại ca rồi thử in lại.");

        var shift = await _shiftRepo.GetByIdAsync(shiftId, ct);

        if (shift == null || shift.StoreId != storeId)
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Không tìm thấy ca POS cần in.",
                actionHint: "Vui lòng kiểm tra lại ca POS.");

        shift.RecalcExpected();

        var openedByName = await ResolveUserDisplayNameAsync(shift.OpenedByUserId, ct);

        string? closedByName = null;
        if (shift.ClosedByUserId.HasValue)
        {
            closedByName = await ResolveUserDisplayNameAsync(shift.ClosedByUserId.Value, ct);
        }

        return new POSShiftPrintDto
        {
            ShiftId = shift.Id,
            ShiftCode = shift.ShiftCode,
            Status = shift.Status,

            PrintType = entryType == POSShiftCashDenominationEntryType.Opening
                ? "Opening"
                : "Closing",

            BarcodeValue = entryType == POSShiftCashDenominationEntryType.Opening
                ? $"POSSHIFT-OPEN-{shift.Id}"
                : $"POSSHIFT-CLOSE-{shift.Id}",

            OpenedAtUtc = shift.OpenedAtUtc,
            ClosedAtUtc = shift.ClosedAtUtc,

            OpenedByUserId = shift.OpenedByUserId,
            OpenedByUserName = openedByName,

            ClosedByUserId = shift.ClosedByUserId,
            ClosedByUserName = closedByName,

            TerminalId = shift.TerminalId,
            TerminalCode = shift.Terminal?.Code,
            TerminalName = shift.Terminal?.Name,

            WarehouseId = shift.WarehouseId,
            WarehouseCode = shift.Warehouse?.Code,
            WarehouseName = shift.Warehouse?.Name,

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
            CashDifference = shift.CashDifference,

            OpenNote = shift.OpenNote,
            CloseNote = shift.CloseNote,

            Denominations = MapPrintDenominations(shift, entryType)
        };
    }
    private static bool IsHandoverSlipUsable(POSShiftHandoverSlip slip)
    {
        return slip.Status == POSShiftHandoverSlipStatus.Draft
               || slip.Status == POSShiftHandoverSlipStatus.Printed;
    }

    private static List<POSShiftDenominationRequest> BuildDenominationsFromSlip(
        POSShiftHandoverSlip slip)
    {
        return slip.Denominations
            .Where(x => !x.IsDeleted && x.DenominationValue > 0 && x.Quantity > 0)
            .Select(x => new POSShiftDenominationRequest
            {
                DenominationValue = x.DenominationValue,
                Quantity = x.Quantity
            })
            .ToList();
    }
    /// <summary>
    /// Dashboard quản lý ca POS.
    /// Tổng hợp theo bộ lọc.
    /// </summary>
    public async Task<POSShiftManagerDashboardDto> GetManagerDashboardAsync(
        POSShiftManagerDashboardQueryDto query,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var shifts = await _shiftRepo.QueryForManagerDashboardAsync(
            storeId,
            query.FromUtc,
            query.ToUtcExclusive,
            query.UserId,
            query.TerminalId,
            query.Status,
            ct);

        var result = new POSShiftManagerDashboardDto();

        result.Overview = new POSShiftManagerOverviewDto
        {
            TotalShifts = shifts.Count,

            OpenShifts = shifts.Count(x =>
                x.Status == POSShiftStatus.Open),

            ClosedShifts = shifts.Count(x =>
                x.Status == POSShiftStatus.Closed),

            CashSalesTotal = shifts.Sum(x => x.CashSalesTotal),

            NonCashSalesTotal = shifts.Sum(x => x.NonCashSalesTotal),

            TotalSales =
                shifts.Sum(x => x.CashSalesTotal) +
                shifts.Sum(x => x.NonCashSalesTotal),

            CashInTotal = shifts.Sum(x => x.CashInTotal),

            CashOutTotal = shifts.Sum(x => x.CashOutTotal),

            RefundTotal =
                shifts.Sum(x => x.CashRefundTotal) +
                shifts.Sum(x => x.NonCashRefundTotal),

            RefundCount = shifts.Sum(x => x.RefundCount),

            VoidCount = shifts.Sum(x => x.VoidCount),

            ClosingCashExpectedTotal = shifts
                .Where(x => x.Status == POSShiftStatus.Closed)
                .Sum(x => x.ClosingCashExpected),

            ClosingCashActualTotal = shifts
                .Where(x =>
                    x.Status == POSShiftStatus.Closed &&
                    x.ClosingCashActual.HasValue)
                .Sum(x => x.ClosingCashActual ?? 0m),

            CashDifferenceTotal = shifts
                .Where(x =>
                    x.Status == POSShiftStatus.Closed &&
                    x.ClosingCashActual.HasValue)
                .Sum(x =>
                    (x.ClosingCashActual ?? 0m) -
                    x.ClosingCashExpected),

            DifferenceShiftCount = shifts.Count(x =>
                x.Status == POSShiftStatus.Closed &&
                x.ClosingCashActual.HasValue &&
                x.ClosingCashActual.Value != x.ClosingCashExpected),

            ShortageShiftCount = shifts.Count(x =>
                x.Status == POSShiftStatus.Closed &&
                x.ClosingCashActual.HasValue &&
                x.ClosingCashActual.Value < x.ClosingCashExpected),

            OverShiftCount = shifts.Count(x =>
                x.Status == POSShiftStatus.Closed &&
                x.ClosingCashActual.HasValue &&
                x.ClosingCashActual.Value > x.ClosingCashExpected)
        };

        var userIds = shifts
            .Select(x => x.OpenedByUserId)
            .Distinct()
            .ToList();

        var users = await _users.GetByIdsAsync(userIds, ct);

        result.EmployeeStats = shifts
            .GroupBy(x => x.OpenedByUserId)
            .Select(g =>
            {
                var user = users.FirstOrDefault(x => x.Id == g.Key);

                return new POSShiftManagerEmployeeStatDto
                {
                    UserId = g.Key,

                    UserName = user?.UserName,

                    FullName = user?.FullName,

                    TotalShifts = g.Count(),

                    OpenShifts = g.Count(x =>
                        x.Status == POSShiftStatus.Open),

                    ClosedShifts = g.Count(x =>
                        x.Status == POSShiftStatus.Closed),

                    CashSalesTotal = g.Sum(x =>
                        x.CashSalesTotal),

                    NonCashSalesTotal = g.Sum(x =>
                        x.NonCashSalesTotal),

                    TotalSales =
                        g.Sum(x => x.CashSalesTotal) +
                        g.Sum(x => x.NonCashSalesTotal),

                    RefundTotal =
                        g.Sum(x => x.CashRefundTotal) +
                        g.Sum(x => x.NonCashRefundTotal),

                    RefundCount = g.Sum(x => x.RefundCount),

                    VoidCount = g.Sum(x => x.VoidCount),

                    CashDifferenceTotal = g
                        .Where(x =>
                            x.Status == POSShiftStatus.Closed &&
                            x.ClosingCashActual.HasValue)
                        .Sum(x =>
                            (x.ClosingCashActual ?? 0m) -
                            x.ClosingCashExpected),

                    DifferenceShiftCount = g.Count(x =>
                        x.Status == POSShiftStatus.Closed &&
                        x.ClosingCashActual.HasValue &&
                        x.ClosingCashActual.Value != x.ClosingCashExpected)
                };
            })
            .OrderByDescending(x => x.TotalSales)
            .ToList();

        result.Shifts = shifts
            .Select(x => new POSShiftManagerShiftItemDto
            {
                Id = x.Id,

                ShiftCode = x.ShiftCode,

                Status = x.Status,

                OpenedByUserId = x.OpenedByUserId,

                OpenedByUserName = users
                    .FirstOrDefault(u => u.Id == x.OpenedByUserId)
                    ?.UserName,

                ClosedByUserId = x.ClosedByUserId,

                TerminalId = x.TerminalId,

                TerminalCode = x.Terminal?.Code,

                TerminalName = x.Terminal?.Name,

                WarehouseId = x.WarehouseId,

                WarehouseCode = x.Warehouse?.Code,

                WarehouseName = x.Warehouse?.Name,

                OpenedAtUtc = x.OpenedAtUtc,

                ClosedAtUtc = x.ClosedAtUtc,

                OpeningCash = x.OpeningCash,

                CashSalesTotal = x.CashSalesTotal,

                NonCashSalesTotal = x.NonCashSalesTotal,

                CashInTotal = x.CashInTotal,

                CashOutTotal = x.CashOutTotal,

                RefundTotal =
                    x.CashRefundTotal +
                    x.NonCashRefundTotal,

                RefundCount = x.RefundCount,

                VoidCount = x.VoidCount,

                ClosingCashExpected = x.ClosingCashExpected,

                ClosingCashActual = x.ClosingCashActual,

                CashDifference =
                    x.Status == POSShiftStatus.Closed &&
                    x.ClosingCashActual.HasValue
                        ? x.ClosingCashActual.Value -
                          x.ClosingCashExpected
                        : 0m,

                OpenNote = x.OpenNote,

                CloseNote = x.CloseNote
            })
            .OrderByDescending(x => x.OpenedAtUtc)
            .ToList();

        result.DifferenceShifts = result.Shifts
            .Where(x => x.CashDifference != 0)
            .OrderByDescending(x => Math.Abs(x.CashDifference))
            .Take(20)
            .ToList();

        return result;
    }
}