using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public sealed class PurchaseOrderLineInputDto
{
    public int? Id { get; set; }
    public PurchaseItemKind ItemKind { get; set; } = PurchaseItemKind.Catalog;
    [Range(1, int.MaxValue)] public int? ProductVariantId { get; set; }
    [Range(1, int.MaxValue)] public int? ProductUnitConversionId { get; set; }
    [StringLength(250)] public string? ProductName { get; set; }
    [StringLength(100)] public string? UnitName { get; set; }
    public int? TaxId { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal UnitPriceBeforeVat { get; set; }
}

public sealed class SavePurchaseOrderRequest
{
    public int? Id { get; set; }
    [StringLength(250)] public string? Title { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int ExpectedWarehouseId { get; set; }
    [Range(1, int.MaxValue)] public int LegalEntityId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? ExpectedDeliveryDate { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    [StringLength(500)] public string? OutsideRequestReason { get; set; }
    public bool HasVat { get; set; }
    public string? RowVersion { get; set; }
    [MinLength(1)] public List<PurchaseOrderLineInputDto> Lines { get; set; } = new();
}

public sealed class UpdateSourcePurchaseOrderCommercialLineRequest
{
    [Range(1, int.MaxValue)] public int PurchaseOrderLineId { get; set; }
    public int? TaxId { get; set; }
    [Range(typeof(decimal), "0", "999999999999")]
    public decimal UnitPriceBeforeVat { get; set; }
}

public sealed class UpdateSourcePurchaseOrderCommercialRequest
{
    [StringLength(250)] public string? Title { get; set; }
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Range(1, int.MaxValue)] public int ExpectedWarehouseId { get; set; }
    [Range(1, int.MaxValue)] public int LegalEntityId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public bool HasVat { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MinLength(1)]
    public List<UpdateSourcePurchaseOrderCommercialLineRequest> Lines { get; set; } = new();
}

public sealed class PurchaseOrderListQueryDto
{
    [StringLength(30)] public string Tab { get; set; } = "awaiting";
    [StringLength(100)] public string? Search { get; set; }
    public int? LegalEntityId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class PurchaseOrderListResultDto
{
    public PurchaseOrderListQueryDto Query { get; set; } = new();
    public PagedResult<PurchaseOrderListItemDto> Orders { get; set; } = new();
    public Dictionary<string, int> TabCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PurchaseOrderListItemDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int? SourcePurchaseRequestId { get; set; }
    public DateTime OrderDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string LegalEntityName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }
    public decimal TotalAfterVat { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity { get; set; }
    public int? CreatedByUserId { get; set; }
}

public sealed class PurchaseOrderLineDto
{
    public int Id { get; set; }
    public int? SourcePurchaseRequestLineId { get; set; }
    public int LineNo { get; set; }
    public PurchaseItemKind ItemKind { get; set; }
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? UnitId { get; set; }
    public int? TaxId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal UnitPriceBeforeVat { get; set; }
    public decimal TaxRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal UnitPriceAfterVat { get; set; }
    public decimal LineTotalBeforeVat { get; set; }
    public decimal LineTotalAfterVat { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal ConfirmedReceivedQuantity { get; set; }
    public int? BaseUnitId { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal CanonicalOrderedQuantity { get; set; }
    public decimal CanonicalConfirmedReceivedQuantity { get; set; }
    public decimal InFlightQuantity { get; set; }
    public decimal AvailableToAllocateQuantity { get; set; }
    public decimal ConfirmedOverdeliveryQuantity { get; set; }
    public decimal ProjectedOverdeliveryQuantity { get; set; }
    public List<PurchaseReceiptUnitOptionDto> AllowedReceiptUnits { get; set; } = new();
    public decimal PendingQuantity { get; set; }
    public decimal ShortClosedQuantity { get; set; }
    public PurchaseOrderLineReceiptStatus ReceiptStatus { get; set; }
    public string? ShortCloseReason { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public string? ResolvedProductName { get; set; }
    public string? ResolvedSku { get; set; }
    public string? ResolvedUnitName { get; set; }
}

public sealed class PurchaseReceiptUnitOptionDto
{
    public int UnitId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; }
    public decimal MaximumReceiptQuantity { get; set; }
    public decimal MaximumEntryQuantity { get; set; }
    public bool IsOrderedUnit { get; set; }
    public bool IsBaseUnit { get; set; }
}

public sealed class PurchaseOrderActionDto
{
    public PurchaseOrderActionType ActionType { get; set; }
    public PurchaseOrderStatus FromStatus { get; set; }
    public PurchaseOrderStatus ToStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int? StockDocumentId { get; set; }
}

public sealed class PurchaseOrderReceiptDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public StockDocumentStatus Status { get; set; }
    public DateTime DocumentDate { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class PurchaseOrderDetailDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int? SourcePurchaseRequestId { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public string? SupplierEmail { get; set; }
    public string? SupplierAddress { get; set; }
    public string? SupplierContactName { get; set; }
    public string? SupplierTaxCode { get; set; }
    public int ExpectedWarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseCode { get; set; } = string.Empty;
    public string? WarehouseLocation { get; set; }
    public int LegalEntityId { get; set; }
    public string LegalEntityName { get; set; } = string.Empty;
    public string LegalEntityCode { get; set; } = string.Empty;
    public string LegalEntityLegalName { get; set; } = string.Empty;
    public string? LegalEntityTaxCode { get; set; }
    public string? LegalEntityAddress { get; set; }
    public string? LegalEntityPhone { get; set; }
    public string? LegalEntityEmail { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? Note { get; set; }
    public string? OutsideRequestReason { get; set; }
    public string? WorkflowNote { get; set; }
    public bool HasVat { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public decimal SubtotalBeforeVat { get; set; }
    public decimal VatTotal { get; set; }
    public decimal TotalAfterVat { get; set; }
    public int? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }
    public string? SubmittedByName { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public int? ReturnedByUserId { get; set; }
    public string? ReturnedByName { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public int? RejectedByUserId { get; set; }
    public string? RejectedByName { get; set; }
    public DateTime? SentToSupplierAtUtc { get; set; }
    public int? SentToSupplierByUserId { get; set; }
    public string? SentToSupplierByName { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }
    public string? CancelledByName { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<PurchaseOrderLineDto> Lines { get; set; } = new();
    public List<PurchaseOrderActionDto> Actions { get; set; } = new();
    public List<PurchaseOrderReceiptDto> Receipts { get; set; } = new();
}

public sealed class PurchaseLookupOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Phone { get; set; }
    public string? TaxCode { get; set; }
    public int? LegalEntityId { get; set; }
    public decimal? TaxRate { get; set; }
    public int? TaxId { get; set; }
}

public sealed class PurchaseProductOptionDto
{
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public int? DefaultTaxId { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public decimal SuggestedUnitPriceAfterVat { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
}

/// <summary>
/// Lightweight projection used by the remote product picker. One item represents
/// one purchasing unit conversion, not merely a product variant.
/// </summary>
public sealed class PurchaseProductLookupDto
{
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public int? DefaultTaxId { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public decimal SuggestedUnitPriceAfterVat { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public bool SupplierMatch { get; set; }
}

public sealed class PurchaseSupplierLookupDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TaxCode { get; set; }
}

public sealed class PurchaseLookupPageDto<T>
{
    public List<T> Items { get; set; } = new();
    public bool HasMore { get; set; }
}

public sealed class PurchaseOrderFormOptionsDto
{
    public int? DefaultLegalEntityId { get; set; }
    public List<PurchaseLookupOptionDto> Suppliers { get; set; } = new();
    public List<PurchaseLookupOptionDto> LegalEntities { get; set; } = new();
    public List<PurchaseLookupOptionDto> Warehouses { get; set; } = new();
    public List<PurchaseLookupOptionDto> Taxes { get; set; } = new();
    public List<PurchaseProductOptionDto> Products { get; set; } = new();
}

public sealed class PurchaseWorkflowRequest
{
    [StringLength(1000)] public string? Note { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class ManagePurchaseOrderOutstandingRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class CreatePurchaseReceiptLineRequest
{
    [Range(1, int.MaxValue)] public int PurchaseOrderLineId { get; set; }
    [Range(1, int.MaxValue)] public int? ReceiptUnitId { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999")] public decimal Quantity { get; set; }
    public PurchaseShortageDisposition ShortageDisposition { get; set; }
    [StringLength(500)] public string? ShortageReason { get; set; }
}

public sealed class CreatePurchaseReceiptRequest
{
    public DateTime? DocumentDate { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public List<CreatePurchaseReceiptLineRequest> Lines { get; set; } = new();
}
