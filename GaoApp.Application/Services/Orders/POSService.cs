using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
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
           IProductVariantRepository productVariantRepository)
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
                InventoryResolutionStatus = order.InventoryResolutionStatus.ToString()
            }),
            ChangedColumnsJson = JsonSerializer.Serialize(new[]
            {
            "Status",
            "PaymentStatus",
            "OrderNumber",
            "GrandTotal",
            "HasInventoryIssue",
"InventoryResolutionStatus"
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

    private static void EnsureCanBeCurrentCart(Order order, int shiftId)
    {
        if (order.POSShiftId != shiftId)
            throw new InvalidOperationException("Đơn không thuộc ca POS hiện tại.");

        if (order.Status != OrderStatus.Draft)
            throw new InvalidOperationException("Chỉ đơn Draft mới có thể là giỏ hiện tại.");
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
    private static PosSellingUnitInfo ResolvePreferredSellingUnit(ProductVariant variant)
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
                UnitPrice = chosen.Price ?? variant.Price ?? product.BasePrice,
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
            UnitPrice = variant.Price ?? product.BasePrice,
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
            x.VariantId == lookup.ProductVariantId &&
            x.SellingUnitId == lookup.UnitId &&
            x.UnitPrice == unitPrice);

        if (existing != null)
        {
            existing.Quantity += qty;
            existing.BaseQuantity += baseQtyToAdd;
            existing.Multiplier = multiplier;
            existing.ScannedBarcode = lookup.Barcode;
            existing.BarcodeSource = barcodeSource;
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
            OrderId = order.Id,
            ProductId = lookup.ProductId,
            VariantId = lookup.ProductVariantId,

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

        if (order == null || order.Status != OrderStatus.Draft)
            throw new InvalidOperationException("Không tìm thấy giỏ hiện tại hợp lệ.");

        return order;
    }

    private static bool IsEmptyDraft(Order order)
    {
        if (order.Status != OrderStatus.Draft)
            return false;

        var hasLines = order.Lines.Any(x => !x.IsDeleted);
        var hasPayments = order.Payments.Any(x => !x.IsDeleted);

        return !hasLines && !hasPayments;
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
                    ProductUnitConversionId = line.SellingUnitId, // nếu sau này bạn có field riêng thì map lại
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
        var internalNote = BuildInventoryIssueInternalNote(summary);

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
                InternalNote = internalNote,
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
                Note = internalNote
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
        var warehouse = await ResolveWarehouseForOrderAsync(order, ct);

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
      warehouse.Id,
      line.VariantId,
      order.Id,
      line.Id,
      qtyToAddBack,
      entry.UnitCost,
      reason,
      DateTime.UtcNow);

                // Subkey để phân biệt từng valuation part
                movementRequest.ReferenceSubKey = $"void-from-val-{entry.Id}";

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

    public async Task<OrderReceiptDto> RefundCompletedOrderAsync(int orderId, string reason, CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Lý do trả hàng / hoàn tiền không được để trống.");

        var order = await _orders.GetByIdWithDetailsAsync(orderId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Chỉ được refund toàn phần cho đơn đã hoàn tất.");

        var request = new GaoApp.Application.DTOs.Returns.CreateSalesReturnRequest
        {
            OrderId = order.Id,
            POSShiftId = order.POSShiftId,
            Type = GaoApp.Domain.Enums.SalesReturnType.ReturnAndRefund,
            Reason = reason,
            Note = "Refund toàn phần từ flow cũ.",
            Lines = order.Lines
                .Where(x => !x.IsDeleted)
                .Select(x => new GaoApp.Application.DTOs.Returns.CreateSalesReturnLineRequest
                {
                    OrderLineId = x.Id,
                    ReturnQuantity = x.Quantity,
                    ReturnBaseQuantity = x.BaseQuantity > 0 ? x.BaseQuantity : x.Quantity,
                    RefundUnitAmount = x.Quantity > 0 ? (x.LineTotal / x.Quantity) : x.UnitPrice,
                    Action = GaoApp.Domain.Enums.SalesReturnLineAction.Restock,
                    Reason = reason
                })
                .ToList(),
            Payments = order.Payments
                .Where(x => !x.IsDeleted)
                .Select(x => new GaoApp.Application.DTOs.Returns.CreateSalesReturnPaymentRequest
                {
                    Method = x.Method,
                    Amount = x.Amount,
                    ReferenceCode = x.ReferenceCode,
                    Provider = x.Provider,
                    Note = $"Refund full from order {order.OrderNumber}"
                })
                .ToList()
        };

        await _salesReturnService.CreateAsync(request, ct);

        order.Status = OrderStatus.Refunded;
        order.PaymentStatus = PaymentStatus.Refunded;

        await _orders.SaveChangesAsync(ct);

        return await GetReceiptAsync(order.Id, ct);
    }

    public async Task<int> CreateDraftAsync(int? customerId = null, string? note = null, CancellationToken ct = default)
    {
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Không thể tạo đơn: chưa mở ca POS.");

        var order = new Order
        {
            CustomerId = customerId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = OrderStatus.Draft,
            PaymentStatus = PaymentStatus.Unpaid,
            POSShiftId = openShift.Id
        };

        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        openShift.CurrentOrderId = order.Id;
        await _orders.SaveChangesAsync(ct);

        return order.Id;
    }

    public async Task<OrderDraftDto> GetDraftAsync(int orderId, CancellationToken ct = default)
    {
        var order = await RequireDraftAsync(orderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return Map(order);
    }

    public async Task<OrderDraftDto> AddItemAsync(int orderId, int variantId, decimal qty = 1, CancellationToken ct = default)
    {
        if (qty <= 0) qty = 1;

        var order = await RequireDraftAsync(orderId, ct);

        var variant = await _variants.GetActiveWithProductAsync(variantId, ct)
                     ?? throw new InvalidOperationException("Variant không tồn tại hoặc đang bị khóa.");

        var product = variant.Product
                      ?? throw new InvalidOperationException("Variant thiếu Product navigation.");

        // Không còn lấy theo variant.Barcode nữa.
        // POS sẽ tự resolve đơn vị bán mặc định + barcode đại diện.
        var sellingInfo = ResolvePreferredSellingUnit(variant);

        var unitPrice = SafePositive(sellingInfo.UnitPrice ?? 0m, 0m);
        var barcodeSource = BarcodeLookupSourceType.ManualVariant;
        var barcode = sellingInfo.Barcode;

        var existing = order.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
            x.VariantId == variantId &&
            x.SellingUnitId == sellingInfo.SellingUnitId &&
            x.UnitPrice == unitPrice);

        if (existing != null)
        {
            existing.Quantity += qty;
            existing.BaseQuantity += ComputeBaseQuantity(qty, sellingInfo.Multiplier);
            existing.Multiplier = sellingInfo.Multiplier;
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

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await GetDraftAsync(order.Id, ct);
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

                    var sellingInfo = ResolvePreferredSellingUnit(variant);

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

        AddOrMergeLineFromBarcode(order, lookup, qty);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return await GetDraftAsync(order.Id, ct);
    }

    public async Task<OrderDraftDto> UpdateLineQtyAsync(int lineId, decimal qty, CancellationToken ct = default)
    {
        if (qty <= 0) qty = 1;

        var line = await _orders.GetDraftLineAsync(lineId, ct)
                   ?? throw new InvalidOperationException("Line không tồn tại hoặc đơn không còn Draft.");

        line.Quantity = qty;

        var multiplier = line.Multiplier <= 0 ? 1m : line.Multiplier;
        line.BaseQuantity = qty * multiplier;

        var order = await RequireDraftAsync(line.OrderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return Map(order);
    }

    public async Task<OrderDraftDto> RemoveLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _orders.GetDraftLineAsync(lineId, ct)
                   ?? throw new InvalidOperationException("Line không tồn tại hoặc đơn không còn Draft.");

        line.IsDeleted = true;

        var order = await RequireDraftAsync(line.OrderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return Map(order);
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

        return Map(order);
    }

    public async Task<OrderDraftDto> RemovePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _payments.GetDraftPaymentAsync(paymentId, ct)
            ?? throw new InvalidOperationException("Payment không tồn tại hoặc đơn không còn Draft.");

        payment.IsDeleted = true;

        var order = await RequireDraftAsync(payment.OrderId, ct);

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        return Map(order);
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
            var finalizeInventorySummary = await ApplyInventoryForFinalizeAsync(order, ct);

            // Bán hàng vẫn complete bình thường
            order.Status = OrderStatus.Completed;
            order.PaymentStatus = PaymentStatus.Paid;
            order.CompletedAtUtc = DateTime.UtcNow;

            // Nhưng inventory/cost issue thì đi luồng riêng
            await UpsertPendingInventoryIssueAsync(order, finalizeInventorySummary, ct);

            if (string.IsNullOrWhiteSpace(order.OrderNumber))
                order.OrderNumber = await _orderNo.NextAsync(ct);

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
                    order.CompletedAtUtc
                },
                ct: ct);

            // Ghi audit log chung toàn hệ thống
            await WriteFinalizeOrderAuditLogAsync(
                order: order,
                oldStatus: oldStatus,
                oldPaymentStatus: oldPaymentStatus,
                ct: ct);

            await tx.CommitAsync(ct);

            return Map(order);
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
        // - release reservation nếu order đang OnHold
        // - cập nhật status order
        // - cập nhật current cart nếu cần
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var order = await _orders.GetByIdAsync(orderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

            if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.OnHold)
                throw new InvalidOperationException("Chỉ được hủy đơn Draft hoặc đơn đang giữ.");

            var openShift = await _shifts.GetOpenShiftAsync(ct);
            if (openShift != null && openShift.CurrentOrderId == order.Id)
            {
                openShift.CurrentOrderId = null;
            }

            // Nếu đơn đang giữ thì phải nhả reservation trước khi hủy.
            if (order.Status == OrderStatus.OnHold)
            {
                await _inventoryReservationService.ReleaseForOrderAsync(
                    order,
                    string.IsNullOrWhiteSpace(reason) ? "Hủy đơn giữ." : reason,
                    ct);
            }

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
            PaidTotal = order.PaidTotal,
            BalanceDue = order.BalanceDue,
            ChangeDue = order.ChangeDue,
            TotalLines = order.Lines?.Count ?? 0,
            TotalQuantity = order.Lines?.Sum(x => x.Quantity) ?? 0,
            RefundedTotal = refundedTotal,
            RefundableRemaining = refundableRemaining,

            Lines = order.Lines.Select(x => new OrderLineDto
            {
                LineId = x.Id,
                VariantId = x.VariantId,
                ItemName = x.ItemName,
                ProductVariantName = x.Variant != null ? x.Variant.ProductVariantName : null,
                UnitName = x.UnitName,
                Sku = x.Sku ?? string.Empty,
                Barcode = x.Barcode,
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
                BarcodeSource = x.BarcodeSource
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
            throw new InvalidOperationException("Không tìm thấy Draft order (đơn có thể đã giữ, đã hoàn tất hoặc đã hủy).");

        if (order.Status != OrderStatus.Draft)
            throw new InvalidOperationException("Đơn không còn ở trạng thái Draft.");

        return order;
    }

    private static void Recalc(Order order)
    {
        var lines = order.Lines.Where(x => !x.IsDeleted).ToList();

        foreach (var l in lines)
        {
            l.LineTotal = (l.Quantity * l.UnitPrice) - l.LineDiscount;
            if (l.LineTotal < 0)
                l.LineTotal = 0;
        }

        var subtotal = lines.Sum(x => x.Quantity * x.UnitPrice);
        if (subtotal < 0) subtotal = 0;

        var lineDiscountTotal = Math.Max(lines.Sum(x => x.LineDiscount), 0);

        if (order.OrderDiscount < 0)
            order.OrderDiscount = 0;

        if (order.OrderDiscount > subtotal)
            order.OrderDiscount = subtotal;

        order.Subtotal = subtotal;
        order.DiscountTotal = lineDiscountTotal + order.OrderDiscount;
        order.GrandTotal = Math.Max(order.Subtotal - order.DiscountTotal, 0);

        order.PaidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
        if (order.PaidTotal < 0) order.PaidTotal = 0;

        order.BalanceDue = Math.Max(order.GrandTotal - order.PaidTotal, 0);
        order.ChangeDue = Math.Max(order.PaidTotal - order.GrandTotal, 0);

        if (order.PaidTotal <= 0)
            order.PaymentStatus = PaymentStatus.Unpaid;
        else if (order.PaidTotal < order.GrandTotal)
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
        else
            order.PaymentStatus = PaymentStatus.Paid;
    }

    private static void ApplyPaymentsToShift(POSShift shift, Order order)
    {
        var payments = order.Payments.Where(p => !p.IsDeleted).ToList();

        foreach (var payment in payments)
        {
            if (payment.Method == PaymentMethod.Cash)
                shift.CashSalesTotal += payment.Amount;
            else
                shift.NonCashSalesTotal += payment.Amount;
        }

        shift.RecalcExpected();
    }

    private static OrderDraftDto Map(Order order)
    {
        return new OrderDraftDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name,
            CustomerPhone = order.Customer?.Phone,
            Note = order.Note,
            Subtotal = order.Subtotal,
            OrderDiscount = order.OrderDiscount,
            DiscountTotal = order.DiscountTotal,
            GrandTotal = order.GrandTotal,
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
            Lines = order.Lines
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Id)
                .Select(x => new OrderLineDto
                {
                    LineId = x.Id,
                    VariantId = x.VariantId,
                    ItemName = x.ItemName,
                    ProductVariantName = x.Variant != null ? x.Variant.ProductVariantName : null,
                    UnitName = x.UnitName,
                    Sku = x.Sku ?? string.Empty,
                    Barcode = x.Barcode,
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
                    BarcodeSource = x.BarcodeSource
                })
                .ToList()
        };
    }

    public async Task<HoldOrderResultDto> HoldAndCreateNewDraftAsync(
     int orderId,
     string? holdNote = null,
     CancellationToken ct = default)
    {
        // Transaction ngoài cùng:
        // - đổi order thành OnHold
        // - reserve hàng
        // - tạo draft mới
        // - set current cart
        // nếu lỗi giữa chừng sẽ rollback toàn bộ
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var openShift = await _shifts.GetOpenShiftAsync(ct);
            if (openShift == null)
                throw new InvalidOperationException("Không thể giữ đơn: chưa mở ca POS.");

            var order = await RequireDraftAsync(orderId, ct);

            if (order.POSShiftId != openShift.Id)
                throw new InvalidOperationException("Không thể giữ đơn: đơn không thuộc ca POS đang mở.");

            if (!order.Lines.Any(x => !x.IsDeleted))
                throw new InvalidOperationException("Không thể giữ đơn trống.");

            if (order.Payments.Any(x => !x.IsDeleted))
                throw new InvalidOperationException("Đơn đã có thanh toán, vui lòng xóa thanh toán trước khi giữ đơn.");

            order.Status = OrderStatus.OnHold;
            order.HeldAtUtc = DateTime.UtcNow;
            order.HoldNote = string.IsNullOrWhiteSpace(holdNote) ? null : holdNote.Trim();

            if (string.IsNullOrWhiteSpace(order.HoldCode))
                order.HoldCode = GenerateHoldCode(order.Id);

            // Giữ hàng chỉ phát sinh khi đơn chuyển sang OnHold.
            await _inventoryReservationService.RebuildForOrderAsync(order, ct);

            var newDraft = new Order
            {
                Status = OrderStatus.Draft,
                PaymentStatus = PaymentStatus.Unpaid,
                POSShiftId = openShift.Id
            };

            await _orders.AddAsync(newDraft, ct);
            await _orders.SaveChangesAsync(ct);

            openShift.CurrentOrderId = newDraft.Id;
            await _orders.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return new HoldOrderResultDto
            {
                HeldOrderId = order.Id,
                HoldCode = order.HoldCode,
                NewDraftOrderId = newDraft.Id,
                Message = "Đã giữ đơn và tạo đơn nháp mới."
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
        // Resume có nhiều bước:
        // - kiểm tra đơn giữ
        // - xử lý current cart hiện tại
        // - đổi held order về Draft
        // - set CurrentOrderId
        await using var tx = await _uow.BeginTransactionAsync(ct);

        try
        {
            var openShift = await _shifts.GetOpenShiftAsync(ct);
            if (openShift == null)
                throw new InvalidOperationException("Không thể mở lại đơn giữ: chưa mở ca POS.");

            var heldOrder = await _orders.GetByIdAsync(orderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

            if (heldOrder.POSShiftId != openShift.Id)
                throw new InvalidOperationException("Đơn không thuộc ca POS đang mở.");

            if (heldOrder.Status != OrderStatus.OnHold)
                throw new InvalidOperationException("Đơn không ở trạng thái đang giữ.");

            if (openShift.CurrentOrderId.HasValue)
            {
                var currentOrder = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

                if (currentOrder != null &&
                    currentOrder.POSShiftId == openShift.Id &&
                    currentOrder.Status == OrderStatus.Draft)
                {
                    if (currentOrder.Id != heldOrder.Id)
                    {
                        if (IsEmptyDraft(currentOrder))
                        {
                            currentOrder.Status = OrderStatus.Cancelled;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "Giỏ hiện tại đang có dữ liệu. Vui lòng giữ đơn hoặc hủy giỏ hiện tại trước khi lấy lại đơn giữ.");
                        }
                    }
                }
            }

            // Resume chỉ đổi đơn từ OnHold về Draft.
            // Reservation vẫn còn active, không reserve lại, không release.
            heldOrder.Status = OrderStatus.Draft;
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
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

        var orders = await _orders.GetHeldOrdersByShiftAsync(openShift.Id, ct);

        return orders.Select(o => new HeldOrderDto
        {
            OrderId = o.Id,
            HoldCode = o.HoldCode,
            HoldNote = o.HoldNote,
            HeldAtUtc = o.HeldAtUtc,
            LineCount = o.Lines.Count(x => !x.IsDeleted),
            TotalQuantity = o.Lines.Where(x => !x.IsDeleted).Sum(x => x.Quantity),
            Subtotal = o.Lines.Where(x => !x.IsDeleted).Sum(x => x.LineTotal)
        }).ToList();
    }

    public async Task<CurrentCartDto> GetCurrentCartAsync(CancellationToken ct = default)
    {
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

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
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

        EnsureCanBeCurrentCart(order, openShift.Id);

        openShift.CurrentOrderId = order.Id;
        await _orders.SaveChangesAsync(ct);
    }

    public async Task<List<ActiveDraftOrderDto>> GetDraftOrdersAsync(CancellationToken ct = default)
    {
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

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
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

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
                return Map(currentOrder!);
            }

            openShift.CurrentOrderId = null;
            await _orders.SaveChangesAsync(ct);
        }

        var draftOrders = await _orders.GetDraftOrdersByShiftAsync(openShift.Id, ct);

        var latestDraft = draftOrders
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        if (latestDraft != null)
        {
            openShift.CurrentOrderId = latestDraft.Id;
            await _orders.SaveChangesAsync(ct);

            Recalc(latestDraft);
            await _orders.SaveChangesAsync(ct);

            return Map(latestDraft);
        }

        var newOrder = new Order
        {
            Status = OrderStatus.Draft,
            PaymentStatus = PaymentStatus.Unpaid,
            POSShiftId = openShift.Id
        };

        await _orders.AddAsync(newOrder, ct);
        await _orders.SaveChangesAsync(ct);

        openShift.CurrentOrderId = newOrder.Id;
        await _orders.SaveChangesAsync(ct);

        Recalc(newOrder);
        await _orders.SaveChangesAsync(ct);

        return Map(newOrder);
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
        var openShift = await _shifts.GetOpenShiftAsync(ct);
        if (openShift == null)
            throw new InvalidOperationException("Chưa mở ca POS.");

        if (openShift.CurrentOrderId.HasValue)
        {
            var current = await _orders.GetByIdAsync(openShift.CurrentOrderId.Value, ct);

            if (current != null &&
                current.POSShiftId == openShift.Id &&
                current.Status == OrderStatus.Draft)
            {
                if (IsEmptyDraft(current))
                    return current.Id;

                throw new InvalidOperationException(
                    "Giỏ hiện tại đang có dữ liệu. Vui lòng giữ đơn hoặc hủy giỏ hiện tại trước khi tạo giỏ mới.");
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

        return variants.Select(x =>
        {
            var productName = x.Product?.Name?.Trim() ?? string.Empty;

            // Ưu tiên lấy tên biến thể thực tế.
            // Không lấy SKU làm tên biến thể nữa.
            var productVariantName = x.ProductVariantName?.Trim() ?? string.Empty;

            var displayName = string.IsNullOrWhiteSpace(productVariantName)
                ? productName
                : $"{productName} - {productVariantName}";

            var sellingInfo = ResolvePreferredSellingUnit(x);

            return new POSProductSearchItemDto
            {
                VariantId = x.Id,
                ProductId = x.ProductId,
                ProductName = productName,
                ProductVariantName = productVariantName,
                DisplayName = displayName,
                Sku = x.Sku,

                // Barcode đại diện của đơn vị bán ưu tiên
                Barcode = sellingInfo.Barcode ?? ResolveRepresentativeBarcode(x),

                // Giá hiển thị theo đơn vị bán ưu tiên
                Price = sellingInfo.UnitPrice ?? 0m,
                IsActive = x.IsActive
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
            Address = x.Address
        }).ToList();
    }

    public async Task<OrderDraftDto> SetCustomerForCurrentCartAsync(int customerId, CancellationToken ct = default)
    {
        var customer = await _customers.GetActiveByIdAsync(customerId, ct)
            ?? throw new InvalidOperationException("Khách hàng không tồn tại hoặc đã bị khóa.");

        var order = await RequireCurrentDraftAsync(ct);

        order.CustomerId = customer.Id;

        await _orders.SaveChangesAsync(ct);

        return await GetDraftAsync(order.Id, ct);
    }

    public async Task<OrderDraftDto> ClearCustomerForCurrentCartAsync(CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        order.CustomerId = null;

        await _orders.SaveChangesAsync(ct);

        return await GetDraftAsync(order.Id, ct);
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
            IsActive = true
        };

        await _customers.AddAsync(customer, ct);
        await _customers.SaveChangesAsync(ct);

        return await SetCustomerForCurrentCartAsync(customer.Id, ct);
    }

    public async Task<OrderDraftDto> UpdateCurrentCartNoteAsync(string? note, CancellationToken ct = default)
    {
        var order = await RequireCurrentDraftAsync(ct);

        order.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        await _orders.SaveChangesAsync(ct);

        var reloaded = await _orders.GetDraftAsync(order.Id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy giỏ hiện tại.");

        Recalc(reloaded);
        await _orders.SaveChangesAsync(ct);

        return Map(reloaded);
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

        Recalc(order);
        await _orders.SaveChangesAsync(ct);

        var reloaded = await _orders.GetDraftAsync(order.Id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy giỏ hiện tại.");

        return Map(reloaded);
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

        return Map(order);
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

            // Void phải ghi movement ngược chiều để đảo tồn kho đã xuất khi finalize
            await ApplyInventoryForVoidAsync(order, reason, ct);

            order.Status = OrderStatus.Voided;
            order.PaymentStatus = PaymentStatus.Voided;

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

    public async Task<POSShiftDashboardDto> GetCurrentShiftDashboardAsync(CancellationToken ct = default)
    {
        var shift = await _shifts.GetCurrentOpenShiftAsync(ct);

        if (shift == null)
            throw new InvalidOperationException("Chưa có ca POS đang mở.");

        var orders = await _orders.GetByShiftIdAsync(shift.Id, ct);

        var completed = orders.Where(x => x.Status == OrderStatus.Completed).ToList();
        var voided = orders.Where(x => x.Status == OrderStatus.Voided).ToList();
        var refunded = orders.Where(x => x.Status == OrderStatus.Refunded).ToList();

        var cashSales = completed
            .SelectMany(x => x.Payments)
            .Where(p => p.Method == PaymentMethod.Cash)
            .Sum(x => x.Amount);

        var bankSales = completed
            .SelectMany(x => x.Payments)
            .Where(p => p.Method != PaymentMethod.Cash)
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
}