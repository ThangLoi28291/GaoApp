using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GaoApp.Application.Services.Orders;

public sealed class POSService : IPOSService
{
    private readonly IAppUnitOfWork _uow;
    private readonly IOrderRepository _orders;
    private readonly IProductVariantRepository _variants;
    private readonly IOrderPaymentRepository _payments;
    private readonly IOrderNumberGenerator _orderNo;
    private readonly IPOSShiftRepository _shifts;
    private readonly ICustomerRepository _customers;
    private readonly IPOSAuditLogRepository _auditLogs;
    private readonly IWarehouseRepository _warehouses;
    private readonly IBarcodeLookupService _barcodeLookup;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ISalesReturnService _salesReturnService;
    private readonly ISalesReturnRepository _salesReturns;
    private readonly IInventoryValuationEntryRepository _inventoryValuationEntryRepository;
    private readonly IOrderInventoryIssueRepository _orderInventoryIssues;
    private readonly IProductVariantRepository _productVariantRepository;
    private readonly ICurrentPOSContext _posContext;
    private readonly IInventoryBalanceRepository _inventoryBalanceRepository;
    private readonly IUserRepository _users;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<POSService> _logger;
    private readonly IOrderRewardCalculator _orderRewardCalculator;
    private readonly ICustomerRewardLedgerRepository _rewardLedgerRepository;
    private readonly ICustomerRewardService _customerRewardService;
    private readonly ICustomerRewardVoucherRepository _rewardVoucherRepository;
    private readonly IPromotionEngine _promotionEngine;
    private readonly IPromotionRepository _promotionRepository;
    private readonly IOrderLegalEntityFinalizeService _legalEntityFinalizeService;
    private readonly IOrderLegalEntityReversalService _legalEntityReversalService;
    public POSService(
        IAppUnitOfWork uow,
        IOrderRepository orders,
        IProductVariantRepository variants,
        IOrderPaymentRepository payments,
        IOrderNumberGenerator orderNo,
        IPOSShiftRepository shifts,
        ICustomerRepository customers,
        IPOSAuditLogRepository auditLogs,
        IWarehouseRepository warehouses,
        IBarcodeLookupService barcodeLookup,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory,
        IInventoryReservationService inventoryReservationService,
          IAuditLogService auditLogService,
          ISalesReturnService salesReturnService,
           ISalesReturnRepository salesReturns,
           IInventoryValuationEntryRepository inventoryValuationEntryRepository,
           IOrderInventoryIssueRepository orderInventoryIssues,
           IProductVariantRepository productVariantRepository,
           ICurrentPOSContext posContext,
           IInventoryBalanceRepository inventoryBalanceRepository,
            IUserRepository users, IInvoiceService invoiceService,
ILogger<POSService> logger,
IOrderRewardCalculator orderRewardCalculator,
ICustomerRewardLedgerRepository rewardLedgerRepository, 
ICustomerRewardService customerRewardService, 
ICustomerRewardVoucherRepository rewardVoucherRepository, 
IPromotionEngine promotionEngine,
IPromotionRepository promotionRepository,
IOrderLegalEntityFinalizeService legalEntityFinalizeService,
IOrderLegalEntityReversalService legalEntityReversalService)
    {
        _uow = uow;
        _orders = orders;
        _variants = variants;
        _payments = payments;
        _orderNo = orderNo;
        _shifts = shifts;
        _customers = customers;
        _auditLogs = auditLogs;
        _warehouses = warehouses;
        _barcodeLookup = barcodeLookup;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryReservationService = inventoryReservationService;
        _auditLogService = auditLogService;
        _salesReturnService = salesReturnService;
        _salesReturns = salesReturns;
        _inventoryValuationEntryRepository = inventoryValuationEntryRepository;
        _orderInventoryIssues = orderInventoryIssues;
        _productVariantRepository = productVariantRepository;
        _posContext = posContext;
        _inventoryBalanceRepository = inventoryBalanceRepository;
        _users = users;
        _invoiceService = invoiceService;
        _logger = logger;
        _orderRewardCalculator = orderRewardCalculator;
        _rewardLedgerRepository = rewardLedgerRepository;
        _customerRewardService = customerRewardService;
        _rewardVoucherRepository = rewardVoucherRepository;
        _promotionEngine = promotionEngine;
        _promotionRepository = promotionRepository;
        _legalEntityFinalizeService = legalEntityFinalizeService;
        _legalEntityReversalService = legalEntityReversalService;
    }
    private sealed class FinalizeInventoryIssueLine
    {
        public int OrderLineId { get; set; }

        public int ProductId { get; set; }
        public int VariantId { get; set; }

        public int? ProductUnitConversionId { get; set; }
        public int? BarcodeId { get; set; }

        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        /// Base quantity của dòng bán tại thời điểm finalize.
        /// </summary>
        public decimal BaseQuantity { get; set; }

        public bool IsNegativeInventory { get; set; }
        public decimal BeforeQty { get; set; }
        public decimal AfterQty { get; set; }

        public bool IsProvisionalCost { get; set; }
        public decimal? UnitCostSnapshot { get; set; }
        public decimal? ProvisionalUnitCost { get; set; }

        public string? Note { get; set; }
    }


    private sealed class FinalizeInventorySummary
    {
        public bool HasNegativeInventory { get; set; }
        public bool HasProvisionalCost { get; set; }

        public List<FinalizeInventoryIssueLine> IssueLines { get; set; } = new();
    }
    /// <summary>
    /// Tự sinh hóa đơn bán ra sau khi POS finalize thành công.
    /// Lưu ý:
    /// - Chạy sau khi transaction POS đã commit.
    /// - Không được làm fail POS nếu invoice lỗi.
    /// </summary>
    private async Task TryGenerateInvoiceAfterFinalizeAsync(
        int orderId,
        CancellationToken ct = default)
    {
        try
        {
            var generateResult = await _invoiceService.GenerateInvoicesFromOrderAsync(orderId, ct);

            if (!generateResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Không sinh được bộ hóa đơn sau finalize. OrderId={OrderId}. Error={Error}",
                    orderId,
                    generateResult.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Lỗi hậu xử lý Invoice sau POS finalize. POS vẫn đã hoàn tất. OrderId={OrderId}",
                orderId);
        }
    }
    private async Task<POSShift> RequireCurrentOpenShiftAsync(CancellationToken ct)
    {
        if (!_posContext.IsAvailable || _posContext.StoreId <= 0 || _posContext.TerminalId <= 0)
        {
            throw PosAppException.Context(
                errorCode: PosErrorCodes.ContextTerminalNotResolved,
                message: "Không xác định được máy POS hiện tại.",
                actionHint: "Vui lòng đăng nhập lại hoặc kiểm tra cấu hình terminal của máy này.",
                metadata: new
                {
                    _posContext.IsAvailable,
                    _posContext.StoreId,
                    _posContext.TerminalId
                });
        }

        var shift = await _shifts.GetOpenShiftAsync(
            _posContext.StoreId,
            _posContext.TerminalId,
            ct);

        if (shift == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.ShiftNotOpen,
                message: "Terminal này chưa mở ca POS.",
                actionHint: "Vui lòng mở ca trước khi thực hiện thao tác này.",
                metadata: new
                {
                    _posContext.StoreId,
                    _posContext.TerminalId
                });
        }

