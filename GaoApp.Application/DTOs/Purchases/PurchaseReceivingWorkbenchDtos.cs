using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public sealed class PurchaseReceivingWorkbenchDto
{
    public int StockDocumentId { get; set; }
    public int PurchaseOrderId { get; set; }
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public string ReceiptNumber { get; set; } = string.Empty;
    public StockDocumentStatus Status { get; set; }
    public ReceivingSessionState SessionState { get; set; }
    public int? OwnerUserId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? LastSavedAtUtc { get; set; }
    public int Revision { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<PurchaseReceivingComponentDto> Components { get; set; } = [];
    public List<PurchaseReceivingProgressDto> Progress { get; set; } = [];
    public bool HasPendingOutsidePo { get; set; }
    public bool CanUndo { get; set; }
    public List<StockDocumentProvisionalItemDto> ProvisionalItems { get; set; } = [];
    public int UnresolvedProvisionalCount { get; set; }
}

public sealed class PurchaseReceivingComponentDto
{
    public int Id { get; set; }
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public bool IsBaseUnit { get; set; }
    public decimal Quantity { get; set; }
    public decimal Factor { get; set; }
    public decimal BaseQuantity { get; set; }
    public ReceiptAllocationKind AllocationKind { get; set; }
    public OutsidePoDecisionStatus OutsideStatus { get; set; }
}

public sealed class PurchaseReceivingProgressDto
{
    public int PurchaseOrderLineId { get; set; }
    public int ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string OrderedUnitName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal OrderedBaseQuantity { get; set; }
    public decimal ConfirmedQuantity { get; set; }
    public decimal ConfirmedBaseQuantity { get; set; }
    public decimal CurrentReceiptBaseQuantity { get; set; }
    public decimal RemainingBaseQuantity { get; set; }
    public decimal ProjectedOverdeliveryBaseQuantity { get; set; }
}

public class PurchaseReceivingLeaseRequest
{
    public Guid? LeaseToken { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class PurchaseReceivingScanRequest : PurchaseReceivingLeaseRequest
{
    public Guid CommandId { get; set; }
    public string Barcode { get; set; } = string.Empty;
}

public sealed class PurchaseReceivingAddRequest : PurchaseReceivingLeaseRequest
{
    public Guid CommandId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; } = 1m;
}

public sealed class PurchaseReceivingEditRequest : PurchaseReceivingLeaseRequest
{
    public Guid CommandId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class PurchaseReceivingCommandRequest : PurchaseReceivingLeaseRequest
{
    public Guid CommandId { get; set; }
}

public sealed class PurchaseReceivingOutsideDecisionRequest
{
    public bool Accept { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
}
