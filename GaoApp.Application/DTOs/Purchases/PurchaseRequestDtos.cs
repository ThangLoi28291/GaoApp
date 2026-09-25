using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public sealed class PurchaseRequestLineInputDto
{
    public int? Id { get; set; }
    public PurchaseItemKind ItemKind { get; set; } = PurchaseItemKind.Catalog;
    [Range(1, int.MaxValue)] public int? ProductVariantId { get; set; }
    [Range(1, int.MaxValue)] public int? ProductUnitConversionId { get; set; }
    [StringLength(250)] public string? ProductName { get; set; }
    [StringLength(100)] public string? UnitName { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999")] public decimal Quantity { get; set; }
}

public sealed class SavePurchaseRequestRequest
{
    public int? Id { get; set; }
    [Required, StringLength(250)] public string Title { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; } = DateTime.Today;
    public DateTime? NeedByDate { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public string? RowVersion { get; set; }
    [MinLength(1)] public List<PurchaseRequestLineInputDto> Lines { get; set; } = new();
}

public sealed class PurchaseRequestListItemDto
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime? NeedByDate { get; set; }
    public PurchaseRequestStatus Status { get; set; }
    public int RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal ApprovedQuantity { get; set; }
    public decimal ConvertedQuantity { get; set; }
}

public sealed class PurchaseRequestLineDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }
    public PurchaseItemKind ItemKind { get; set; }
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? UnitId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public decimal ConvertedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    /// <summary>Tồn hiện tại đã quy đổi sang UnitName của dòng.</summary>
    public decimal CurrentStockQuantity { get; set; }
    /// <summary>Số lượng đang về từ PO đã duyệt/gửi, đã quy đổi sang UnitName của dòng.</summary>
    public decimal IncomingQuantity { get; set; }
}

/// <summary>Số lượng kho dạng đơn vị gốc, dùng nội bộ giữa repository và service.</summary>
public sealed class PurchaseRequestInventoryContextDto
{
    public int ProductVariantId { get; set; }
    public decimal CurrentStockBaseQuantity { get; set; }
    public decimal IncomingBaseQuantity { get; set; }
}

public sealed class PurchaseRequestActionDto
{
    public PurchaseRequestActionType ActionType { get; set; }
    public PurchaseRequestStatus FromStatus { get; set; }
    public PurchaseRequestStatus ToStatus { get; set; }
    public int? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
    public int? PurchaseOrderId { get; set; }
}

public sealed class PurchaseRequestPurchaseOrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }
    public decimal TotalAfterVat { get; set; }
}

public sealed class PurchaseRequestDetailDto
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime? NeedByDate { get; set; }
    public string? Note { get; set; }
    public string? WorkflowNote { get; set; }
    public PurchaseRequestStatus Status { get; set; }
    public int RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<PurchaseRequestLineDto> Lines { get; set; } = new();
    public List<PurchaseRequestActionDto> Actions { get; set; } = new();
    public List<PurchaseRequestPurchaseOrderDto> PurchaseOrders { get; set; } = new();
}

public class PurchaseRequestWorkflowRequest
{
    [StringLength(1000)] public string? Note { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class PurchaseRequestApprovalLineRequest
{
    [Range(1, int.MaxValue)] public int PurchaseRequestLineId { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal ApprovedQuantity { get; set; }
}

public sealed class ApprovePurchaseRequestRequest : PurchaseRequestWorkflowRequest
{
    /// <summary>
    /// Rỗng nghĩa là duyệt toàn bộ số lượng yêu cầu. Nếu có dữ liệu thì phải gửi đủ mọi dòng.
    /// </summary>
    public List<PurchaseRequestApprovalLineRequest> Lines { get; set; } = new();
}

public sealed class PurchaseRequestConversionLineRequest
{
    [Range(1, int.MaxValue)] public int PurchaseRequestLineId { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999")] public decimal Quantity { get; set; }
    // Giữ lại để tương thích client cũ; PO số lượng-only không còn dùng giá/VAT này.
    [Range(typeof(decimal), "0", "999999999999")] public decimal? UnitPriceBeforeVat { get; set; }
    public int? TaxId { get; set; }
}

public sealed class PurchaseRequestOrderGroupRequest
{
    /// <summary>Khóa ổn định do client sinh để retry không tạo trùng đơn.</summary>
    [Required, StringLength(64)] public string ClientGroupKey { get; set; } = string.Empty;
    [StringLength(250)] public string? Title { get; set; }
    public int? SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int ExpectedWarehouseId { get; set; }
    [Range(1, int.MaxValue)] public int LegalEntityId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public bool HasVat { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    [MinLength(1)] public List<PurchaseRequestConversionLineRequest> Lines { get; set; } = new();
}

public sealed class ConvertPurchaseRequestRequest
{
    public string? RowVersion { get; set; }
    [MinLength(1)] public List<PurchaseRequestOrderGroupRequest> Orders { get; set; } = new();
}

public sealed class ConvertPurchaseRequestResultDto
{
    public PurchaseRequestStatus RequestStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<int> PurchaseOrderIds { get; set; } = new();
}

public sealed class PurchaseRequestPreparationLineDto
{
    public int PurchaseRequestLineId { get; set; }
    public PurchaseItemKind ItemKind { get; set; }
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; }
    public decimal ApprovedQuantity { get; set; }
    public decimal ConvertedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public int? SuggestedSupplierId { get; set; }
    public string SuggestedSupplierName { get; set; } = string.Empty;
    public int? DefaultTaxId { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public decimal? SuggestedUnitPriceAfterVat { get; set; }
}

public sealed class PurchaseRequestPreparationDto
{
    public int PurchaseRequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
    public int? DefaultLegalEntityId { get; set; }
    public List<PurchaseLookupOptionDto> LegalEntities { get; set; } = new();
    public List<PurchaseLookupOptionDto> Warehouses { get; set; } = new();
    public List<PurchaseLookupOptionDto> Taxes { get; set; } = new();
    public List<PurchaseRequestPreparationLineDto> Lines { get; set; } = new();
}

public sealed class PurchaseRequestProductLookupDto
{
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public decimal CurrentStockBaseQuantity { get; set; }
    public decimal IncomingBaseQuantity { get; set; }
    public bool HasOpenRequest { get; set; }
    public int OpenRequestCount { get; set; }
    public decimal OpenRequestBaseQuantity { get; set; }
    public string? OpenRequestNumber { get; set; }
    public List<PurchaseRequestProductUnitOptionDto> UnitOptions { get; set; } = new();
}

public sealed class PurchaseRequestProductUnitOptionDto
{
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; }
    public bool IsDefaultForSale { get; set; }
    public bool IsBarcodeMatch { get; set; }
    public bool IsHistoricalBarcodeMatch { get; set; }
}