        return shift;
    }

    private async Task<int?> TryResolveCurrentPOSWarehouseIdAsync(CancellationToken ct)
    {
        if (!_posContext.IsAvailable)
            return null;

        var shift = await _shifts.GetOpenShiftAsync(
            _posContext.StoreId,
            _posContext.TerminalId,
            ct);

        if (shift == null)
            return null;

        if (shift.WarehouseId <= 0)
            return null;

        return shift.WarehouseId;
    }
    private void EnsureShiftOwnership(POSShift shift)
    {
        var currentUserId = _posContext.UserId;

        if (!currentUserId.HasValue || currentUserId.Value <= 0)
        {
            throw PosAppException.Context(
                errorCode: PosErrorCodes.ContextUserNotResolved,
                message: "Không xác định được tài khoản đang thao tác.",
                actionHint: "Vui lòng đăng nhập lại rồi thử lại.");
        }

        if (shift.OpenedByUserId <= 0 || shift.OpenedByUserId == currentUserId.Value)
            return;

        shift.RecalcExpected();

        throw PosAppException.Ownership(
            errorCode: PosErrorCodes.ShiftOwnedByAnotherUser,
            message: "Terminal này đang có ca POS của nhân viên khác.",
            actionHint: "Vui lòng đổi đúng tài khoản đã mở ca, hoặc nhờ quản lý tiếp quản/đóng hộ ca.",
            metadata: new
            {
                shiftId = shift.Id,
                shiftCode = string.IsNullOrWhiteSpace(shift.ShiftCode)
                    ? $"SHIFT-{shift.Id}"
                    : shift.ShiftCode,

                storeId = shift.StoreId,

                terminalId = shift.TerminalId,
                terminalCode = shift.Terminal != null
                    ? shift.Terminal.Code
                    : null,
                terminalName = shift.Terminal != null
                    ? shift.Terminal.Name
                    : null,

                openedByUserId = shift.OpenedByUserId,

                // POSService chưa nên query user trong hàm sync này.
                // UI sẽ gọi /admin/pos/shift/ownership-info để lấy tên người mở ca.
                openedByUserName = (string?)null,

                currentUserId = currentUserId.Value,

                openedAtUtc = shift.OpenedAtUtc,

                warehouseId = shift.WarehouseId,
                warehouseCode = shift.Warehouse != null
                    ? shift.Warehouse.Code
                    : null,
                warehouseName = shift.Warehouse != null
                    ? shift.Warehouse.Name
                    : null,

                openingCash = shift.OpeningCash,
                cashSalesTotal = shift.CashSalesTotal,
                cashInTotal = shift.CashInTotal,
                cashOutTotal = shift.CashOutTotal,
                cashRefundTotal = shift.CashRefundTotal,
                closingCashExpected = shift.ClosingCashExpected,

                ownershipInfoUrl = "/admin/pos/shift/ownership-info",
                takeOverUrl = "/admin/pos/shift/takeover",
                forceCloseUrl = "/admin/pos/shift/force-close",

                canTakeOver = false,
                canForceClose = false,

                requiredTakeOverPolicy = "pos.shift.takeover",
                requiredForceClosePolicy = "pos.shift.forceclose"
            });
    }
    /// <summary>
    /// Ghi log timeline riêng cho POS.
    /// Log này phục vụ xem lịch sử thao tác của order trong POS.
    /// 
    /// Lưu ý:
    /// - Đây là log nghiệp vụ riêng của POS, không thay thế AuditLog chung toàn hệ thống.
    /// - Không throw lại exception để tránh lỗi ghi log làm fail flow chính.
    /// </summary>
    private async Task WritePosAuditLogAsync(
        string action,
        int? orderId,
        string? note,
        object? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            var log = new POSAuditLog
            {
                Action = action,
                OrderId = orderId,
                UserId = null, // TODO: gắn current user nếu service của bạn đã có context user
                Note = note,
                MetadataJson = metadata == null
                    ? null
                    : JsonSerializer.Serialize(metadata)
            };

            await _auditLogs.AddAsync(log, ct);
            await _auditLogs.SaveChangesAsync(ct);
        }
        catch
        {
            // Không throw lại để tránh lỗi ghi log làm fail nghiệp vụ chính.
            // Sau này có thể bơm ILogger để ghi technical log.
        }
    }
    /// <summary>
    /// Ghi audit log chung toàn hệ thống.
    /// Dùng cho các nghiệp vụ quan trọng như finalize/refund/cancel.
    /// </summary>
    private async Task WriteFinalizeOrderAuditLogAsync(
        Order order,
        string oldStatus,
        string oldPaymentStatus,
        CancellationToken ct = default)
    {
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            Module = AuditModuleType.Orders,
            ActionType = AuditActionType.FinalizeOrder,
            EntityName = nameof(Order),
            EntityId = order.Id.ToString(),
            EntityDisplay = $"Đơn hàng {order.OrderNumber}",
            Summary = $"Finalize order {order.OrderNumber} - Tổng tiền {order.GrandTotal:n0}",
            OldValuesJson = JsonSerializer.Serialize(new
            {
                Status = oldStatus,
                PaymentStatus = oldPaymentStatus
            }),
            NewValuesJson = JsonSerializer.Serialize(new
            {
                Status = order.Status.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderNumber = order.OrderNumber,
                GrandTotal = order.GrandTotal,
                HasInventoryIssue = order.HasInventoryIssue,
                InventoryResolutionStatus = order.InventoryResolutionStatus.ToString(),
                LegalEntityCount = order.LegalEntityCount,
                HasMultipleLegalEntities = order.HasMultipleLegalEntities,
                LegalEntityAllocatedAtUtc = order.LegalEntityAllocatedAtUtc
            }),
            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
                "Status",
                "PaymentStatus",
                "OrderNumber",
                "GrandTotal",
                "HasInventoryIssue",
                "InventoryResolutionStatus",
                "LegalEntityCount",
                "HasMultipleLegalEntities",
                "LegalEntityAllocatedAtUtc"
            }),
            IsSuccess = true
        }, ct);

    }
    /// <summary>
    /// Ghi AuditLog chung cho nghiệp vụ trả hàng / hoàn tiền.
    /// Đây là log toàn hệ thống để tra cứu vận hành.
    /// </summary>
    private async Task WriteRefundOrderAuditLogAsync(
        Order order,
        string oldStatus,
        string oldPaymentStatus,
        string reason,
        CancellationToken ct = default)
    {
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            Module = AuditModuleType.Orders,
            ActionType = AuditActionType.Refund,
            EntityName = nameof(Order),
            EntityId = order.Id.ToString(),
            EntityDisplay = $"Đơn hàng {order.OrderNumber}",
            Summary = $"Refund order {order.OrderNumber} - Lý do: {reason}",
            OldValuesJson = JsonSerializer.Serialize(new
            {
                Status = oldStatus,
                PaymentStatus = oldPaymentStatus
            }),
            NewValuesJson = JsonSerializer.Serialize(new
            {
                Status = order.Status.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderNumber = order.OrderNumber,
                GrandTotal = order.GrandTotal,
                Reason = reason
            }),
            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
            "Status",
            "PaymentStatus",
            "Reason"
        }),
            IsSuccess = true
        }, ct);
    }

    /// <summary>
    /// Ghi AuditLog chung cho nghiệp vụ hủy đơn sau khi đã chốt.
    /// Đây là log toàn hệ thống để tra cứu vận hành.
    /// </summary>
    private async Task WriteVoidOrderAuditLogAsync(
        Order order,
        string oldStatus,
        string oldPaymentStatus,
        string reason,
        CancellationToken ct = default)
    {
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            Module = AuditModuleType.Orders,
            ActionType = AuditActionType.CancelOrder,
            EntityName = nameof(Order),
            EntityId = order.Id.ToString(),
            EntityDisplay = $"Đơn hàng {order.OrderNumber}",
            Summary = $"Void order {order.OrderNumber} - Lý do: {reason}",
            OldValuesJson = JsonSerializer.Serialize(new
            {
                Status = oldStatus,
                PaymentStatus = oldPaymentStatus
            }),
            NewValuesJson = JsonSerializer.Serialize(new
            {
                Status = order.Status.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderNumber = order.OrderNumber,
                GrandTotal = order.GrandTotal,
                Reason = reason
            }),
            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
            "Status",
            "PaymentStatus",
            "Reason"
        }),
            IsSuccess = true
        }, ct);
    }

    private static string GenerateHoldCode(int orderId)
    {
        return $"H{orderId:D6}";
    }
    private static bool HasMeaningfulWork(Order order)
    {
        if (order == null) return false;

        var hasLines = order.Lines.Any(x => !x.IsDeleted);
        var hasPayments = order.Payments.Any(x => !x.IsDeleted);

        return hasLines || hasPayments;
    }
    private static void EnsureCanBeCurrentCart(Order order, int shiftId)
    {
        if (order.POSShiftId != shiftId)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartNotInCurrentShift,
                message: "Đơn này không thuộc ca POS hiện tại.",
                actionHint: "Vui lòng chọn đơn thuộc ca hiện tại hoặc chuyển sang đúng ca để tiếp tục.",
                metadata: new
                {
                    order.Id,
                    order.OrderNumber,
                    OrderShiftId = order.POSShiftId,
                    CurrentShiftId = shiftId
                });
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentInvalidStatus,
                message: "Giỏ hiện tại không còn ở trạng thái có thể chỉnh sửa.",
                actionHint: "Vui lòng chọn một đơn nháp khác hoặc tạo giỏ mới.",
                metadata: new
                {
                    order.Id,
                    order.OrderNumber,
                    Status = order.Status.ToString()
                });
        }
    }

    private static decimal SafePositive(decimal value, decimal fallback = 0m)
    {
        return value < 0 ? fallback : value;
    }

    private static decimal ComputeBaseQuantity(decimal sellingQty, decimal multiplier)
    {
        if (sellingQty <= 0) sellingQty = 1;
        if (multiplier <= 0) multiplier = 1;

        return sellingQty * multiplier;
    }

    private static BarcodeLookupSourceType? MapBarcodeSource(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return null;

        return sourceType.Trim() switch
        {
            "UnitBarcode" => BarcodeLookupSourceType.UnitBarcode,
            "VariantBarcode" => BarcodeLookupSourceType.VariantBarcode,
            "BarcodeHistory" => BarcodeLookupSourceType.BarcodeHistory,
            "ManualVariant" => BarcodeLookupSourceType.ManualVariant,
            _ => null
        };
    }

    /// <summary>
    /// Thông tin đơn vị bán mà POS sẽ dùng khi:
    /// - add tay theo variant
    /// - fallback từ barcode history variant-level
    /// </summary>
    private sealed class PosSellingUnitInfo
    {
        public int? ProductUnitConversionId { get; set; }
        public int SellingUnitId { get; set; }
        public string? SellingUnitName { get; set; }
        public int BaseUnitId { get; set; }
        public string? BaseUnitName { get; set; }
        public decimal Multiplier { get; set; } = 1m;
        public decimal? UnitPrice { get; set; }
        public string? Barcode { get; set; }
        public bool IsBaseUnit { get; set; }
        public bool IsDefaultForSale { get; set; }
    }

    /// <summary>
    /// Lấy barcode primary/active của 1 conversion.
    /// </summary>
    private static string? ResolvePrimaryBarcode(ProductUnitConversion? conversion)
    {
        if (conversion?.Barcodes == null || !conversion.Barcodes.Any())
            return null;

        return conversion.Barcodes
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolve 1 barcode đại diện của variant.
    ///
    /// Thứ tự ưu tiên:
    /// 1) đơn vị mặc định bán
    /// 2) đơn vị gốc
    /// 3) barcode active đầu tiên còn lại
    /// </summary>
    private static string? ResolveRepresentativeBarcode(ProductVariant? variant)
    {
        if (variant?.UnitConversions == null || !variant.UnitConversions.Any())
            return null;

        var activeConversions = variant.UnitConversions
            .Where(c => !c.IsDeleted && c.IsActive)
            .ToList();

        if (!activeConversions.Any())
            return null;

        var defaultSaleBarcode = activeConversions
            .Where(c => c.IsDefaultForSale)
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(defaultSaleBarcode))
            return defaultSaleBarcode;

        var baseUnitBarcode = activeConversions
            .Where(c => c.IsBaseUnit)
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(baseUnitBarcode))
            return baseUnitBarcode;

        var fallbackBarcode = activeConversions
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(fallbackBarcode) ? null : fallbackBarcode;
    }

    /// <summary>
    /// Resolve đơn vị bán mặc định khi user bấm chọn tay theo variant.
    ///
    /// Thứ tự ưu tiên:
    /// 1) ProductUnitConversion.IsDefaultForSale = true
    /// 2) ProductUnitConversion.IsBaseUnit = true
    /// 3) conversion active đầu tiên
    /// 4) fallback về base unit của Product
    /// </summary>
    private static PosSellingUnitInfo ResolvePreferredSellingUnit(
    ProductVariant variant,
    string priceTier = CustomerPriceTiers.Retail)
    {
        var product = variant.Product
            ?? throw new InvalidOperationException("Variant thiếu Product navigation.");

        var baseUnitId = product.BaseUnitId;
        var baseUnitName = product.BaseUnit?.Name;

        var activeConversions = (variant.UnitConversions ?? new List<ProductUnitConversion>())
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderByDescending(c => c.IsDefaultForSale)
            .ThenByDescending(c => c.IsBaseUnit)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Factor)
            .ThenBy(c => c.Id)
            .ToList();

        var chosen = activeConversions.FirstOrDefault(c => c.IsDefaultForSale)
                     ?? activeConversions.FirstOrDefault(c => c.IsBaseUnit)
                     ?? activeConversions.FirstOrDefault();

        if (chosen != null)
        {
            return new PosSellingUnitInfo
            {
                ProductUnitConversionId = chosen.Id,
                SellingUnitId = chosen.UnitId,
                SellingUnitName = chosen.Unit?.Name,
                BaseUnitId = baseUnitId,
                BaseUnitName = baseUnitName,
                Multiplier = chosen.Factor <= 0 ? 1m : chosen.Factor,
                UnitPrice = ResolveSalePriceByTier(variant, chosen, priceTier),
                Barcode = ResolvePrimaryBarcode(chosen),
                IsBaseUnit = chosen.IsBaseUnit,
                IsDefaultForSale = chosen.IsDefaultForSale
            };
        }

        // Fallback khi dữ liệu conversion chưa được chuẩn hóa hết
        return new PosSellingUnitInfo
        {
            ProductUnitConversionId = null,
            SellingUnitId = baseUnitId,
            SellingUnitName = baseUnitName,
            BaseUnitId = baseUnitId,
            BaseUnitName = baseUnitName,
            Multiplier = 1m,
            UnitPrice = ResolveSalePriceByTier(variant, null, priceTier),
            Barcode = null,
            IsBaseUnit = true,
            IsDefaultForSale = true
        };
    }
    private static void AddOrMergeLineFromBarcode(Order order, BarcodeLookupResultDto lookup, decimal qty)
    {
        if (qty <= 0) qty = 1;

        var unitPrice = SafePositive(lookup.SellPrice, 0m);
        var multiplier = lookup.Factor <= 0 ? 1m : lookup.Factor;
        var baseQtyToAdd = ComputeBaseQuantity(qty, multiplier);
        var barcodeSource = MapBarcodeSource(lookup.SourceType);

        var existing = order.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
          x.VariantId == lookup.ProductVariantId
&& x.ProductUnitConversionId ==
   lookup.ProductUnitConversionId);

        if (existing != null)
        {
            if (existing.StoreId <= 0 && order.StoreId > 0)
                existing.StoreId = order.StoreId;
            existing.Quantity += qty;
            existing.BaseQuantity += baseQtyToAdd;
            existing.Multiplier = multiplier;
            existing.ScannedBarcode = lookup.Barcode;
            existing.BarcodeSource = barcodeSource;
            existing.ProductUnitConversionId =
    lookup.ProductUnitConversionId;
            existing.SellingUnitId = lookup.UnitId;
            existing.SellingUnitName = lookup.UnitName;
            existing.BaseUnitId = lookup.BaseUnitId;
            existing.BaseUnitName = lookup.BaseUnitName;
            existing.UnitName = lookup.UnitName;
            existing.Barcode = lookup.Barcode;
            return;
        }

        order.Lines.Add(new OrderLine
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            ProductId = lookup.ProductId,
            VariantId = lookup.ProductVariantId,
            ProductUnitConversionId = lookup.ProductUnitConversionId,
            ItemName = lookup.ProductName,
            UnitName = lookup.UnitName,
            Sku = lookup.VariantSku,
            Barcode = lookup.Barcode,

            Quantity = qty,
            UnitPrice = unitPrice,
            LineDiscount = 0m,

            SellingUnitId = lookup.UnitId,
            SellingUnitName = lookup.UnitName,
            BaseUnitId = lookup.BaseUnitId,
            BaseUnitName = lookup.BaseUnitName,
            Multiplier = multiplier,
            BaseQuantity = baseQtyToAdd,

            ScannedBarcode = lookup.Barcode,
            BarcodeSource = barcodeSource
        });
    }



    private async Task<Order> RequireCurrentDraftAsync(CancellationToken ct)
    {
        var currentDraft = await EnsureCurrentCartAsync(ct);
        var order = await _orders.GetByIdAsync(currentDraft.OrderId, ct);

        if (order == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentNotFound,
                message: "Không tìm thấy giỏ hiện tại hợp lệ.",
                actionHint: "Vui lòng tải lại màn hình POS hoặc tạo giỏ mới.",
                metadata: new
                {
                    CurrentOrderId = currentDraft.OrderId
                });
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentInvalidStatus,
                message: "Giỏ hiện tại không còn ở trạng thái có thể chỉnh sửa.",
                actionHint: "Vui lòng chọn một đơn nháp khác hoặc tạo giỏ mới.",
                metadata: new
                {
                    order.Id,
                    order.OrderNumber,
                    Status = order.Status.ToString()
                });
        }

        return order;
    }

    private static bool IsEmptyDraft(Order order)
    {
        if (order.Status != OrderStatus.Draft)
            return false;

        return !HasMeaningfulWork(order);
    }
    private async Task<Order?> FindReusableEmptyDraftAsync(
    int shiftId,
    int? excludeOrderId = null,
    CancellationToken ct = default)
    {
        var drafts = await _orders.GetDraftOrdersByShiftAsync(shiftId, ct);

        return drafts
            .Where(x => !excludeOrderId.HasValue || x.Id != excludeOrderId.Value)
            .Where(IsEmptyDraft)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
    }
    private static void ReversePaymentsFromShift(POSShift shift, Order order)
    {
        var payments = order.Payments.Where(p => !p.IsDeleted).ToList();

        foreach (var payment in payments)
        {
            if (payment.Method == PaymentMethod.Cash)
                shift.CashSalesTotal -= payment.Amount;
            else
                shift.NonCashSalesTotal -= payment.Amount;
        }

        if (shift.CashSalesTotal < 0) shift.CashSalesTotal = 0;
        if (shift.NonCashSalesTotal < 0) shift.NonCashSalesTotal = 0;

        shift.RecalcExpected();
    }

    private static bool CanVoidCompletedOrder(Order order)
    {
        if (order.Status != OrderStatus.Completed)
            return false;

        if (!order.CompletedAtUtc.HasValue)
            return false;

        var completedLocal = order.CompletedAtUtc.Value.ToLocalTime();
        var nowLocal = DateTime.Now;

        if (completedLocal.Date != nowLocal.Date)
            return false;

        if ((nowLocal - completedLocal).TotalHours > 2)
            return false;

        return true;
    }

    private async Task<Warehouse> ResolveWarehouseForOrderAsync(Order order, CancellationToken ct)
    {
        var shift = await _shifts.GetByIdAsync(order.POSShiftId, ct);
        if (shift == null)
            throw new InvalidOperationException("Không tìm thấy ca POS của đơn hàng.");

        if (shift.WarehouseId <= 0)
            throw new InvalidOperationException("Ca POS chưa cấu hình kho xuất bán.");

        var warehouse = await _warehouses.GetByIdAsync(shift.WarehouseId, ct);
        if (warehouse == null)
            throw new InvalidOperationException("Không tìm thấy kho của ca POS.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho của ca POS đã ngưng hoạt động.");

        return warehouse;
    }

    private async Task<FinalizeInventorySummary> ApplyInventoryForFinalizeAsync(Order order, CancellationToken ct)
    {
        var warehouse = await ResolveWarehouseForOrderAsync(order, ct);

        var summary = new FinalizeInventorySummary();

        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
        {
            var qtyToDeduct = line.BaseQuantity > 0 ? line.BaseQuantity : line.Quantity;

            // GHI CHÚ:
            // Không rewrite costing engine.
            // Nếu line đã có provisional unit cost thì truyền xuống request,
            // còn không để movement service xử lý theo engine hiện tại.
            var variant = await _productVariantRepository.GetActiveWithProductAsync(line.VariantId, ct);

            if (variant == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy ProductVariant #{line.VariantId} hoặc variant đã bị inactive/deleted.");
            }

            decimal? provisionalUnitCost = null;

            // 1. ưu tiên provisional đã có sẵn trên line
            if (line.ProvisionalUnitCost.HasValue && line.ProvisionalUnitCost.Value > 0)
            {
                provisionalUnitCost = line.ProvisionalUnitCost.Value;
            }
            // 2. fallback snapshot cost đã có trên line
            else if (line.UnitCostSnapshot.HasValue && line.UnitCostSnapshot.Value > 0)
            {
                provisionalUnitCost = line.UnitCostSnapshot.Value;
            }
            // 3. fallback cost nền từ variant
            else if (variant.CostPrice > 0)
            {
                provisionalUnitCost = variant.CostPrice;
            }

            var movementRequest = _inventoryMovementFactory.CreateSaleFinalize(
                warehouse.Id,
                line.VariantId,
                order.Id,
                line.Id,
                line.ItemName,
                qtyToDeduct,
                provisionalUnitCost,
                DateTime.UtcNow);

            // fallback cuối cho engine hiện tại
            if (provisionalUnitCost.HasValue && provisionalUnitCost.Value > 0)
            {
                movementRequest.ProvisionalUnitCost = provisionalUnitCost.Value;
                movementRequest.UnitCost = provisionalUnitCost.Value;
            }



            // POS finalize mức 2:
            // vẫn complete order dù âm tồn.
            movementRequest.AllowNegativeBalance = true;

            var movement = await _inventoryMovementService.CreateAsync(movementRequest, ct);

            if (movement.IsSkipped || !movement.IsCreated)
                continue;

            // =====================================================
            // Snapshot cost vào OrderLine
            // =====================================================
            var lineCostTotal = Math.Abs(movement.ValueChange);

            line.LineCostTotal = lineCostTotal;

            line.UnitCostSnapshot = qtyToDeduct > 0
                ? Math.Round(lineCostTotal / qtyToDeduct, 6, MidpointRounding.AwayFromZero)
                : 0m;

            line.IsProvisionalCost = movement.HasProvisionalValuation;

            line.ProvisionalUnitCost = movement.HasProvisionalValuation
                ? movement.ProvisionalUnitCost
                : null;

            line.CostSnapshotNote =
                $"Finalize POS. TxId={movement.InventoryTransactionId}; " +
                $"ActualQty={movement.ActualQuantity}; " +
                $"ProvisionalQty={movement.ProvisionalQuantity}; " +
                $"BeforeQty={movement.BeforeQty}; AfterQty={movement.AfterQty}; " +
                $"BeforeAvg={movement.BeforeAverageCost}; AfterAvg={movement.AfterAverageCost}";

            line.GrossProfit = line.LineTotal - line.LineCostTotal;

            var hasNegative = movement.IsNegativeAfterTransaction;
            var hasProvisional = movement.HasProvisionalValuation;

            if (hasNegative || hasProvisional)
            {
                summary.IssueLines.Add(new FinalizeInventoryIssueLine
                {
                    OrderLineId = line.Id,
                    ProductId = line.ProductId,
                    VariantId = line.VariantId,
                    ProductUnitConversionId =
    line.ProductUnitConversionId, // nếu sau này bạn có field riêng thì map lại
                    BarcodeId = null, // hiện chưa thấy bạn có BarcodeId trên OrderLine, tạm để null
                    ItemName = line.ItemName,
                    BaseQuantity = qtyToDeduct,

                    IsNegativeInventory = hasNegative,
                    BeforeQty = movement.BeforeQty,
                    AfterQty = movement.AfterQty,

                    IsProvisionalCost = hasProvisional,
                    UnitCostSnapshot = line.UnitCostSnapshot,
                    ProvisionalUnitCost = line.ProvisionalUnitCost,

                    Note = line.CostSnapshotNote
                });
            }

            if (hasNegative)
                summary.HasNegativeInventory = true;

            if (hasProvisional)
                summary.HasProvisionalCost = true;
        }

        return summary;
    }

    private static FinalizeInventorySummary MapLegalEntityInventorySummary(
        OrderLegalEntityFinalizeResult source)
    {
        return new FinalizeInventorySummary
        {
            HasNegativeInventory = source.HasNegativeInventory,
            HasProvisionalCost = source.HasProvisionalCost,
            IssueLines = source.IssueLines.Select(x => new FinalizeInventoryIssueLine
            {
                OrderLineId = x.OrderLineId,
                ProductId = x.ProductId,
                VariantId = x.VariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                BarcodeId = null,
                ItemName = x.ItemName,
                BaseQuantity = x.BaseQuantity,
                IsNegativeInventory = x.IsNegativeInventory,
                BeforeQty = x.BeforeQty,
                AfterQty = x.AfterQty,
                IsProvisionalCost = x.IsProvisionalCost,
                UnitCostSnapshot = x.UnitCostSnapshot,
                ProvisionalUnitCost = x.ProvisionalUnitCost,
                Note = x.Note
            }).ToList()
        };
    }

    private static void SyncIssueLines(
    OrderInventoryIssue issue,
    Order order,
    FinalizeInventorySummary summary,
    DateTime now)
    {
        // Xóa mềm các line cũ chưa xóa để tránh trùng khi reopen/finalize lại
        foreach (var existing in issue.Lines.Where(x => !x.IsDeleted))
        {
            existing.IsDeleted = true;
            existing.DeletedAtUtc = now;
        }

        foreach (var src in summary.IssueLines)
        {
            var orderLine = order.Lines.FirstOrDefault(x => x.Id == src.OrderLineId && !x.IsDeleted);

            var negativeBefore = Math.Max(0m, -src.BeforeQty);
            var negativeAfter = Math.Max(0m, -src.AfterQty);

            // Chỉ lấy phần âm PHÁT SINH THÊM của riêng order line này,
            // không lấy âm lũy kế sau bán.
            var negativeQty = src.IsNegativeInventory
                ? Math.Max(0m, negativeAfter - negativeBefore)
                : 0m;

            issue.Lines.Add(new OrderInventoryIssueLine
            {
                StoreId = issue.StoreId,
                OrderInventoryIssueId = issue.Id,
                OrderId = order.Id,
                OrderLineId = src.OrderLineId,

                ProductId = src.ProductId,
                ProductVariantId = src.VariantId,
                ProductUnitConversionId = src.ProductUnitConversionId,
                BarcodeId = src.BarcodeId,

                OrderedQty = src.BaseQuantity,
                StockBefore = src.BeforeQty,
                StockAfter = src.AfterQty,

                NegativeQty = negativeQty,

                ProvisionalUnitCost = src.IsProvisionalCost
                    ? src.ProvisionalUnitCost
                    : null,

                ProvisionalCostAmount = src.IsProvisionalCost && src.ProvisionalUnitCost.HasValue
                    ? Math.Round(negativeQty * src.ProvisionalUnitCost.Value, 6, MidpointRounding.AwayFromZero)
                    : null,

                RevaluationAmount = null,

                IsResolved = false,
                ResolvedAtUtc = null
            });
        }
    }
    private static InventoryIssueReasonType ResolveInventoryIssueReasonType(FinalizeInventorySummary summary)
    {
        // GHI CHÚ:
        // InventoryIssueReasonType hiện tại là enum "nguyên nhân gốc".
        // Tại thời điểm finalize POS, hệ thống CHƯA đủ dữ kiện để kết luận
        // nguyên nhân gốc thật sự là gì.
        //
        // Vì vậy:
        // - có issue khi finalize => ReasonType = Unknown
        // - root cause sẽ được cập nhật ở bước xử lý case sau
        return InventoryIssueReasonType.Unknown;
    }
    private static string BuildInventoryIssueInternalSummary(FinalizeInventorySummary summary)
    {
        var issueCount = summary.IssueLines?.Count ?? 0;

        var parts = new List<string>();

        if (summary.HasNegativeInventory)
            parts.Add("âm tồn");

        if (summary.HasProvisionalCost)
            parts.Add("provisional cost");

        var reasonText = parts.Count > 0
            ? string.Join(" / ", parts)
            : "inventory issue";

        return $"Phát hiện {reasonText} sau finalize. " +
               $"Số dòng issue: {issueCount}. " +
               $"Chi tiết xem tại OrderInventoryIssueLines.";
    }
    private static string BuildInventoryIssueInternalNote(FinalizeInventorySummary summary)
    {
        var parts = new List<string>();

        if (summary.HasNegativeInventory)
            parts.Add("Phát hiện âm tồn sau finalize");

        if (summary.HasProvisionalCost)
            parts.Add("Phát hiện provisional cost sau finalize");

        if (summary.IssueLines.Count > 0)
            parts.Add($"Số dòng issue: {summary.IssueLines.Count}");

        foreach (var line in summary.IssueLines)
        {
            var lineParts = new List<string>
        {
            $"LineId={line.OrderLineId}",
            $"VariantId={line.VariantId}",
            $"Qty={line.BaseQuantity}"
        };

            if (line.IsNegativeInventory)
                lineParts.Add($"NegativeQty: {line.BeforeQty} -> {line.AfterQty}");

            if (line.IsProvisionalCost)
                lineParts.Add($"ProvisionalUnitCost={line.ProvisionalUnitCost}");

            if (!string.IsNullOrWhiteSpace(line.Note))
                lineParts.Add(line.Note);

            parts.Add(string.Join(" | ", lineParts));
        }

        return string.Join(Environment.NewLine, parts);
    }

    private async Task UpsertPendingInventoryIssueAsync(
     Order order,
     FinalizeInventorySummary summary,
     CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // =========================================================
        // KHÔNG CÓ ISSUE
        // =========================================================
        if (!summary.HasNegativeInventory && !summary.HasProvisionalCost)
        {
            order.HasInventoryIssue = false;
            order.InventoryResolutionStatus = InventoryResolutionStatus.None;
            order.InventoryIssueOpenedAtUtc = null;
            order.InventoryIssueApprovedAtUtc = null;
            return;
        }

        // =========================================================
        // CÓ ISSUE => ORDER VẪN COMPLETE, NHƯNG MỞ CASE HẬU KIỂM
        // =========================================================
        var reasonType = ResolveInventoryIssueReasonType(summary);
        var internalNote = BuildInventoryIssueInternalSummary(summary);

        var issue = order.InventoryIssue
            ?? await _orderInventoryIssues.GetByOrderIdAsync(order.Id, ct);

        if (issue == null)
        {
            var dueAtUtc = now.AddDays(1);

            issue = new OrderInventoryIssue
            {
                StoreId = order.StoreId,
                OrderId = order.Id,
                Code = $"INVISS-{now:yyyyMMddHHmmss}-{order.Id}",
                Status = InventoryResolutionStatus.PendingResolution,
                OpenedAtUtc = now,
                DueAtUtc = dueAtUtc,
                ReadyForApprovalAtUtc = null,
                ApprovedAtUtc = null,
                ApprovedByUserId = null,
                RejectedAtUtc = null,
                RejectedByUserId = null,
                ReasonType = reasonType,
                InternalNote = TrimText(internalNote, 1000),
                IsOverdue = false
            };
            issue.Severity = CalculateSeverity(issue, now);
            await _orderInventoryIssues.AddAsync(issue, ct);

            // Save trước để lấy IssueId
            await _orderInventoryIssues.SaveChangesAsync(ct);

            order.InventoryIssue = issue;
            // Đồng bộ line issue từ summary vào entity
            SyncIssueLines(issue, order, summary, now);

            // Timeline: CaseCreated
            await _orderInventoryIssues.AddActionAsync(new OrderInventoryIssueAction
            {
                StoreId = issue.StoreId,
                OrderInventoryIssueId = issue.Id,
                ActionType = InventoryIssueActionType.CaseCreated,
                ActorUserId = null, // nếu sau này có current user thì gắn vào đây
                ActionAtUtc = now,
                ReferenceType = InventoryIssueReferenceType.Order,
                ReferenceId = issue.OrderId,
                Note = "Case inventory issue được tạo từ luồng POS finalize."
            }, ct);

            // Timeline: NegativeDetected
            await _orderInventoryIssues.AddActionAsync(new OrderInventoryIssueAction
            {
                StoreId = issue.StoreId,
                OrderInventoryIssueId = issue.Id,
                ActionType = InventoryIssueActionType.NegativeDetected,
                ActorUserId = null,
                ActionAtUtc = now,
                ReferenceType = InventoryIssueReferenceType.Order,
                ReferenceId = issue.OrderId,
                Note = TrimText(internalNote, 1000)
            }, ct);

            await _orderInventoryIssues.SaveChangesAsync(ct);
        }
        else
        {
            // =====================================================
            // Nếu order đã có case thì reopen case về PendingResolution
            // =====================================================
            var wasClosed =
                issue.Status == InventoryResolutionStatus.Approved ||
                issue.Status == InventoryResolutionStatus.Rejected;

            if (issue.OpenedAtUtc == default)
                issue.OpenedAtUtc = now;

            if (issue.DueAtUtc == default)
                issue.DueAtUtc = issue.OpenedAtUtc.AddDays(1);

            issue.Status = InventoryResolutionStatus.PendingResolution;
            issue.Severity = CalculateSeverity(issue, now);

            // Reset nhánh duyệt/từ chối
            issue.ReadyForApprovalAtUtc = null;
            issue.ApprovedAtUtc = null;
            issue.ApprovedByUserId = null;
            issue.RejectedAtUtc = null;
            issue.RejectedByUserId = null;

            issue.ReasonType = reasonType;
            issue.InternalNote = internalNote;
            issue.IsOverdue = now > issue.DueAtUtc;
            // Đồng bộ line issue từ summary vào entity
            SyncIssueLines(issue, order, summary, now);
            _orderInventoryIssues.Update(issue);
            await _orderInventoryIssues.SaveChangesAsync(ct);

            // Nếu case trước đó đã closed rồi mà mở lại, append Reopened
            if (wasClosed)
            {
                await _orderInventoryIssues.AddActionAsync(new OrderInventoryIssueAction
                {
                    StoreId = issue.StoreId,
                    OrderInventoryIssueId = issue.Id,
                    ActionType = InventoryIssueActionType.Reopened,
                    ActorUserId = null,
                    ActionAtUtc = now,
                    ReferenceType = InventoryIssueReferenceType.Order,
                    ReferenceId = issue.OrderId,
                    Note = "Case được mở lại do phát sinh issue mới từ POS finalize."
                }, ct);
            }

            // Ghi nhận lại negative/provisional detect
            await _orderInventoryIssues.AddActionAsync(new OrderInventoryIssueAction
            {
                StoreId = issue.StoreId,
                OrderInventoryIssueId = issue.Id,
                ActionType = InventoryIssueActionType.NegativeDetected,
                ActorUserId = null,
                ActionAtUtc = now,
                ReferenceType = InventoryIssueReferenceType.Order,
                ReferenceId = issue.OrderId,
                Note = internalNote
            }, ct);

            await _orderInventoryIssues.SaveChangesAsync(ct);
        }

        // =========================================================
        // MIRROR TRẠNG THÁI VỀ ORDER
        // =========================================================
        order.HasInventoryIssue = true;
        order.InventoryResolutionStatus = InventoryResolutionStatus.PendingResolution;
        order.InventoryIssueOpenedAtUtc ??= now;
        order.InventoryIssueApprovedAtUtc = null;
    }
    private static InventoryIssueSeverity CalculateSeverity(OrderInventoryIssue issue, DateTime now)
    {
        if (issue.Status == InventoryResolutionStatus.Approved)
            return InventoryIssueSeverity.Normal;

        var totalHours = (now - issue.OpenedAtUtc).TotalHours;
        var dueHours = (issue.DueAtUtc - issue.OpenedAtUtc).TotalHours;

        if (now > issue.DueAtUtc)
        {
            // quá hạn
            if ((now - issue.DueAtUtc).TotalHours > 24)
                return InventoryIssueSeverity.Critical;

            return InventoryIssueSeverity.Overdue;
        }

        // gần tới hạn
        if (totalHours / dueHours > 0.7)
            return InventoryIssueSeverity.Warning;

        return InventoryIssueSeverity.Normal;
    }
    private async Task ApplyInventoryForVoidAsync(Order order, string reason, CancellationToken ct)
    {
        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
        {
            // Lấy valuation gốc của line bán
            var sourceEntries = await _inventoryValuationEntryRepository.GetByReferenceAsync(
                InventoryReferenceType.Order,
                order.Id.ToString(),
                line.Id,
                ct);

            if (sourceEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy valuation gốc của OrderLine #{line.Id} để void.");
            }

            // Chỉ lấy outbound sale gốc
            var outboundEntries = sourceEntries
                .Where(x => x.EntryType == InventoryValuationEntryType.Outbound)
                .OrderByDescending(x => x.Id)
                .ToList();

            if (outboundEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"OrderLine #{line.Id} không có outbound valuation để void.");
            }

            // GHI CHÚ:
            // Void mức 2 = mirror từng valuation entry gốc.
            // Nếu line bán split thành actual/provisional thì nhập lại cũng split tương ứng.
            foreach (var entry in outboundEntries)
            {
                var qtyToAddBack = Math.Abs(entry.Quantity);
                if (qtyToAddBack <= 0)
                    continue;

                if (entry.UnitCost <= 0)
                {
                    throw new InvalidOperationException(
                        $"Valuation entry #{entry.Id} không có UnitCost hợp lệ để void.");
                }

                var movementRequest = _inventoryMovementFactory.CreateSaleVoid(
      entry.WarehouseId,
      line.VariantId,
      order.Id,
      line.Id,
      qtyToAddBack,
      entry.UnitCost,
      reason,
      DateTime.UtcNow);

                // Subkey để phân biệt từng valuation part
                movementRequest.ReferenceSubKey = $"void-from-val-{entry.Id}";
                movementRequest.SourceValuationEntryId = entry.Id;
                movementRequest.SourceReferenceSubKey = entry.ReferenceSubKey;

                // GHI CHÚ:
                // Nếu inbound part này mirror từ provisional outbound,
                // ta giữ cờ để movement service/valuation layer biết đây là reversal của provisional part.
                movementRequest.ForceProvisionalWhenNegative = entry.IsProvisional;

                await _inventoryMovementService.CreateAsync(movementRequest, ct);
            }
        }
    }

    private async Task ApplyInventoryForRefundAsync(Order order, string reason, CancellationToken ct)
    {
        var warehouse = await ResolveWarehouseForOrderAsync(order, ct);

        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
        {
            var qtyToAddBack = line.BaseQuantity > 0 ? line.BaseQuantity : line.Quantity;

            var unitCost = line.UnitCostSnapshot.Value;
            if (unitCost <= 0)
            {
                throw new InvalidOperationException(
                    $"OrderLine #{line.Id} chưa có UnitCostSnapshot hợp lệ để refund.");
            }

            var movementRequest = _inventoryMovementFactory.CreateSaleRefund(
                warehouse.Id,
                line.VariantId,
                order.Id,
                line.Id,
                qtyToAddBack,
                unitCost,
                reason,
                DateTime.UtcNow);

            await _inventoryMovementService.CreateAsync(movementRequest, ct);
        }
    }

    public async Task<OrderReceiptDto> RefundCompletedOrderAsync(
     int orderId,
     string reason,
     PaymentMethod refundMethod,
     string? refundReferenceCode = null,
     string? refundProvider = null,
     CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Lý do trả hàng / hoàn tiền không được để trống.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var order = await _orders.GetByIdWithDetailsAsync(orderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

            if (order.Status != OrderStatus.Completed)
                throw new InvalidOperationException("Chỉ được refund toàn phần cho đơn đã hoàn tất.");

            var alreadyRefunded = await _salesReturns.GetRefundedTotalByOrderAsync(order.Id, ct);

            if (alreadyRefunded > 0)
            {
                throw new InvalidOperationException(
                    "Đơn này đã có phát sinh hoàn tiền trước đó. Vui lòng dùng chức năng Trả hàng / hoàn tiền để xử lý phần còn lại.");
            }

            var refundAmount = order.PaidTotal;

            if (refundAmount <= 0)
                throw new InvalidOperationException("Đơn hàng chưa có số tiền thanh toán để hoàn.");

            var oldStatus = order.Status.ToString();
            var oldPaymentStatus = order.PaymentStatus.ToString();

            var cleanReferenceCode = string.IsNullOrWhiteSpace(refundReferenceCode)
                ? null
                : refundReferenceCode.Trim();

            var cleanProvider = string.IsNullOrWhiteSpace(refundProvider)
                ? null
                : refundProvider.Trim();

            var request = new GaoApp.Application.DTOs.Returns.CreateSalesReturnRequest
            {
                OrderId = order.Id,
                POSShiftId = order.POSShiftId,
                Type = SalesReturnType.ReturnAndRefund,
                Reason = reason,
                Note = $"Refund toàn phần từ flow cũ. Phương thức hoàn tiền thực tế: {refundMethod}.",

                Lines = order.Lines
                    .Where(x => !x.IsDeleted)
                    .Select(x => new GaoApp.Application.DTOs.Returns.CreateSalesReturnLineRequest
                    {
                        OrderLineId = x.Id,
                        ReturnQuantity = x.Quantity,
                        ReturnBaseQuantity = x.BaseQuantity > 0 ? x.BaseQuantity : x.Quantity,
                        RefundUnitAmount = x.Quantity > 0 ? (x.LineTotal / x.Quantity) : x.UnitPrice,
                        Action = SalesReturnLineAction.Restock,
                        Reason = reason
                    })
                    .ToList(),

                Payments = new List<GaoApp.Application.DTOs.Returns.CreateSalesReturnPaymentRequest>
                {
                    new GaoApp.Application.DTOs.Returns.CreateSalesReturnPaymentRequest
                    {
                        Method = refundMethod,
                        Amount = refundAmount,
                        ReferenceCode = cleanReferenceCode,
                        Provider = cleanProvider,
                        Note = $"Refund full from order {order.OrderNumber}. Actual refund method: {refundMethod}"
                    }
                }
            };

            // SalesReturnService tham gia transaction đang mở trên cùng AppDbContext.
            // UnitOfWork của service con không commit transaction do service cha sở hữu.
            await _salesReturnService.CreateAsync(request, ct);

            order.Status = OrderStatus.Refunded;
            order.PaymentStatus = PaymentStatus.Refunded;

            var refundNote =
                $"[RETURN/REFUND - {DateTime.Now:dd/MM/yyyy HH:mm:ss}] " +
                $"Refund toàn phần. Số tiền: {refundAmount:n0}. " +
                $"Phương thức hoàn: {refundMethod}. " +
                $"Lý do: {reason}";

            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? refundNote
                : $"{order.Note}{Environment.NewLine}{refundNote}";

            // SalesReturnService đã ghi ReturnDeducted theo các dòng thực trả.
            // Không ghi thêm SaleRefunded để tránh trừ tích lũy hai lần.
            await _rewardVoucherRepository.RestoreUsedVouchersByOrderIdAsync(
                order.Id,
                reason,
                ct);

            await _orders.SaveChangesAsync(ct);

            await WritePosAuditLogAsync(
                action: "ORDER_REFUNDED",
                orderId: order.Id,
                note: reason,
                metadata: new
                {
                    order.OrderNumber,
                    RefundAmount = refundAmount,
                    RefundMethod = refundMethod.ToString(),
                    RefundReferenceCode = cleanReferenceCode,
                    RefundProvider = cleanProvider,
                    Status = order.Status.ToString(),
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Reason = reason
                },
                ct: ct);

            await WriteRefundOrderAuditLogAsync(
                order: order,
                oldStatus: oldStatus,
                oldPaymentStatus: oldPaymentStatus,
                reason: $"{reason} | RefundMethod={refundMethod} | Amount={refundAmount:n0}",
                ct: ct);

            await tx.CommitAsync(ct);

            return await GetReceiptAsync(order.Id, ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> CreateDraftAsync(
     int? customerId = null,
     string? note = null,
     CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        var reusableDraft = await FindReusableEmptyDraftAsync(openShift.Id, null, ct);

        if (reusableDraft != null)
        {
            reusableDraft.CustomerId = customerId;
            reusableDraft.Note = string.IsNullOrWhiteSpace(note) ? reusableDraft.Note : note.Trim();

            openShift.CurrentOrderId = reusableDraft.Id;

            Recalc(reusableDraft);
            await _orders.SaveChangesAsync(ct);

            return reusableDraft.Id;
        }

        var order = new Order
        {
            StoreId = _posContext.StoreId,
            Status = OrderStatus.Draft,
            PaymentStatus = PaymentStatus.Unpaid,
            POSShiftId = openShift.Id,
            CustomerId = customerId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        Recalc(order);

        await _legalEntityFinalizeService.CaptureModeAsync(order, ct);
        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        openShift.CurrentOrderId = order.Id;
        await _orders.SaveChangesAsync(ct);

        return order.Id;
    }
    public async Task<OrderDraftDto> GetDraftAsync(int orderId, CancellationToken ct = default)
    {
        var order = await RequireDraftAsync(orderId, ct);

        await ApplyPromotionsForOrderAsync(order, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }
    private static PosSellingUnitInfo ResolveSellingUnitForManualAdd(
    ProductVariant variant,
    int? productUnitConversionId = null,
    string priceTier = CustomerPriceTiers.Retail)
    {
        if (variant == null)
            throw new ArgumentNullException(nameof(variant));

        var product = variant.Product
            ?? throw new InvalidOperationException("Variant thiếu Product navigation.");

        var baseUnitId = product.BaseUnitId;
        var baseUnitName = product.BaseUnit?.Name;

        // Không truyền conversion => giữ logic cũ
        if (!productUnitConversionId.HasValue || productUnitConversionId.Value <= 0)
            return ResolvePreferredSellingUnit(variant, priceTier);

        var activeConversions = (variant.UnitConversions ?? new List<ProductUnitConversion>())
            .Where(c => !c.IsDeleted && c.IsActive)
            .ToList();

        var chosen = activeConversions.FirstOrDefault(c => c.Id == productUnitConversionId.Value);
        if (chosen == null)
            throw new InvalidOperationException("Không tìm thấy đơn vị quy đổi hợp lệ cho sản phẩm.");

        return new PosSellingUnitInfo
        {
            ProductUnitConversionId = chosen.Id,
            SellingUnitId = chosen.UnitId,
            SellingUnitName = chosen.Unit?.Name,
            BaseUnitId = baseUnitId,
            BaseUnitName = baseUnitName,
            Multiplier = chosen.Factor <= 0 ? 1m : chosen.Factor,
            UnitPrice = ResolveSalePriceByTier(variant, chosen, priceTier),
            Barcode = ResolvePrimaryBarcode(chosen),
            IsBaseUnit = chosen.IsBaseUnit,
            IsDefaultForSale = chosen.IsDefaultForSale
        };
    }
    public async Task<OrderDraftDto> AddItemAsync(
    int orderId,
    int variantId,
    int? productUnitConversionId = null,
    decimal qty = 1,
    CancellationToken ct = default)
    {
        if (qty <= 0)
            qty = 1;

        var order = await RequireDraftAsync(orderId, ct);
        var priceTier = ResolveOrderPriceTier(order);

        var variant = await _variants.GetActiveWithProductAsync(variantId, ct)
                     ?? throw new InvalidOperationException("Variant không tồn tại hoặc đang bị khóa.");

        var product = variant.Product
                      ?? throw new InvalidOperationException("Variant thiếu Product navigation.");
        if (!product.IsActive || !product.IsSellable)
            throw new InvalidOperationException("Sản phẩm chưa được phép bán tại POS.");

        // NEW:
        // nếu có productUnitConversionId => add đúng đơn vị con
        // nếu không => giữ hành vi cũ theo đơn vị ưu tiên mặc định
        var sellingInfo = ResolveSellingUnitForManualAdd(
    variant,
    productUnitConversionId,
    priceTier);


        var unitPrice = SafePositive(sellingInfo.UnitPrice ?? 0m, 0m);
        var barcodeSource = BarcodeLookupSourceType.ManualVariant;
        var barcode = sellingInfo.Barcode;

        var existing = order.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
          x.VariantId == variantId
&& x.ProductUnitConversionId ==
   sellingInfo.ProductUnitConversionId);
        if (existing != null)
        {
            if (existing.StoreId <= 0 && order.StoreId > 0)
                existing.StoreId = order.StoreId;
            existing.Quantity += qty;
            existing.BaseQuantity += ComputeBaseQuantity(qty, sellingInfo.Multiplier);
            existing.Multiplier = sellingInfo.Multiplier;
            existing.ProductUnitConversionId =
    sellingInfo.ProductUnitConversionId;
            existing.SellingUnitId = sellingInfo.SellingUnitId;
            existing.SellingUnitName = sellingInfo.SellingUnitName;
            existing.BaseUnitId = sellingInfo.BaseUnitId;
            existing.BaseUnitName = sellingInfo.BaseUnitName;
            existing.UnitName = sellingInfo.SellingUnitName;
            existing.ScannedBarcode = barcode;
            existing.Barcode = barcode;
            existing.BarcodeSource = barcodeSource;
        }
        else
        {
            order.Lines.Add(new OrderLine
            {
                StoreId = order.StoreId,
                OrderId = order.Id,
                ProductId = variant.ProductId,
                VariantId = variant.Id,
                ItemName = product.Name,

                UnitName = sellingInfo.SellingUnitName,

                Sku = variant.Sku,
                Barcode = barcode,

                Quantity = qty,
                BaseQuantity = ComputeBaseQuantity(qty, sellingInfo.Multiplier),
                Multiplier = sellingInfo.Multiplier,
                ProductUnitConversionId = sellingInfo.ProductUnitConversionId,

                SellingUnitId = sellingInfo.SellingUnitId,
                SellingUnitName = sellingInfo.SellingUnitName,
                BaseUnitId = sellingInfo.BaseUnitId,
                BaseUnitName = sellingInfo.BaseUnitName,
                ScannedBarcode = barcode,
                BarcodeSource = barcodeSource,

                UnitPrice = unitPrice,
                LineDiscount = 0m
            });
        }


        await ApplyBestPackPriceForVariantLinesAsync(
     order,
     variant.Id,
     ct);

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }

    public async Task<OrderDraftDto> AddItemByBarcodeAsync(int orderId, string barcode, decimal qty = 1, CancellationToken ct = default)
    {
        barcode = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            throw new InvalidOperationException("Barcode rỗng.");

        if (qty <= 0)
            qty = 1;

        var order = await RequireDraftAsync(orderId, ct);

        var lookup = await _barcodeLookup.FindAsync(barcode, ct);

        // Fallback tạm thời cho barcode history variant-level
        // (nếu hệ thống history hiện tại vẫn đang map về ProductVariant)
        if (lookup == null)
        {
            var variantId = await _variants.ResolveVariantIdByBarcodeHistoryAsync(barcode, ct);
            if (variantId != null)
            {
                var variant = await _variants.GetActiveWithProductAsync(variantId.Value, ct);
                if (variant != null)
                {
                    var product = variant.Product
                                  ?? throw new InvalidOperationException("Variant thiếu Product navigation.");
                    var priceTier = ResolveOrderPriceTier(order);
                    var sellingInfo = ResolvePreferredSellingUnit(variant, priceTier);

                    lookup = new BarcodeLookupResultDto
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,

                        ProductVariantId = variant.Id,
                        VariantSku = variant.Sku,

                        ProductUnitConversionId = sellingInfo.ProductUnitConversionId,

                        UnitId = sellingInfo.SellingUnitId,
                        UnitName = sellingInfo.SellingUnitName,

                        BaseUnitId = sellingInfo.BaseUnitId,
                        BaseUnitName = sellingInfo.BaseUnitName,

                        Factor = sellingInfo.Multiplier <= 0 ? 1m : sellingInfo.Multiplier,
                        IsBaseUnit = sellingInfo.IsBaseUnit,
                        IsDefaultForSale = sellingInfo.IsDefaultForSale,

                        CostPrice = variant.CostPrice,
                        SellPrice = sellingInfo.UnitPrice ?? 0m,

                        // Khi lookup bằng barcode history, barcode hiện tại quét vào chính là barcode nguồn
                        Barcode = barcode,
                        SourceType = "BarcodeHistory"
                    };
                }
            }
        }

        if (lookup == null)
            throw new InvalidOperationException("Không tìm thấy sản phẩm theo barcode.");
        lookup = await ApplyPriceTierToBarcodeLookupAsync(order, lookup, ct);

        AddOrMergeLineFromBarcode(order, lookup, qty);
        await ApplyBestPackPriceForVariantLinesAsync(
      order,
      lookup.ProductVariantId,
      ct);

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }
    public async Task<OrderDraftDto> UpdateLineQtyAsync(int lineId, decimal qty, CancellationToken ct = default)
    {
        if (qty <= 0) qty = 1;

        var line = await _orders.GetDraftLineAsync(lineId, ct)
                   ?? throw new InvalidOperationException("Line không tồn tại hoặc đơn không còn Draft.");

        var order = await RequireDraftAsync(line.OrderId, ct);

        line.Quantity = qty;

        var multiplier = line.Multiplier <= 0 ? 1m : line.Multiplier;
        line.BaseQuantity = qty * multiplier;

        await ApplyBestPackPriceForVariantLinesAsync(
     order,
     line.VariantId,
     ct);

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }
    public async Task<OrderDraftDto> RemoveLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _orders.GetDraftLineAsync(lineId, ct)
                   ?? throw new InvalidOperationException("Line không tồn tại hoặc đơn không còn Draft.");

        line.IsDeleted = true;

        var order = await RequireDraftAsync(line.OrderId, ct);

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }

    public async Task<OrderDraftDto> AddPaymentAsync(int orderId, UpsertPaymentRequest dto, CancellationToken ct = default)
    {
        if (dto.Amount <= 0)
            throw new InvalidOperationException("Số tiền thanh toán phải > 0.");

        var order = await RequireDraftAsync(orderId, ct);

        if (!order.Lines.Any(x => !x.IsDeleted))
            throw new InvalidOperationException("Không thể thanh toán: giỏ hiện tại chưa có sản phẩm.");

        Recalc(order);

        if (order.GrandTotal <= 0)
            throw new InvalidOperationException("Không thể thanh toán: tổng tiền đơn hàng không hợp lệ.");

        var currentPaid = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
        var willPaid = currentPaid + dto.Amount;

        if (dto.Method != PaymentMethod.Cash && willPaid > order.GrandTotal)
            throw new InvalidOperationException("Phương thức này không cho phép thanh toán dư (overpay).");

        order.Payments.Add(new OrderPayment
        {
            OrderId = order.Id,
            Method = dto.Method,
            Amount = dto.Amount,
            ReferenceCode = dto.ReferenceCode,
            Provider = dto.Provider,
            PaidAtUtc = DateTime.UtcNow
        });

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }

    public async Task<OrderDraftDto> RemovePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _payments.GetDraftPaymentAsync(paymentId, ct)
            ?? throw new InvalidOperationException("Payment không tồn tại hoặc đơn không còn Draft.");

        payment.IsDeleted = true;

        var order = await RequireDraftAsync(payment.OrderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }

    public async Task<OrderDraftDto> FinalizeAsync(int orderId, CancellationToken ct = default)
    {
        // Transaction ngoài cùng:
        // nếu bất kỳ bước nào lỗi thì rollback toàn bộ:
        // - consume reservation
        // - ghi inventory movement
        // - update order
        // - update shift
        // - audit log
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var order = await RequireDraftAsync(orderId, ct);

            // Lưu trạng thái cũ để ghi audit old/new values
            var oldStatus = order.Status.ToString();
            var oldPaymentStatus = order.PaymentStatus.ToString();
            await ApplyPromotionsForOrderAsync(order, ct);
            Recalc(order);

            if (!order.Lines.Any(l => !l.IsDeleted))
                throw new InvalidOperationException("Không thể chốt đơn: đơn chưa có sản phẩm.");

            if (order.GrandTotal <= 0)
                throw new InvalidOperationException("Không thể chốt đơn: tổng tiền không hợp lệ.");

            if (order.BalanceDue > 0)
                throw new InvalidOperationException("Không thể chốt đơn: chưa thanh toán đủ.");

            var shift = await _shifts.GetByIdAsync(order.POSShiftId, ct);
            if (shift == null)
                throw new InvalidOperationException("Không tìm thấy ca POS của đơn hàng.");

            if (shift.Status != POSShiftStatus.Open)
                throw new InvalidOperationException("Không thể chốt đơn: ca POS của đơn đã đóng.");

            if (shift.WarehouseId <= 0)
                throw new InvalidOperationException("Ca POS chưa cấu hình kho xuất bán.");

            // Nếu đơn trước đó từng OnHold và còn reservation active,
            // phải consume reservation trước khi ghi xuất kho thực tế.
            if (await _inventoryReservationService.HasActiveReservationForOrderAsync(order, ct))
            {
                await _inventoryReservationService.ConsumeForOrderAsync(order, ct);
            }

            // Chỉ khi finalize thành công mới ghi xuất kho thực tế.
            // Feature tắt: giữ nguyên tuyệt đối luồng xuất kho theo ca hiện hữu.
            // Feature bật: khóa tồn, allocation theo HKD và movement được thực hiện
            // trong cùng transaction ngoài cùng này.
            var legalEntityFinalize = await _legalEntityFinalizeService
                .ApplyIfEnabledAsync(order, ct);

            var finalizeInventorySummary = legalEntityFinalize.WasApplied
                ? MapLegalEntityInventorySummary(legalEntityFinalize)
                : await ApplyInventoryForFinalizeAsync(order, ct);

            // Bán hàng vẫn complete bình thường
            order.Status = OrderStatus.Completed;
            order.PaymentStatus = PaymentStatus.Paid;
            order.CompletedAtUtc = DateTime.UtcNow;

            // Đánh dấu voucher đã dùng khi đơn chốt thành công
            MarkRewardVouchersAsUsed(order);

            // Nhưng inventory/cost issue thì đi luồng riêng
            await UpsertPendingInventoryIssueAsync(order, finalizeInventorySummary, ct);

            if (string.IsNullOrWhiteSpace(order.OrderNumber))
                order.OrderNumber = await _orderNo.NextAsync(ct);

            // Cộng tích điểm sau khi đơn đã Completed/Paid và đã có OrderNumber.
            // Chỉ Add ledger, SaveChanges sẽ dùng chung với transaction finalize bên dưới.
            await ApplyRewardForFinalizedOrderAsync(order, ct);

            ApplyPaymentsToShift(shift, order);

            if (shift.CurrentOrderId == order.Id)
            {
                shift.CurrentOrderId = null;
            }

            // Lưu thay đổi dữ liệu nghiệp vụ trước
            await _orders.SaveChangesAsync(ct);

            // Ghi POS audit log riêng cho timeline order/POS
            await WritePosAuditLogAsync(
                action: "ORDER_FINALIZED",
                orderId: order.Id,
                note: $"Tổng tiền: {order.GrandTotal:n0}",
                metadata: new
                {
                    order.OrderNumber,
                    order.GrandTotal,
                    Status = order.Status.ToString(),
                    PaymentStatus = order.PaymentStatus.ToString(),
                    order.CompletedAtUtc,
                    order.LegalEntityCount,
                    order.HasMultipleLegalEntities,
                    order.LegalEntityAllocatedAtUtc
                },
                ct: ct);

            // Ghi audit log chung toàn hệ thống
            await WriteFinalizeOrderAuditLogAsync(
                order: order,
                oldStatus: oldStatus,
                oldPaymentStatus: oldPaymentStatus,
                ct: ct);

            await tx.CommitAsync(ct);

            // =====================================================
            // Phase 3.5:
            // Tự sinh Invoice sau khi POS finalize đã commit.
            // Không để lỗi Invoice làm fail POS.
            // =====================================================
            await TryGenerateInvoiceAfterFinalizeAsync(order.Id, ct);

            return await MapAsync(order, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            // Ghi log thất bại cho audit log chung nếu đã load được order
            try
            {
                await _auditLogService.WriteAsync(new WriteAuditLogRequest
                {
                    Module = AuditModuleType.Orders,
                    ActionType = AuditActionType.FinalizeOrder,
                    EntityName = nameof(Order),
                    EntityId = orderId.ToString(),
                    EntityDisplay = $"Đơn hàng #{orderId}",
                    Summary = $"Finalize order thất bại cho order #{orderId}",
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                }, ct);
            }
            catch
            {
                // Không throw lại để tránh ghi log lỗi làm mất exception chính.
            }

            throw;
        }
    }

    public async Task CancelAsync(int orderId, string? reason = null, CancellationToken ct = default)
    {
        // Transaction ngoài cùng:
        // - release mọi reservation active của order (kể cả Draft vừa resume từ OnHold)
        // - cập nhật status order
        // - cập nhật current cart nếu cần
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var order = await _orders.GetByIdAsync(orderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

            if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.OnHold)
                throw new InvalidOperationException("Chỉ được hủy đơn Draft hoặc đơn đang giữ.");

            var openShift = await RequireCurrentOpenShiftAsync(ct);
            EnsureShiftOwnership(openShift);
            if (openShift != null && openShift.CurrentOrderId == order.Id)
            {
                openShift.CurrentOrderId = null;
            }

            // Luôn release theo dữ liệu reservation thực tế, không suy đoán bằng Status.
            // Một đơn OnHold sau khi Resume sẽ trở lại Draft nhưng reservation vẫn còn
            // active cho tới khi finalize/cancel. Chỉ kiểm tra Status từng làm reservation
            // của Draft-resumed bị mồ côi khi hủy giỏ.
            await _inventoryReservationService.ReleaseForOrderAsync(
                order,
                string.IsNullOrWhiteSpace(reason) ? "Hủy đơn/giỏ và nhả giữ hàng." : reason,
                ct);

            order.Status = OrderStatus.Cancelled;

            if (!string.IsNullOrWhiteSpace(reason))
            {
                var r = reason.Trim();
                order.Note = string.IsNullOrWhiteSpace(order.Note)
                    ? $"[CANCEL] {r}"
                    : $"{order.Note}{Environment.NewLine}[CANCEL] {r}";
            }

            await _orders.SaveChangesAsync(ct);
            // Ghi POS audit log riêng
            await WritePosAuditLogAsync(
                action: "ORDER_CANCELLED",
                orderId: order.Id,
                note: string.IsNullOrWhiteSpace(reason) ? "Hủy đơn" : reason,
                metadata: new
                {
                    order.OrderNumber,
                    Status = order.Status.ToString(),
                    Reason = reason
                },
                ct: ct);

            // Ghi audit log chung
            await _auditLogService.WriteAsync(new WriteAuditLogRequest
            {
                Module = AuditModuleType.Orders,
                ActionType = AuditActionType.CancelOrder,
                EntityName = nameof(Order),
                EntityId = order.Id.ToString(),
                EntityDisplay = string.IsNullOrWhiteSpace(order.OrderNumber)
                    ? $"Đơn hàng #{order.Id}"
                    : $"Đơn hàng {order.OrderNumber}",
                Summary = string.IsNullOrWhiteSpace(reason)
                    ? $"Hủy đơn #{order.Id}"
                    : $"Hủy đơn #{order.Id} - Lý do: {reason}",
                NewValuesJson = JsonSerializer.Serialize(new
                {
                    Status = order.Status.ToString(),
                    Reason = reason
                }),
                ChangedColumnsJson = JsonSerializer.Serialize(new[]
                {
        "Status",
        "Reason"
    }),
                IsSuccess = true
            }, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<OrderReceiptDto> GetReceiptAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithDetailsAsync(orderId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");
        var refundedTotal = await _salesReturns.GetRefundedTotalByOrderAsync(order.Id, ct);

        var refundableRemaining = order.PaidTotal - refundedTotal;
        if (refundableRemaining < 0)
            refundableRemaining = 0;
        return new OrderReceiptDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            CreatedAtUtc = order.CreatedAtUtc,
            FinalizedAtUtc = order.CompletedAtUtc,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name,
            CustomerPhone = order.Customer?.Phone,

            CashierName = null,
            ShiftCode = order.POSShift?.ShiftCode,
            Note = order.Note,
            StoreName = order.Store?.Name ?? "GaoApp POS",
            StoreAddress = null,
            StorePhone = null,
            Subtotal = order.Subtotal,
            DiscountTotal = order.DiscountTotal,
            GrandTotal = order.GrandTotal,
            VoucherDiscountTotal = order.VoucherDiscountTotal,
            RewardVouchers = order.RewardVouchers
    .Where(x => !x.IsDeleted)
    .OrderBy(x => x.Id)
    .Select(x => new OrderRewardVoucherDto
    {
        VoucherId = x.VoucherId,
        VoucherCode = x.Voucher != null ? x.Voucher.VoucherCode : "",
        Value = x.VoucherValue,
        Status = x.Voucher != null ? x.Voucher.Status.ToString() : null
    })
    .ToList(),
            PaidTotal = order.PaidTotal,
            BalanceDue = order.BalanceDue,
            ChangeDue = order.ChangeDue,
            TotalLines = order.Lines?.Count ?? 0,
            TotalQuantity = order.Lines?.Sum(x => x.Quantity) ?? 0,
            RefundedTotal = refundedTotal,
            RefundableRemaining = refundableRemaining,

            Lines = order.Lines.Select(x =>
            {
                var img = ResolveVariantImage(x.Variant);

                return new OrderLineDto
                {
                    LineId = x.Id,
                    VariantId = x.VariantId,
                    ItemName = x.ItemName,
                    ProductVariantName = x.Variant != null ? x.Variant.ProductVariantName : null,
                    UnitName = x.UnitName,
                    Sku = x.Sku ?? string.Empty,
                    Barcode = x.Barcode,

                    // ✅ NEW: IMAGE
                    ImageUrl = img.url,
                    ImageThumbUrl = img.thumb,
                    ImageAlt = img.alt,
                    HasImage = img.hasImage,

                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    LineDiscount = x.LineDiscount,
                    LineTotal = x.LineTotal,
                    SellingUnitId = x.SellingUnitId,
                    SellingUnitName = x.SellingUnitName,
                    OriginalUnitPrice = x.OriginalUnitPrice,
                    PromotionDiscount = x.PromotionDiscount,
                    PromotionName = x.PromotionName,
                    BaseUnitId = x.BaseUnitId,
                    BaseUnitName = x.BaseUnitName,
                    Multiplier = x.Multiplier,
                    BaseQuantity = x.BaseQuantity,
                    ScannedBarcode = x.ScannedBarcode,
                    BarcodeSource = x.BarcodeSource
                };
            }).ToList(),
            Payments = order.Payments.Select(p => new OrderPaymentDto
            {
                PaymentId = p.Id,
                Method = p.Method.ToString(),
                Amount = p.Amount,
                Reference = p.ReferenceCode,
                CreatedAt = p.PaidAtUtc
            }).ToList()
        };
    }

    public async Task<PagedResult<OrderListItemDto>> GetOrdersAsync(OrderListQueryDto query, CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var fromLocal = query.FromDate?.Date;
        var toLocalExclusive = query.ToDate?.Date.AddDays(1);

        DateTime? fromUtc = fromLocal.HasValue
            ? DateTime.SpecifyKind(fromLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

        DateTime? toUtcExclusive = toLocalExclusive.HasValue
            ? DateTime.SpecifyKind(toLocalExclusive.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

        var (items, total) = await _orders.QueryOrdersAsync(
            fromUtc: fromUtc,
            toUtcExclusive: toUtcExclusive,
            status: query.Status,
            keyword: query.Keyword,
            page: page,
            pageSize: pageSize,
            ct: ct);

        // =====================================================
        // Lấy danh sách OrderId của page hiện tại
        // để query hậu mãi 1 lần, tránh N+1
        // =====================================================
        var orderIds = items
            .Select(x => x.Id)
            .Distinct()
            .ToList();

        var afterSaleSummaries = await _salesReturns.GetAfterSaleSummaryByOrderIdsAsync(orderIds, ct);

        var afterSaleDict = afterSaleSummaries.ToDictionary(x => x.OrderId, x => x);

        var dtoItems = items.Select(o =>
        {
            afterSaleDict.TryGetValue(o.Id, out var afterSale);

            return new OrderListItemDto
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                Status = o.Status.ToString(),
                PaymentStatus = o.PaymentStatus.ToString(),
                GrandTotal = o.GrandTotal,
                VoucherDiscountTotal = o.VoucherDiscountTotal,
                RewardVouchers = o.RewardVouchers
    .Where(x => !x.IsDeleted)
    .OrderBy(x => x.Id)
    .Select(x => new OrderRewardVoucherDto
    {
        VoucherId = x.VoucherId,
        VoucherCode = x.Voucher != null ? x.Voucher.VoucherCode : "",
        Value = x.VoucherValue,
        Status = x.Voucher != null ? x.Voucher.Status.ToString() : null
    })
    .ToList(),
                PaidTotal = o.PaidTotal,
                BalanceDue = o.BalanceDue,
                CreatedAtUtc = o.CreatedAtUtc,
                CompletedAtUtc = o.CompletedAtUtc,

                // =========================
                // HẬU MÃI
                // =========================
                RefundedTotal = afterSale?.RefundedTotal ?? 0m,
                ReturnCount = afterSale?.ReturnCount ?? 0

            };
        }).ToList();

        return new PagedResult<OrderListItemDto>
        {
            Items = dtoItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    private async Task<Order> RequireDraftAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetDraftAsync(orderId, ct);
        if (order == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentNotFound,
                message: "Không tìm thấy đơn nháp hợp lệ.",
                actionHint: "Đơn có thể đã được giữ, đã hoàn tất hoặc đã hủy. Vui lòng tải lại danh sách đơn.");
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentInvalidStatus,
                message: "Đơn không còn ở trạng thái có thể chỉnh sửa.",
                actionHint: "Vui lòng tải lại trạng thái đơn hoặc chọn đơn khác.",
                metadata: new
                {
                    order.Id,
                    order.OrderNumber,
                    Status = order.Status.ToString()
                });
        }

        return order;
    }

    private static void Recalc(Order order)
    {
        var lines = order.Lines
            .Where(x => !x.IsDeleted)
            .ToList();

        // =========================
        // 1. TÍNH LẠI TỪNG DÒNG
        // =========================
        foreach (var l in lines)
        {
            var grossLineAmount = Math.Round(
                l.Quantity * l.UnitPrice,
                0,
                MidpointRounding.AwayFromZero);

            if (grossLineAmount < 0)
                grossLineAmount = 0;

            if (l.LineDiscount < 0)
                l.LineDiscount = 0;

            if (l.PromotionDiscount < 0)
                l.PromotionDiscount = 0;

            if (l.LineDiscount > grossLineAmount)
                l.LineDiscount = grossLineAmount;

            var remainAfterLineDiscount = Math.Max(
                grossLineAmount - l.LineDiscount,
                0);

            if (l.PromotionDiscount > remainAfterLineDiscount)
                l.PromotionDiscount = remainAfterLineDiscount;

            l.LineTotal = Math.Round(
                grossLineAmount - l.LineDiscount - l.PromotionDiscount,
                0,
                MidpointRounding.AwayFromZero);

            if (l.LineTotal < 0)
                l.LineTotal = 0;
        }

        // =========================
        // 2. TỔNG TIỀN HÀNG
        // =========================
        var subtotal = Math.Round(
            lines.Sum(x => x.Quantity * x.UnitPrice),
            0,
            MidpointRounding.AwayFromZero);

        if (subtotal < 0)
            subtotal = 0;

        var lineDiscountTotal = Math.Round(
            Math.Max(lines.Sum(x => x.LineDiscount), 0),
            0,
            MidpointRounding.AwayFromZero);

        var promotionDiscountTotal = Math.Round(
            Math.Max(lines.Sum(x => x.PromotionDiscount), 0),
            0,
            MidpointRounding.AwayFromZero);

        var comboDiscountTotal = Math.Round(
            Math.Max(order.ComboDiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        order.Subtotal = subtotal;
        order.PromotionDiscountTotal = promotionDiscountTotal;
        order.ComboDiscountTotal = comboDiscountTotal;

        // =========================
        // 3. KHÓA COMBO KHÔNG VƯỢT TIỀN CÒN LẠI
        // =========================
        var maxComboDiscount = Math.Max(
            subtotal - lineDiscountTotal - promotionDiscountTotal,
            0);

        if (order.ComboDiscountTotal > maxComboDiscount)
            order.ComboDiscountTotal = maxComboDiscount;

        comboDiscountTotal = order.ComboDiscountTotal;

        // Nếu combo bị đưa về 0 thì xóa snapshot combo.
        if (comboDiscountTotal <= 0)
        {
            order.ComboPromotionId = null;
            order.ComboPromotionName = null;
            order.ComboPromotionNote = null;
        }

        // =========================
        // 4. GIẢM GIÁ ĐƠN HÀNG
        // =========================
        var maxOrderDiscount = Math.Max(
            subtotal
            - lineDiscountTotal
            - promotionDiscountTotal
            - comboDiscountTotal,
            0);

        if (order.OrderDiscount < 0)
            order.OrderDiscount = 0;

        order.OrderDiscount = Math.Round(
            order.OrderDiscount,
            0,
            MidpointRounding.AwayFromZero);

        if (order.OrderDiscount > maxOrderDiscount)
            order.OrderDiscount = maxOrderDiscount;

        // =========================
        // 5. VOUCHER
        // =========================
        var voucherDiscountTotal = order.RewardVouchers?
            .Where(x => !x.IsDeleted)
            .Sum(x => x.VoucherValue) ?? 0m;

        voucherDiscountTotal = Math.Round(
            Math.Max(voucherDiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        var maxVoucherDiscount = Math.Max(
            subtotal
            - lineDiscountTotal
            - promotionDiscountTotal
            - comboDiscountTotal
            - order.OrderDiscount,
            0);

        if (voucherDiscountTotal > maxVoucherDiscount)
            voucherDiscountTotal = maxVoucherDiscount;

        order.VoucherDiscountTotal = voucherDiscountTotal;

        // =========================
        // 6. TỔNG GIẢM / TỔNG THANH TOÁN
        // =========================
        order.DiscountTotal = Math.Round(
            lineDiscountTotal
            + promotionDiscountTotal
            + comboDiscountTotal
            + order.OrderDiscount
            + order.VoucherDiscountTotal,
            0,
            MidpointRounding.AwayFromZero);

        order.GrandTotal = Math.Round(
            Math.Max(order.Subtotal - order.DiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        // =========================
        // 7. THANH TOÁN
        // =========================
        order.PaidTotal = Math.Round(
            order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount),
            0,
            MidpointRounding.AwayFromZero);

        if (order.PaidTotal < 0)
            order.PaidTotal = 0;

        order.BalanceDue = Math.Max(order.GrandTotal - order.PaidTotal, 0);
        order.ChangeDue = Math.Max(order.PaidTotal - order.GrandTotal, 0);

        if (order.PaidTotal <= 0)
            order.PaymentStatus = PaymentStatus.Unpaid;
        else if (order.PaidTotal < order.GrandTotal)
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
        else
            order.PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>
    /// Áp lại khuyến mãi cho toàn bộ line trong đơn.
    /// Gọi sau khi giá bán đã được tính xong:
    /// - giá lẻ
    /// - giá sỉ
    /// - giá lốc/thùng
    /// </summary>
    private async Task ApplyPromotionsForOrderAsync(
        Order order,
        CancellationToken ct = default)
    {
        if (order == null)
            return;

        if (order.StoreId <= 0)
            return;

        var hasLines = order.Lines.Any(x => !x.IsDeleted);
        if (!hasLines)
            return;

        // Query DB đúng 2 lần cho cả giỏ:
        // 1 lần lấy KM sản phẩm + mua X tặng Y
        // 1 lần lấy KM combo
        var productPromotions = await _promotionRepository
            .GetActiveProductDiscountPromotionsAsync(order.StoreId, ct);

        var comboPromotions = await _promotionRepository
            .GetActiveComboPromotionsAsync(order.StoreId, ct);

        await _promotionEngine.ApplyOrderPromotionsAsync(
            order,
            productPromotions,
            comboPromotions,
            ct);
    }

    private async Task<OrderDraftDto> SaveAndMapDraftAfterCartChangedAsync(
    Order order,
    CancellationToken ct = default)
    {
        await ApplyPromotionsForOrderAsync(order, ct);

        Recalc(order);

        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }

    /// <summary>
    /// Cộng thanh toán của đơn vào ca POS.
    ///
    /// QUAN TRỌNG:
    /// - Không cộng phần tiền khách đưa dư vào doanh thu ca.
    /// - Tổng cộng vào ca tối đa chỉ bằng GrandTotal.
    /// - Nếu đơn có nhiều phương thức thanh toán:
    ///   + Ưu tiên cộng non-cash đúng số đã thanh toán.
    ///   + Cash chỉ nhận phần còn lại cần đủ GrandTotal.
    /// 
    /// Ví dụ:
    /// - Đơn 840.000
    /// - Khách đưa tiền mặt 1.000.000
    /// => CashSalesTotal chỉ cộng 840.000, không cộng 1.000.000.
    /// </summary>
    private static void ApplyPaymentsToShift(POSShift shift, Order order)
    {
        var payments = order.Payments
            .Where(p => !p.IsDeleted && p.Amount > 0)
            .OrderBy(p => p.Method == PaymentMethod.Cash ? 2 : 1)
            .ThenBy(p => p.Id)
            .ToList();

        var remainingToApply = order.GrandTotal;

        if (remainingToApply <= 0)
            return;

        foreach (var payment in payments)
        {
            if (remainingToApply <= 0)
                break;

            var amountToApply = Math.Min(payment.Amount, remainingToApply);

            if (payment.Method == PaymentMethod.Cash)
                shift.CashSalesTotal += amountToApply;
            else
                shift.NonCashSalesTotal += amountToApply;

            remainingToApply -= amountToApply;
        }

        shift.RecalcExpected();
    }

    private async Task<OrderDraftDto> MapAsync(Order order, CancellationToken ct = default)
    {
        return new OrderDraftDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name,
            CustomerPhone = order.Customer?.Phone,
            CustomerPriceTier = order.Customer?.PriceTier,
            PromotionDiscountTotal = order.PromotionDiscountTotal,
            RewardSummary = order.CustomerId.HasValue
    ? await _customerRewardService.GetSummaryAsync(order.CustomerId.Value, ct)
    : null,
            Note = order.Note,
            Subtotal = order.Subtotal,
            OrderDiscount = order.OrderDiscount,
            DiscountTotal = order.DiscountTotal,
            GrandTotal = order.GrandTotal,
            ComboDiscountTotal = order.ComboDiscountTotal,
            ComboPromotionId = order.ComboPromotionId,
            ComboPromotionName = order.ComboPromotionName,
            ComboPromotionNote = order.ComboPromotionNote,
            VoucherDiscountTotal = order.VoucherDiscountTotal,
            AppliedRewardVouchers = order.RewardVouchers
    .Where(x => !x.IsDeleted)
    .OrderBy(x => x.Id)
    .Select(x => new AppliedRewardVoucherDto
    {
        VoucherId = x.VoucherId,
        VoucherCode = x.Voucher != null ? x.Voucher.VoucherCode : "",
        Value = x.VoucherValue
    })
    .ToList(),
            PaidTotal = order.PaidTotal,
            BalanceDue = order.BalanceDue,
            ChangeDue = order.ChangeDue,
            Payments = order.Payments
                .Where(p => !p.IsDeleted)
                .OrderBy(p => p.Id)
                .Select(p => new OrderPaymentDto
                {
                    PaymentId = p.Id,
                    Method = p.Method.ToString(),
                    Amount = p.Amount,
                    Reference = p.ReferenceCode,
                    CreatedAt = p.PaidAtUtc
                })
                .ToList(),
            Lines = BuildOrderedPosLines(order)
    .Select(x =>
    {
        var img = ResolveVariantImage(x.Variant);

        return new OrderLineDto
        {
            LineId = x.Id,
            VariantId = x.VariantId,
            ItemName = x.ItemName,
            ProductVariantName = x.Variant != null ? x.Variant.ProductVariantName : null,
            UnitName = x.UnitName,
            Sku = x.Sku ?? string.Empty,
            Barcode = x.Barcode,
            UnitPrices = BuildLineUnitPrices(order, x),

            OriginalUnitPrice = x.OriginalUnitPrice,
            PromotionDiscount = x.PromotionDiscount,
            PromotionName = x.PromotionName,

            ImageUrl = img.url,
            ImageThumbUrl = img.thumb,
            ImageAlt = img.alt,
            HasImage = img.hasImage,

            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice,
            LineDiscount = x.LineDiscount,
            LineTotal = x.LineTotal,

            SellingUnitId = x.SellingUnitId,
            SellingUnitName = x.SellingUnitName,
            BaseUnitId = x.BaseUnitId,
            BaseUnitName = x.BaseUnitName,
            Multiplier = x.Multiplier,
            BaseQuantity = x.BaseQuantity,
            ScannedBarcode = x.ScannedBarcode,

            ComboPromotionId = x.ComboPromotionId,
            ComboPromotionName = x.ComboPromotionName,
            ComboPromotionNote = x.ComboPromotionNote,
            ComboAllocatedDiscount = x.ComboAllocatedDiscount,

            PromotionType = x.PromotionType,
            PromotionBuyQuantity = x.PromotionBuyQuantity,
            PromotionGiftQuantity = x.PromotionGiftQuantity,

            IsPromotionGift = x.IsPromotionGift,
            GiftPromotionId = x.GiftPromotionId,
            GiftSourceLineId = x.GiftSourceLineId,
            GiftPromotionName = x.GiftPromotionName,
            GiftPromotionNote = x.GiftPromotionNote,

            BarcodeSource = x.BarcodeSource
        };
    })
    .ToList()
        };
    }
    private static List<OrderLine> BuildOrderedPosLines(Order order)
    {
        var lines = order.Lines
            .Where(x => !x.IsDeleted)
            .ToList();

        var normalLines = lines
            .Where(x => !x.IsPromotionGift)
            .OrderBy(x => x.Id)
            .ToList();

        var giftLinesBySource = lines
            .Where(x => x.IsPromotionGift)
            .GroupBy(x => x.GiftSourceLineId ?? 0)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Id).ToList());

        var result = new List<OrderLine>();

        foreach (var line in normalLines)
        {
            result.Add(line);

            if (giftLinesBySource.TryGetValue(line.Id, out var gifts))
            {
                result.AddRange(gifts);
            }
        }

        // Phòng trường hợp gift cũ chưa có source id
        var orphanGifts = lines
            .Where(x => x.IsPromotionGift && (!x.GiftSourceLineId.HasValue || x.GiftSourceLineId.Value <= 0))
            .OrderBy(x => x.Id)
            .ToList();

        result.AddRange(orphanGifts);

        return result;
    }
    private static List<OrderLineUnitPriceDto> BuildLineUnitPrices(Order order, OrderLine line)
    {
        var tier = (order.Customer?.PriceTier ?? "RETAIL")
            .Trim()
            .ToUpperInvariant();

        var isWholesale = tier == "WHOLESALE";

        var conversions = line.Variant?.UnitConversions?
            .Where(c => !c.IsDeleted && c.IsActive && c.Unit != null)
            .OrderByDescending(c => c.IsBaseUnit)
            .ThenBy(c => c.Factor)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToList() ?? new();

        var sameVariantLines = order.Lines
            .Where(x => !x.IsDeleted && x.VariantId == line.VariantId)
            .ToList();

        foreach (var l in sameVariantLines)
        {
            var m = l.Multiplier <= 0 ? 1m : l.Multiplier;
            l.BaseQuantity = l.Quantity * m;
        }

        var totalBaseQty = sameVariantLines.Sum(x => x.BaseQuantity);

        ProductUnitConversion? appliedConversion = null;

        // Tổng >= 48: áp giá thùng
        appliedConversion = conversions
            .Where(c => c.Factor >= 48 && totalBaseQty >= c.Factor)
            .OrderByDescending(c => c.Factor)
            .FirstOrDefault();

        // Tổng >= 4: áp giá lốc
        appliedConversion ??= conversions
            .Where(c => c.Factor >= 4 && c.Factor < 48 && totalBaseQty >= c.Factor)
            .OrderByDescending(c => c.Factor)
            .FirstOrDefault();

        return conversions.Select(c =>
        {
            var retail = c.Price;
            var wholesale = c.WholesalePrice;

            var effective = isWholesale && wholesale.HasValue && wholesale.Value > 0
                ? wholesale.Value
                : retail ?? 0m;

            return new OrderLineUnitPriceDto
            {
                UnitName = c.Unit.Name,
                Factor = c.Factor,
                RetailPrice = retail,
                WholesalePrice = wholesale,
                EffectivePrice = effective,
                IsBaseUnit = c.IsBaseUnit,

                // Đơn vị bán thật sự của dòng hiện tại
                IsCurrentUnit =
    line.ProductUnitConversionId.HasValue
        ? c.Id == line.ProductUnitConversionId.Value
        : c.UnitId == line.SellingUnitId,

                // Đơn vị giá đang áp dụng theo tổng cùng VariantId
                IsEffectivePriceUnit = appliedConversion != null
                    ? c.Id == appliedConversion.Id
                    : c.UnitId == line.SellingUnitId
            };
        }).ToList();
    }
    public async Task<HoldOrderResultDto> HoldAndCreateNewDraftAsync(
      int orderId,
      string? holdNote = null,
      CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var openShift = await RequireCurrentOpenShiftAsync(ct);
            EnsureShiftOwnership(openShift);

            var order = await RequireDraftAsync(orderId, ct);

            if (order.POSShiftId != openShift.Id)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.CartNotInCurrentTerminal,
                    message: "Không thể giữ đơn vì đơn không thuộc terminal POS hiện tại.",
                    actionHint: "Vui lòng thao tác trên đúng terminal hoặc chuyển xử lý đơn phù hợp.",
                    metadata: new
                    {
                        order.Id,
                        order.OrderNumber,
                        OrderShiftId = order.POSShiftId,
                        CurrentShiftId = openShift.Id,
                        openShift.TerminalId
                    });
            }

            if (!order.Lines.Any(x => !x.IsDeleted))
            {
                throw PosAppException.Validation(
                    errorCode: PosErrorCodes.CartEmptyCannotHold,
                    message: "Không thể giữ giỏ trống.",
                    actionHint: "Hãy thêm sản phẩm vào giỏ trước khi giữ đơn.",
                    metadata: new
                    {
                        order.Id,
                        order.OrderNumber
                    });
            }

            order.Status = OrderStatus.OnHold;
            order.HeldAtUtc = DateTime.UtcNow;
            order.HoldNote = string.IsNullOrWhiteSpace(holdNote) ? null : holdNote.Trim();

            if (string.IsNullOrWhiteSpace(order.HoldCode))
                order.HoldCode = GenerateHoldCode(order.Id);

            await _inventoryReservationService.RebuildForOrderAsync(order, ct);

            var reusableDraft = await FindReusableEmptyDraftAsync(
                openShift.Id,
                excludeOrderId: order.Id,
                ct);

            Order newDraft;

            if (reusableDraft != null)
            {
                newDraft = reusableDraft;
            }
            else
            {
                newDraft = new Order
                {
                    StoreId = _posContext.StoreId,
                    Status = OrderStatus.Draft,
                    PaymentStatus = PaymentStatus.Unpaid,
                    POSShiftId = openShift.Id
                };

                Recalc(newDraft);

                await _legalEntityFinalizeService.CaptureModeAsync(newDraft, ct);
                await _orders.AddAsync(newDraft, ct);
                await _orders.SaveChangesAsync(ct);
            }

            openShift.CurrentOrderId = newDraft.Id;
            await _orders.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return new HoldOrderResultDto
            {
                HeldOrderId = order.Id,
                HoldCode = order.HoldCode,
                NewDraftOrderId = newDraft.Id,
                Message = "Đã giữ đơn và chuyển sang giỏ nháp mới."
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> ResumeHeldAsync(int orderId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var openShift = await RequireCurrentOpenShiftAsync(ct);
            EnsureShiftOwnership(openShift);

            var heldOrder = await _orders.GetByIdAsync(orderId, ct);
            if (heldOrder == null)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.CartCurrentNotFound,
                    message: "Không tìm thấy đơn cần mở lại.",
                    actionHint: "Vui lòng tải lại danh sách đơn giữ rồi thử lại.");
            }

            if (heldOrder.Status != OrderStatus.OnHold)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.CartResumeNotHeld,
                    message: "Đơn này không còn ở trạng thái đang giữ.",
                    actionHint: "Vui lòng tải lại danh sách đơn giữ.",
                    metadata: new
                    {
                        heldOrder.Id,
                        heldOrder.OrderNumber,
                        Status = heldOrder.Status.ToString()
                    });
            }

            if (heldOrder.StoreId != openShift.StoreId)
            {
                throw PosAppException.Business(
                    errorCode: PosErrorCodes.CartResumeStoreMismatch,
                    message: "Đơn giữ này không thuộc cửa hàng hiện tại.",
                    actionHint: "Vui lòng kiểm tra lại cửa hàng hoặc mở đơn ở đúng nơi phát sinh.",
                    metadata: new
                    {
                        heldOrder.Id,
                        heldOrder.OrderNumber,
                        OrderStoreId = heldOrder.StoreId,
                        CurrentStoreId = openShift.StoreId
                    });
            }

            if (openShift.CurrentOrderId.HasValue)
            {
                var currentOrder = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

                if (currentOrder != null &&
                    currentOrder.Status == OrderStatus.Draft &&
                    currentOrder.Id != heldOrder.Id)
                {
                    var hasData = HasMeaningfulWork(currentOrder);

                    if (hasData)
                    {
                        currentOrder.Status = OrderStatus.OnHold;
                        currentOrder.HeldAtUtc = DateTime.UtcNow;

                        if (string.IsNullOrWhiteSpace(currentOrder.HoldCode))
                            currentOrder.HoldCode = GenerateHoldCode(currentOrder.Id);

                        if (string.IsNullOrWhiteSpace(currentOrder.HoldNote))
                            currentOrder.HoldNote = "Tự giữ khi chuyển sang đơn khác";

                        await _inventoryReservationService.RebuildForOrderAsync(
                            currentOrder,
                            ct);
                    }
                }
            }

            heldOrder.Status = OrderStatus.Draft;
            heldOrder.POSShiftId = openShift.Id;
            openShift.CurrentOrderId = heldOrder.Id;

            await _orders.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return heldOrder.Id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<HeldOrderDto>> GetHeldOrdersAsync(CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        var orders = await _orders.GetHeldOrdersByStoreAsync(openShift.StoreId, ct);
        var userIds = orders
    .Where(o => o.POSShift != null && o.POSShift.OpenedByUserId > 0)
    .Select(o => o.POSShift.OpenedByUserId)
    .Distinct()
    .ToList();
        var users = await _users.GetByIdsAsync(userIds, ct);
        var userMap = users.ToDictionary(
    x => x.Id,
    x => string.IsNullOrWhiteSpace(x.FullName) ? x.UserName : x.FullName
);

        return orders.Select(o => new HeldOrderDto
        {
            OrderId = o.Id,
            HoldCode = o.HoldCode,
            HoldNote = o.HoldNote,
            HeldAtUtc = o.HeldAtUtc,

            LineCount = o.Lines.Count(x => !x.IsDeleted),
            TotalQuantity = o.Lines.Where(x => !x.IsDeleted).Sum(x => x.Quantity),
            Subtotal = o.Lines.Where(x => !x.IsDeleted).Sum(x => x.LineTotal),

            CustomerId = o.CustomerId,
            CustomerName = o.Customer?.Name,
            CustomerPhone = o.Customer?.Phone,

            // =========================================================
            // BƯỚC 4.2:
            // Map thông tin ca POS đang giữ đơn
            // =========================================================
            PosShiftId = o.POSShiftId,
            ShiftCode = o.POSShift?.ShiftCode,

            // Terminal hiện tạm map dạng string để UI dùng thống nhất
            TerminalId = o.POSShift != null
         ? o.POSShift.TerminalId.ToString()
         : null,
            TerminalName = o.POSShift != null
    ? o.POSShift.Terminal.Name
    : null,

            HeldByUserId = o.POSShift?.OpenedByUserId,
            HeldByUserName = o.POSShift != null &&
                 userMap.ContainsKey(o.POSShift.OpenedByUserId)
    ? userMap[o.POSShift.OpenedByUserId]
    : null,
            IsCurrentShift = o.POSShiftId == openShift.Id,
            IsCurrentTerminal = o.POSShift != null
         && o.POSShift.TerminalId == openShift.TerminalId
        }).ToList();
    }

    public async Task<CurrentCartDto> GetCurrentCartAsync(CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        if (openShift.CurrentOrderId.HasValue)
        {
            var order = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

            if (order == null || order.POSShiftId != openShift.Id || order.Status != OrderStatus.Draft)
            {
                openShift.CurrentOrderId = null;
                await _orders.SaveChangesAsync(ct);
            }
        }

        return new CurrentCartDto
        {
            CurrentOrderId = openShift.CurrentOrderId
        };
    }

    public async Task SetCurrentCartAsync(int orderId, CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null)
        {
            throw PosAppException.Business(
                errorCode: PosErrorCodes.CartCurrentNotFound,
                message: "Không tìm thấy đơn hàng.",
                actionHint: "Vui lòng tải lại danh sách đơn và thử lại.");
        }

        EnsureCanBeCurrentCart(order, openShift.Id);

        openShift.CurrentOrderId = order.Id;
        await _orders.SaveChangesAsync(ct);
    }

    public async Task<List<ActiveDraftOrderDto>> GetDraftOrdersAsync(CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        var orders = await _orders.GetDraftOrdersByShiftAsync(openShift.Id, ct);

        return orders.Select(o => new ActiveDraftOrderDto
        {
            OrderId = o.Id,
            OrderNumber = o.OrderNumber,
            Note = o.Note,
            LineCount = o.Lines.Count(x => !x.IsDeleted),
            TotalQuantity = o.Lines.Where(x => !x.IsDeleted).Sum(x => x.Quantity),
            GrandTotal = o.GrandTotal,
            CreatedAtUtc = o.CreatedAtUtc,
            IsCurrent = openShift.CurrentOrderId == o.Id
        }).ToList();
    }

    public async Task<POSScreenDto> GetPOSScreenAsync(CancellationToken ct = default)
    {
        var ensuredDraft = await EnsureCurrentCartAsync(ct);
        var current = await GetCurrentCartAsync(ct);
        var held = await GetHeldOrdersAsync(ct);

        return new POSScreenDto
        {
            CurrentCart = current,
            CurrentDraft = ensuredDraft,
            HeldOrders = held
        };
    }

    public async Task<OrderDraftDto> EnsureCurrentCartAsync(CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        if (openShift.CurrentOrderId.HasValue)
        {
            var currentOrder = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

            var isValidCurrent =
                currentOrder != null &&
                currentOrder.POSShiftId == openShift.Id &&
                currentOrder.Status == OrderStatus.Draft;

            if (isValidCurrent)
            {
                Recalc(currentOrder!);
                await _orders.SaveChangesAsync(ct);
                return await MapAsync(currentOrder!, ct);
            }

            openShift.CurrentOrderId = null;
            await _orders.SaveChangesAsync(ct);
        }

        var draftOrders = await _orders.GetDraftOrdersByShiftAsync(openShift.Id, ct);

        var reusableDraft = draftOrders
            .OrderByDescending(x => HasMeaningfulWork(x))
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        if (reusableDraft != null)
        {
            openShift.CurrentOrderId = reusableDraft.Id;

            Recalc(reusableDraft);
            await _orders.SaveChangesAsync(ct);

            return await MapAsync(reusableDraft, ct);
        }

        var newOrder = new Order
        {
            StoreId = _posContext.StoreId,
            Status = OrderStatus.Draft,
            PaymentStatus = PaymentStatus.Unpaid,
            POSShiftId = openShift.Id
        };

        Recalc(newOrder);

        await _legalEntityFinalizeService.CaptureModeAsync(newOrder, ct);
        await _orders.AddAsync(newOrder, ct);
        await _orders.SaveChangesAsync(ct);

        openShift.CurrentOrderId = newOrder.Id;
        await _orders.SaveChangesAsync(ct);

        return await MapAsync(newOrder, ct);
    }

    public async Task<OrderDraftDto> ScanToCurrentCartAsync(string barcode, decimal qty = 1, CancellationToken ct = default)
    {
        var currentOrder = await RequireCurrentDraftAsync(ct);
        return await AddItemByBarcodeAsync(currentOrder.Id, barcode, qty, ct);
    }

    public async Task<OrderDraftDto> AddPaymentToCurrentCartAsync(QuickAddPaymentRequest dto, CancellationToken ct = default)
    {
        var currentOrder = await RequireCurrentDraftAsync(ct);

        var paymentDto = new UpsertPaymentRequest
        {
            Method = dto.Method,
            Amount = dto.Amount,
            ReferenceCode = dto.ReferenceCode,
            Provider = dto.Provider
        };

        return await AddPaymentAsync(currentOrder.Id, paymentDto, ct);
    }

    public async Task<OrderDraftDto> FinalizeCurrentCartAsync(CancellationToken ct = default)
    {
        var currentOrder = await RequireCurrentDraftAsync(ct);
        return await FinalizeAsync(currentOrder.Id, ct);
    }

    public async Task<HoldOrderResultDto> HoldCurrentCartAsync(string? holdNote = null, CancellationToken ct = default)
    {
        var currentOrder = await RequireCurrentDraftAsync(ct);
        return await HoldAndCreateNewDraftAsync(currentOrder.Id, holdNote, ct);
    }

    public async Task CancelCurrentCartAsync(string? reason = null, CancellationToken ct = default)
    {
        var currentOrder = await RequireCurrentDraftAsync(ct);
        await CancelAsync(currentOrder.Id, reason, ct);
    }

    public async Task<int> CreateAndSwitchNewCartAsync(int? customerId = null, string? note = null, CancellationToken ct = default)
    {
        var openShift = await RequireCurrentOpenShiftAsync(ct);
        EnsureShiftOwnership(openShift);

        if (openShift.CurrentOrderId.HasValue)
        {
            var current = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

            if (current != null &&
                current.POSShiftId == openShift.Id &&
                current.Status == OrderStatus.Draft)
            {
                if (IsEmptyDraft(current))
                    return current.Id;

                throw PosAppException.Business(
                    errorCode: PosErrorCodes.CartNewBlockedByActiveCart,
                    message: "Giỏ hiện tại đang có dữ liệu.",
                    actionHint: "Vui lòng giữ đơn hoặc hủy giỏ hiện tại trước khi tạo giỏ mới.",
                    metadata: new
                    {
                        current.Id,
                        current.OrderNumber,
                        HasLines = current.Lines.Any(x => !x.IsDeleted),
                        HasPayments = current.Payments.Any(x => !x.IsDeleted)
                    });
            }
        }

        return await CreateDraftAsync(customerId, note, ct);
    }

    public async Task<List<POSProductSearchItemDto>> SearchProductsForPOSAsync(
    string keyword,
    int take = 20,
    CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<POSProductSearchItemDto>();

        var variants = await _variants.SearchForPOSAsync(keyword, take, ct);

        if (variants == null || variants.Count == 0)
            return new List<POSProductSearchItemDto>();

        // =========================================================
        // Resolve kho POS hiện tại để lấy tồn đúng kho đang bán
        // =========================================================
        var warehouseId = await TryResolveCurrentPOSWarehouseIdAsync(ct);

        Dictionary<int, decimal> availableQtyMap = new();

        if (_posContext.IsAvailable && warehouseId.HasValue && warehouseId.Value > 0)
        {
            var variantIds = variants
                .Select(x => x.Id)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            availableQtyMap = await _inventoryBalanceRepository.GetAvailableQtyMapByVariantIdsAsync(
                _posContext.StoreId,
                warehouseId.Value,
                variantIds,
                ct);
        }

        return variants.Select(x =>
        {
            var productName = x.Product?.Name?.Trim() ?? string.Empty;
            var productVariantName = x.ProductVariantName?.Trim() ?? string.Empty;

            var sellingInfo = ResolvePreferredSellingUnit(x);
            var image = ResolveVariantImage(x);
            var availableQty = availableQtyMap.TryGetValue(x.Id, out var qty)
              ? qty
              : 0m;
            var isNegativeStock = availableQty < 0;
            var displayQty = isNegativeStock ? 0m : availableQty;
            var unitOptions = (x.UnitConversions ?? Enumerable.Empty<ProductUnitConversion>())
    .Where(c =>
        !c.IsDeleted &&
        c.IsActive &&
        c.Unit != null &&
        !c.Unit.IsDeleted &&
        c.Factor > 1 &&
        !c.IsBaseUnit)
    .OrderBy(c => c.Factor)
    .Select(c =>
    {
        var factor = c.Factor <= 0 ? 1m : c.Factor;

        // CHỐT:
        // chỉ lấy phần chẵn tương đối, không hiển thị phần dư
        var convertedAvailableQty = factor > 0
            ? Math.Floor(displayQty / factor)
            : 0m;

        return new POSProductSearchUnitOptionDto
        {
            ProductUnitConversionId = c.Id,
            UnitId = c.UnitId,
            UnitName = c.Unit.Name,
            Factor = c.Factor,
            Price = c.Price ?? 0m,
            Barcode = c.Barcodes?
                .Where(b => !b.IsDeleted && b.IsActive)
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id)
                .Select(b => b.Barcode)
                .FirstOrDefault(),

            AvailableQty = convertedAvailableQty,
            IsNegativeStock = isNegativeStock
        };
    })
    .ToList();



            return new POSProductSearchItemDto
            {
                VariantId = x.Id,
                ProductId = x.ProductId,
                ProductName = productName,
                ProductVariantName = productVariantName,
                DisplayName = string.IsNullOrWhiteSpace(productVariantName)
         ? productName
         : productVariantName,
                Sku = x.Sku,
                Barcode = sellingInfo.Barcode ?? ResolveRepresentativeBarcode(x),
                Price = sellingInfo.UnitPrice ?? 0m,
                IsActive = x.IsActive,

                ImageUrl = image.url,
                ImageThumbUrl = image.thumb,
                ImageAlt = image.alt,
                HasImage = image.hasImage,

                OnHandQty = displayQty,
                IsNegativeStock = isNegativeStock,
                UnitOptions = unitOptions
            };
        }).ToList();
    }

    public async Task<List<POSCustomerSearchItemDto>> SearchCustomersForPOSAsync(string keyword, int take = 20, CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<POSCustomerSearchItemDto>();

        var customers = await _customers.SearchActiveAsync(keyword, take, ct);

        return customers.Select(x => new POSCustomerSearchItemDto
        {
            CustomerId = x.Id,
            Name = x.Name,
            Phone = x.Phone,
            Address = x.Address,

            // NEW: trả nhóm giá để UI biết khách lẻ hay khách sỉ
            PriceTier = NormalizeCustomerPriceTier(x.PriceTier)
        }).ToList();
    }

    public async Task<OrderDraftDto> SetCustomerForCurrentCartAsync(
     int customerId,
     bool repriceExistingLines = false,
     CancellationToken ct = default)
    {
        var customer = await _customers.GetActiveByIdAsync(customerId, ct)
            ?? throw new InvalidOperationException("Khách hàng không tồn tại hoặc đã bị khóa.");

        var order = await RequireCurrentDraftAsync(ct);

        var oldCustomerId = order.CustomerId;

        if (oldCustomerId.HasValue && oldCustomerId.Value != customer.Id)
        {
            await _orders.ClearRewardVouchersAsync(order.Id, ct);
            order.RewardVouchers.Clear();
        }

        order.CustomerId = customer.Id;

        // NEW:
        // Nếu user chọn áp lại giá thì cập nhật giá các dòng hiện có
        // theo PriceTier của khách mới.
        if (repriceExistingLines)
        {
            await RepriceOrderLinesByCustomerAsync(order, customer.PriceTier, ct);
        }

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }
    private async Task RepriceOrderLinesByCustomerAsync(
    Order order,
    string? customerPriceTier,
    CancellationToken ct)
    {
        var priceTier = NormalizeCustomerPriceTier(customerPriceTier);

        var lines = order.Lines
            .Where(x => !x.IsDeleted)
            .ToList();

        if (!lines.Any())
            return;

        foreach (var line in lines)
        {
            var variant = await _variants.GetActiveWithProductAsync(line.VariantId, ct);
            if (variant == null)
                continue;

            ProductUnitConversion? conversion = null;

            // Ưu tiên tìm đúng đơn vị đang bán của dòng.
            // line.SellingUnitId hiện là UnitId, không phải ProductUnitConversionId.
            conversion = variant.UnitConversions?
      .FirstOrDefault(c =>
          !c.IsDeleted &&
          c.IsActive &&
          line.ProductUnitConversionId.HasValue &&
          c.Id == line.ProductUnitConversionId.Value);

            // Fallback nếu không tìm thấy.
            conversion ??= variant.UnitConversions?
      .FirstOrDefault(c =>
          !c.IsDeleted &&
          c.IsActive &&
          c.UnitId == line.SellingUnitId);

            var newUnitPrice = ResolveSalePriceByTier(
                variant,
                conversion,
                priceTier);

            line.UnitPrice = newUnitPrice;
        }
    }

    public async Task<OrderDraftDto> ClearCustomerForCurrentCartAsync(CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        order.CustomerId = null;

        await _orders.ClearRewardVouchersAsync(order.Id, ct);
        order.RewardVouchers.Clear();

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }

    public async Task<OrderDraftDto> CreateCustomerAndSetForCurrentCartAsync(CreatePOSCustomerDto dto, CancellationToken ct = default)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        var phone = (dto.Phone ?? string.Empty).Trim();
        var address = (dto.Address ?? string.Empty).Trim();
        var note = (dto.Note ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên khách hàng không được để trống.");

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var existed = await _customers.GetByPhoneAsync(phone, ct);
            if (existed != null)
                throw new InvalidOperationException("Số điện thoại đã tồn tại trong hệ thống khách hàng.");
        }

        var customer = new Customer
        {
            Name = name,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            Address = string.IsNullOrWhiteSpace(address) ? null : address,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,

            // NEW:
            // Nếu UI gửi WHOLESALE thì lưu khách sỉ.
            // Nếu không gửi hoặc gửi sai thì mặc định RETAIL.
            PriceTier = NormalizeCustomerPriceTier(dto.PriceTier),

            IsActive = true
        };

        await _customers.AddAsync(customer, ct);
        await _customers.SaveChangesAsync(ct);

        return await SetCustomerForCurrentCartAsync(
     customer.Id,
     repriceExistingLines: false,
     ct);
    }
    /// <summary>
    /// Chuẩn hóa nhóm giá khách hàng.
    /// Chỉ cho phép:
    /// - RETAIL
    /// - WHOLESALE
    /// Nếu dữ liệu null/sai thì trả về RETAIL để an toàn.
    /// </summary>
    private static string NormalizeCustomerPriceTier(string? priceTier)
    {
        priceTier = (priceTier ?? string.Empty).Trim().ToUpperInvariant();

        return priceTier == CustomerPriceTiers.Wholesale
            ? CustomerPriceTiers.Wholesale
            : CustomerPriceTiers.Retail;
    }

    public async Task<OrderDraftDto> UpdateCurrentCartNoteAsync(
     string? note,
     CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        order.Note = string.IsNullOrWhiteSpace(note)
            ? null
            : note.Trim();

        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }

    public async Task<OrderDraftDto> UpdateCurrentCartDiscountAsync(decimal discountAmount, CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        if (discountAmount < 0)
            throw new InvalidOperationException("Giảm giá không được nhỏ hơn 0.");

        Recalc(order);

        if (discountAmount > order.Subtotal)
            throw new InvalidOperationException("Giảm giá không được lớn hơn tạm tính.");

        order.OrderDiscount = discountAmount;

        return await SaveAndMapDraftAfterCartChangedAsync(order, ct);
    }

    public async Task<OrderDraftDto> UpdateLineDiscountAsync(int lineId, decimal discountAmount, CancellationToken ct = default)
    {
        if (discountAmount < 0)
            throw new InvalidOperationException("Giảm giá dòng không được nhỏ hơn 0.");

        var line = await _orders.GetDraftLineAsync(lineId, ct)
            ?? throw new InvalidOperationException("Dòng hàng không tồn tại hoặc đơn không còn Draft.");

        var lineSubtotal = line.Quantity * line.UnitPrice;
        if (discountAmount > lineSubtotal)
            throw new InvalidOperationException("Giảm giá dòng không được lớn hơn thành tiền trước giảm.");

        line.LineDiscount = discountAmount;

        var order = await RequireDraftAsync(line.OrderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await MapAsync(order, ct);
    }

    public async Task<OrderReceiptDto> VoidCompletedOrderAsync(int orderId, string reason, CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Lý do hủy đơn không được để trống.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var order = await _orders.GetCompletedOrderForVoidAsync(orderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

            // Lưu trạng thái cũ để ghi audit old/new values
            var oldStatus = order.Status.ToString();
            var oldPaymentStatus = order.PaymentStatus.ToString();

            if (order.Status != OrderStatus.Completed)
                throw new InvalidOperationException("Chỉ được hủy đơn đã hoàn tất.");

            if (!CanVoidCompletedOrder(order))
                throw new InvalidOperationException("Đơn đã quá thời gian cho phép hủy sau khi chốt. Vui lòng thực hiện trả hàng / hoàn tiền.");

            if (await _salesReturns.HasCompletedReturnByOrderAsync(order.Id, ct))
            {
                throw new InvalidOperationException(
                    "Đơn đã có phiếu trả hàng/hoàn tiền. Không thể void toàn bộ; vui lòng tiếp tục xử lý phần còn lại bằng chức năng Trả hàng / hoàn tiền.");
            }

            var shift = await _shifts.GetByIdAsync(order.POSShiftId, ct);
            if (shift == null)
                throw new InvalidOperationException("Không tìm thấy ca POS của đơn hàng.");

            // =====================================================
            // QUAN TRỌNG:
            // Void KHÔNG phải refund.
            // Chỉ đảo tác động sale khỏi ca:
            // - trừ CashSalesTotal / NonCashSalesTotal
            // - tăng VoidCount
            // - KHÔNG đụng RefundCount / RefundTotal
            // =====================================================
            ReversePaymentsFromShiftForVoid(shift, order);

            // Đơn nhiều HKD đảo đúng allocation/valuation gốc; đơn legacy giữ nguyên luồng cũ.
            var handledByLegalEntity = await _legalEntityReversalService
                .ReverseVoidIfAllocatedAsync(order, reason, ct);
            if (!handledByLegalEntity)
                await ApplyInventoryForVoidAsync(order, reason, ct);

            order.Status = OrderStatus.Voided;
            order.PaymentStatus = PaymentStatus.Voided;
            await ReverseRewardForVoidedOrderAsync(order, reason, ct);
            await _rewardVoucherRepository.RestoreUsedVouchersByOrderIdAsync(
           order.Id,
           reason,
           ct);

            var voidNote = $"[HỦY SAU KHI CHỐT - {DateTime.Now:dd/MM/yyyy HH:mm:ss}] {reason}";
            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? voidNote
                : $"{order.Note}{Environment.NewLine}{voidNote}";

            await _orders.SaveChangesAsync(ct);

            await WritePosAuditLogAsync(
                action: "ORDER_VOIDED",
                orderId: order.Id,
                note: reason,
                metadata: new
                {
                    order.OrderNumber,
                    order.GrandTotal,
                    Status = order.Status.ToString(),
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Reason = reason
                },
                ct: ct);

            await WriteVoidOrderAuditLogAsync(
                order: order,
                oldStatus: oldStatus,
                oldPaymentStatus: oldPaymentStatus,
                reason: reason,
                ct: ct);

            await tx.CommitAsync(ct);

            return await GetReceiptAsync(order.Id, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            try
            {
                await _auditLogService.WriteAsync(new WriteAuditLogRequest
                {
                    Module = AuditModuleType.Orders,
                    ActionType = AuditActionType.CancelOrder,
                    EntityName = nameof(Order),
                    EntityId = orderId.ToString(),
                    EntityDisplay = $"Đơn hàng #{orderId}",
                    Summary = $"Void order thất bại cho order #{orderId}",
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                }, ct);
            }
            catch
            {
                // Không throw lại để tránh che mất lỗi chính.
            }

            throw;
        }
    }
    /// <summary>
    /// Trừ lại tích lũy khi refund toàn phần đơn hàng.
    /// Chỉ trừ đúng số đã từng cộng từ SaleEarned.
    /// </summary>
    private async Task ReverseRewardForRefundedOrderAsync(
        Order order,
        string reason,
        CancellationToken ct)
    {
        if (!order.CustomerId.HasValue || order.CustomerId.Value <= 0)
            return;

        var earnedAmount = await _rewardLedgerRepository.GetOrderLedgerAmountAsync(
            order.Id,
            CustomerRewardLedgerType.SaleEarned,
            ct);

        if (earnedAmount <= 0)
            return;

        var existed = await _rewardLedgerRepository.HasLedgerForOrderAsync(
            order.Id,
            CustomerRewardLedgerType.SaleRefunded,
            ct);

        if (existed)
            return;

        await _rewardLedgerRepository.AddAsync(new CustomerRewardLedger
        {
            StoreId = order.StoreId,
            CustomerId = order.CustomerId.Value,
            Type = CustomerRewardLedgerType.SaleRefunded,
            Amount = -earnedAmount,
            OrderId = order.Id,
            ReferenceCode = $"ORDER_REFUND_{order.Id}",
            Description = $"Trừ tích lũy do refund đơn {order.OrderNumber ?? order.Id.ToString()}. Lý do: {reason}"
        }, ct);
    }
    /// <summary>
    /// Đảo tác động thanh toán của SALE khỏi ca khi VOID đơn đã chốt.
    ///
    /// QUAN TRỌNG:
    /// - VOID không phải REFUND.
    /// - Không được cộng/trừ RefundCount.
    /// - Không được cộng/trừ CashRefundTotal / NonCashRefundTotal.
    /// - Chỉ đảo phần doanh thu sale đã ghi vào ca lúc finalize.
    /// </summary>
    private static void ReversePaymentsFromShiftForVoid(POSShift shift, Order order)
    {
        var payments = order.Payments
            .Where(x => !x.IsDeleted)
            .ToList();

        foreach (var payment in payments)
        {
            shift.ReverseSaleAmount(payment.Amount, payment.Method);
        }

        shift.IncreaseVoidCount();
    }
    private static (string? url, string? thumb, string? alt, bool hasImage)
 ResolveVariantImage(ProductVariant? variant)
    {
        // =========================================
        // 1. Ưu tiên ảnh riêng của variant
        // =========================================
        var variantImage = variant?.PrimaryProductImage;
        var variantMedia = variantImage?.MediaAsset;

        if (variantMedia != null && !string.IsNullOrWhiteSpace(variantMedia.StoragePath))
        {
            var variantUrl = "/" + variantMedia.StoragePath.Replace("\\", "/").TrimStart('/');

            return (
                variantUrl,
                variantUrl, // tạm thời thumb = url
                !string.IsNullOrWhiteSpace(variantImage?.AltText)
                    ? variantImage!.AltText
                    : !string.IsNullOrWhiteSpace(variant?.ProductVariantName)
                        ? variant.ProductVariantName
                        : variant?.Product?.Name,
                true
            );
        }

        // =========================================
        // 2. Fallback ảnh primary của Product
        // =========================================
        var productImages = variant?.Product?.ProductImages?
            .Where(x => !x.IsDeleted && x.MediaAsset != null && !string.IsNullOrWhiteSpace(x.MediaAsset.StoragePath))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        var productPrimaryImage = productImages?.FirstOrDefault();
        var productPrimaryMedia = productPrimaryImage?.MediaAsset;

        if (productPrimaryMedia != null && !string.IsNullOrWhiteSpace(productPrimaryMedia.StoragePath))
        {
            var productUrl = "/" + productPrimaryMedia.StoragePath.Replace("\\", "/").TrimStart('/');

            return (
                productUrl,
                productUrl, // tạm thời thumb = url
                !string.IsNullOrWhiteSpace(productPrimaryImage?.AltText)
                    ? productPrimaryImage!.AltText
                    : !string.IsNullOrWhiteSpace(variant?.ProductVariantName)
                        ? variant.ProductVariantName
                        : variant?.Product?.Name,
                true
            );
        }

        // =========================================
        // 3. Không có ảnh
        // =========================================
        return (null, null, null, false);
    }
    public async Task<POSShiftDashboardDto> GetCurrentShiftDashboardAsync(CancellationToken ct = default)
    {
        // =====================================================
        // FIX:
        // Không dùng GetCurrentOpenShiftAsync() legacy nữa
        // vì method đó có thể lấy nhầm ca của terminal khác.
        //
        // POS chuẩn phải luôn lấy ca theo:
        // StoreId + TerminalId hiện tại.
        // =====================================================
        var shift = await RequireCurrentOpenShiftAsync(ct);

        // Kiểm tra ca này có thuộc đúng nhân viên đang đăng nhập không.
        // Nếu không đúng, hệ thống sẽ báo popup tiếp quản / đóng hộ.
        EnsureShiftOwnership(shift);

        var orders = await _orders.GetByShiftIdAsync(shift.Id, ct);

        var completed = orders
            .Where(x => x.Status == OrderStatus.Completed)
            .ToList();

        var voided = orders
            .Where(x => x.Status == OrderStatus.Voided)
            .ToList();

        var refunded = orders
            .Where(x => x.Status == OrderStatus.Refunded)
            .ToList();

        var cashSales = completed
            .SelectMany(x => x.Payments)
            .Where(p => !p.IsDeleted && p.Method == PaymentMethod.Cash)
            .Sum(x => x.Amount);

        var bankSales = completed
            .SelectMany(x => x.Payments)
            .Where(p => !p.IsDeleted && p.Method != PaymentMethod.Cash)
            .Sum(x => x.Amount);

        return new POSShiftDashboardDto
        {
            ShiftId = shift.Id,
            ShiftCode = shift.ShiftCode,
            OpenedAt = shift.OpenedAtUtc.ToLocalTime(),

            CashSales = cashSales,
            BankSales = bankSales,
            TotalSales = cashSales + bankSales,

            OrdersCount = completed.Count,
            VoidCount = voided.Count,
            RefundCount = refunded.Count
        };
    }
    private static string? TrimText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 20)] + "... [rút gọn]";
    }
    /// <summary>
    /// Cộng tiền tích lũy cho khách sau khi đơn POS finalize thành công.
    /// Chống cộng trùng bằng OrderId + Type = SaleEarned.
    /// </summary>
    private async Task ApplyRewardForFinalizedOrderAsync(Order order, CancellationToken ct)
    {
        if (!order.CustomerId.HasValue || order.CustomerId.Value <= 0)
            return;

        if (order.Status != OrderStatus.Completed || order.PaymentStatus != PaymentStatus.Paid)
            return;

        var existed = await _rewardLedgerRepository.HasLedgerForOrderAsync(
     order.Id,
     CustomerRewardLedgerType.SaleEarned,
     ct);
        if (existed)
            return;

        var calculation = await _orderRewardCalculator.CalculateAsync(order.Id, ct);

        if (calculation.RewardableAmount <= 0)
            return;

        var ledger = new CustomerRewardLedger
        {
            StoreId = order.StoreId,
            CustomerId = order.CustomerId.Value,
            Type = CustomerRewardLedgerType.SaleEarned,
            Amount = calculation.RewardableAmount,
            OrderId = order.Id,
            ReferenceCode = $"ORDER_{order.Id}",
            Description = $"Tích điểm từ đơn hàng {order.OrderNumber ?? order.Id.ToString()}: {calculation.RewardableAmount:N0}đ"
        };

        await _rewardLedgerRepository.AddAsync(ledger, ct);
    }
    /// <summary>
    /// Trừ lại tích lũy khi hủy đơn đã chốt.
    /// Chỉ trừ đúng số đã từng cộng từ SaleEarned.
    /// </summary>
    private async Task ReverseRewardForVoidedOrderAsync(
        Order order,
        string reason,
        CancellationToken ct)
    {
        if (!order.CustomerId.HasValue || order.CustomerId.Value <= 0)
            return;

        var earnedAmount = await _rewardLedgerRepository.GetOrderLedgerAmountAsync(
            order.Id,
            CustomerRewardLedgerType.SaleEarned,
            ct);

        if (earnedAmount <= 0)
            return;

        var existed = await _rewardLedgerRepository.HasLedgerForOrderAsync(
            order.Id,
            CustomerRewardLedgerType.SaleVoided,
            ct);

        if (existed)
            return;

        await _rewardLedgerRepository.AddAsync(new CustomerRewardLedger
        {
            StoreId = order.StoreId,
            CustomerId = order.CustomerId.Value,
            Type = CustomerRewardLedgerType.SaleVoided,
            Amount = -earnedAmount,
            OrderId = order.Id,
            ReferenceCode = $"ORDER_VOID_{order.Id}",
            Description = $"Trừ tích lũy do hủy đơn {order.OrderNumber ?? order.Id.ToString()}. Lý do: {reason}"
        }, ct);
    }
   
    public async Task<OrderDraftDto> ApplyRewardVouchersToCurrentCartAsync(
        ApplyRewardVouchersRequest request,
        CancellationToken ct = default)
    {
        request ??= new ApplyRewardVouchersRequest();

        var voucherIds = request.VoucherIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (!voucherIds.Any())
            throw new InvalidOperationException("Vui lòng chọn voucher.");

        var order = await RequireCurrentDraftAsync(ct);

        if (!order.CustomerId.HasValue)
            throw new InvalidOperationException("Vui lòng chọn khách hàng trước khi dùng voucher.");

        if (!order.Lines.Any(x => !x.IsDeleted))
            throw new InvalidOperationException("Giỏ hàng chưa có sản phẩm.");

        var availableVouchers = await _rewardVoucherRepository.GetByCustomerAsync(
            order.CustomerId.Value,
            CustomerRewardVoucherStatus.Available,
            ct);

        var vouchers = availableVouchers
            .Where(x => voucherIds.Contains(x.Id))
            .ToList();

        if (vouchers.Count != voucherIds.Count)
            throw new InvalidOperationException("Có voucher không hợp lệ hoặc không còn khả dụng.");

        var rewardVouchers = vouchers.Select(voucher => new OrderRewardVoucher
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            VoucherId = voucher.Id,
            VoucherValue = voucher.Value
        }).ToList();

        await _orders.ReplaceRewardVouchersAsync(order.Id, rewardVouchers, ct);

        order.RewardVouchers.Clear();

        foreach (var item in rewardVouchers)
        {
            order.RewardVouchers.Add(item);
        }

        Recalc(order);

        await _orders.SaveChangesAsync(ct);

        var freshOrder = await _orders.GetDraftAsync(order.Id, ct)
            ?? throw new InvalidOperationException("Không tải lại được giỏ hàng.");

        Recalc(freshOrder);

        return await MapAsync(freshOrder, ct);
    }
    private void MarkRewardVouchersAsUsed(Order order)
    {
        var appliedVouchers = order.RewardVouchers
            .Where(x => !x.IsDeleted)
            .ToList();

        if (!appliedVouchers.Any())
            return;

        var now = DateTime.UtcNow;

        foreach (var applied in appliedVouchers)
        {
            if (applied.Voucher == null)
            {
                throw new InvalidOperationException(
                    $"Voucher #{applied.VoucherId} chưa được load khi chốt đơn. Kiểm tra Include RewardVouchers.ThenInclude(Voucher).");
            }

            if (applied.Voucher.Status != CustomerRewardVoucherStatus.Available)
            {
                throw new InvalidOperationException(
                    $"Voucher {applied.Voucher.VoucherCode} không còn khả dụng.");
            }

            applied.Voucher.Status = CustomerRewardVoucherStatus.Used;
            applied.Voucher.UsedOrderId = order.Id;
            applied.Voucher.UsedAtUtc = now;
        }
    }

  
    public async Task<OrderDraftDto> ClearRewardVouchersFromCurrentCartAsync(
        CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        await _orders.ClearRewardVouchersAsync(order.Id, ct);

        order.RewardVouchers.Clear();

        Recalc(order);

        await _orders.SaveChangesAsync(ct);

        var freshOrder = await _orders.GetDraftAsync(order.Id, ct)
            ?? throw new InvalidOperationException("Không tải lại được giỏ hàng.");

        Recalc(freshOrder);

        return await MapAsync(freshOrder, ct);
    }

    #region Helper
    /// <summary>
    /// Lấy nhóm giá đang áp dụng cho đơn.
    /// Nếu chưa chọn khách hoặc khách không hợp lệ thì mặc định khách lẻ.
    /// </summary>
    private static string ResolveOrderPriceTier(Order order)
    {
        var tier = order.Customer?.PriceTier;

        tier = (tier ?? string.Empty).Trim().ToUpperInvariant();

        return tier == CustomerPriceTiers.Wholesale
            ? CustomerPriceTiers.Wholesale
            : CustomerPriceTiers.Retail;
    }
    /// <summary>
    /// Resolve giá bán theo nhóm khách.
    /// 
    /// WHOLESALE:
    /// - Ưu tiên ProductUnitConversion.WholesalePrice
    /// - Nếu không có thì fallback ProductUnitConversion.Price
    /// 
    /// RETAIL:
    /// - Dùng ProductUnitConversion.Price
    /// 
    /// Fallback cuối giữ dữ liệu cũ:
    /// - variant.Price
    /// - product.BasePrice
    /// </summary>
    private static decimal ResolveSalePriceByTier(
        ProductVariant variant,
        ProductUnitConversion? conversion,
        string priceTier)
    {
        var product = variant.Product
            ?? throw new InvalidOperationException("Variant thiếu Product navigation.");

        var isWholesale = string.Equals(
            priceTier,
            CustomerPriceTiers.Wholesale,
            StringComparison.OrdinalIgnoreCase);

        if (isWholesale && conversion?.WholesalePrice is > 0)
            return conversion.WholesalePrice.Value;

        if (conversion?.Price is > 0)
            return conversion.Price.Value;

        if (variant.Price is > 0)
            return variant.Price.Value;

        return product.BasePrice;
    }
    /// <summary>
    /// Override giá lookup barcode theo nhóm khách.
    /// Vì barcode lookup hiện tại có thể trả giá lẻ mặc định.
    /// </summary>
    private async Task<BarcodeLookupResultDto> ApplyPriceTierToBarcodeLookupAsync(
    Order order,
    BarcodeLookupResultDto lookup,
    CancellationToken ct)
    {
        var priceTier = ResolveOrderPriceTier(order);

        var variant = await _variants.GetActiveWithProductAsync(
            lookup.ProductVariantId,
            ct);

        if (variant == null)
            return lookup;
        if (variant.Product == null || !variant.Product.IsActive || !variant.Product.IsSellable)
            throw new InvalidOperationException("Sản phẩm chưa được phép bán tại POS.");

        // Ưu tiên đúng đơn vị quy đổi mà barcode lookup trả về.
        var conversion = variant.UnitConversions?
            .FirstOrDefault(c =>
                !c.IsDeleted &&
                c.IsActive &&
                lookup.ProductUnitConversionId.HasValue &&
                c.Id == lookup.ProductUnitConversionId.Value);

        // Fallback nếu lookup cũ không có ProductUnitConversionId.
        conversion ??= variant.UnitConversions?
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderByDescending(c => c.IsDefaultForSale)
            .ThenByDescending(c => c.IsBaseUnit)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .FirstOrDefault();

        lookup.SellPrice = ResolveSalePriceByTier(
            variant,
            conversion,
            priceTier);

        return lookup;
    }

    /// <summary>
    /// Tìm đơn vị gói/lốc/thùng tốt nhất theo số lượng gốc.
    /// Ví dụ:
    /// - baseQuantity = 4  => chọn lốc factor 4
    /// - baseQuantity = 48 => chọn thùng factor 48
    /// - baseQuantity = 96 => chọn thùng factor 48
    /// </summary>
    private static ProductUnitConversion? FindBestPackPriceConversion(
        ProductVariant variant,
        decimal baseQuantity)
    {
        if (variant.UnitConversions == null || baseQuantity <= 0)
            return null;

        return variant.UnitConversions
            .Where(c =>
                !c.IsDeleted &&
                c.IsActive &&
                c.Factor > 1 &&
                baseQuantity >= c.Factor &&
                baseQuantity % c.Factor == 0)
            .OrderByDescending(c => c.Factor)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
    }
    /// <summary>
    /// Áp giá theo tổng số lượng gốc của cùng VariantId trong giỏ.
    /// Luật:
    /// - Tổng base >= 48: áp giá thùng cho tất cả dòng cùng variant.
    /// - Tổng base >= 4 : áp giá lốc cho tất cả dòng cùng variant.
    /// - Còn lại       : áp giá đơn vị hiện tại của từng dòng.
    /// 
    /// Ví dụ:
    /// - 4 cái + 2 lốc + 1 thùng = 60 cái
    /// => tất cả dòng cùng variant áp giá thùng.
    /// </summary>
    private async Task ApplyBestPackPriceForVariantLinesAsync(
        Order order,
        int variantId,
        CancellationToken ct)
    {
        var variant = await _variants.GetActiveWithProductAsync(variantId, ct);
        if (variant == null)
            return;

        var priceTier = ResolveOrderPriceTier(order);

        var lines = order.Lines
            .Where(x => !x.IsDeleted && x.VariantId == variantId)
            .ToList();

        if (!lines.Any())
            return;

        foreach (var line in lines)
        {
            var multiplier = line.Multiplier <= 0 ? 1m : line.Multiplier;
            line.BaseQuantity = line.Quantity * multiplier;
        }

        var totalBaseQty = lines.Sum(x => x.BaseQuantity);

        var activeConversions = variant.UnitConversions?
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderByDescending(x => x.Factor)
            .ToList() ?? new();

        ProductUnitConversion? priceConversion = null;

        // Ưu tiên thùng nếu tổng số lượng gốc >= 48
        priceConversion = activeConversions
            .Where(x => x.Factor >= 48 && totalBaseQty >= x.Factor)
            .OrderByDescending(x => x.Factor)
            .FirstOrDefault();

        // Nếu chưa đủ thùng thì xét lốc >= 4
        priceConversion ??= activeConversions
            .Where(x => x.Factor >= 4 && x.Factor < 48 && totalBaseQty >= x.Factor)
            .OrderByDescending(x => x.Factor)
            .FirstOrDefault();

        foreach (var line in lines)
        {
            var currentMultiplier = line.Multiplier <= 0 ? 1m : line.Multiplier;

            if (priceConversion != null)
            {
                var packPrice = ResolveSalePriceByTier(
                    variant,
                    priceConversion,
                    priceTier);

                if (packPrice > 0 && priceConversion.Factor > 0)
                {
                    line.UnitPrice = Math.Round(
                        packPrice / priceConversion.Factor * currentMultiplier,
                        6,
                        MidpointRounding.AwayFromZero);
                }

                continue;
            }

            // Không đủ lốc/thùng thì lấy giá đúng đơn vị dòng hiện tại.
            var currentConversion = activeConversions
       .FirstOrDefault(x =>
           line.ProductUnitConversionId.HasValue &&
           x.Id == line.ProductUnitConversionId.Value);

            currentConversion ??= activeConversions
    .FirstOrDefault(x =>
        x.UnitId == line.SellingUnitId);

            line.UnitPrice = ResolveSalePriceByTier(
                variant,
                currentConversion,
                priceTier);
        }
    }
    #endregion

}
