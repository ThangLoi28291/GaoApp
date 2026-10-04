namespace GaoApp.Application.DTOs.Purchases;

public sealed record ReceiptIntakeRecentDto(Guid CommandId, string RowKey, string Name,
    string? Barcode, string UnitName, decimal Quantity, decimal Factor,
    decimal CurrentQuantity, DateTime OccurredAtUtc, bool IsRemoved, int? PhotoItemId);

public sealed class CaptureReceiptIntakeRequest : ProvisionalReceivingMutationRequest
{
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public int? ProductVariantId { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public int? BaseUnitId { get; set; }
    public string? BaseUnitName { get; set; }
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public int? CategoryId { get; set; }
    public string? Note { get; set; }
    public bool ApproveNow { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(349556)]
    public string? PhotoDataUrl { get; set; }
}

public sealed class ReviewReceiptIntakeRequest : ProvisionalReceivingMutationRequest
{
    public string ItemRowVersion { get; set; } = "";
    public int? CategoryId { get; set; }
    public bool Approve { get; set; }
    public bool SaveDraftOnly { get; set; }
    public ReceiptIntakeCompletionDto? Completion { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(349556)]
    public string? PhotoDataUrl { get; set; }
    public bool RemoveReviewPhoto { get; set; }
}

public sealed class UpdateReceiptIntakeQuantityRequest : ProvisionalReceivingMutationRequest
{
    public string ItemRowVersion { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed record ReceiptIntakePermissions(bool CanCreateProduct, bool CanUpdateProduct,
    bool CanCreateUnit, bool CanCreateBarcode);

public sealed class RecordKnownReceiptItemRequest : ProvisionalReceivingMutationRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(349556)]
    public string? PhotoDataUrl { get; set; }
    public int ProductUnitConversionId { get; set; }
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public string? Barcode { get; set; }
    public string? Note { get; set; }
}

public sealed class ReceiptIntakeCompletionDto
{
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public int? ProductVariantId { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public int? BaseUnitId { get; set; }
    public string? BaseUnitName { get; set; }
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? RetailPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public bool IsSellable { get; set; }
    public bool MakeBarcodePrimary { get; set; } = true;
    public bool GenerateBaseBarcode { get; set; }
    public bool UsePackagingPhoto { get; set; }
    public string? Note { get; set; }
}
