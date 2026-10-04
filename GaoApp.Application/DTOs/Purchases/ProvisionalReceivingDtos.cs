using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public sealed class ProvisionalReceivingStateDto
{
    public int StockDocumentId { get; set; }
    public PurchaseReceiptSource ReceiptSource { get; set; }
    public int ReceivingRevision { get; set; }
    public string DocumentRowVersion { get; set; } = string.Empty;
    public int UnresolvedCount { get; set; }
    public List<string> PermittedActions { get; set; } = [];
    public List<StockDocumentProvisionalItemDto> Items { get; set; } = [];
    public IReadOnlyList<ReceiptIntakeRecentDto>? RecentReceipts { get; set; }
}

public sealed class StockDocumentProvisionalItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RawBarcode { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public int? ProposedProductVariantId { get; set; }
    public int? ProposedBaseUnitId { get; set; }
    public string? ProposedBaseUnitName { get; set; }
    public decimal? ProposedFactor { get; set; }
    public int? ProposedCategoryId { get; set; }
    public string? Note { get; set; }
    public bool HasPhoto { get; set; }
    public bool HasReviewPhoto { get; set; }
    public ReceiptIntakeCompletionDto? ReviewDraft { get; set; }
    public StockDocumentProvisionalItemStatus Status { get; set; }
    public int? ResolvedStockDocumentLineId { get; set; }
    public ReceiptAllocationKind? ResolutionAllocationKind { get; set; }
    public OutsidePoDecisionStatus? OutsidePoDecisionStatus { get; set; }
    public string ResolutionOutcome { get; set; } = "Unresolved";
    public string BarcodeState { get; set; } = "None";
    public List<string> PermittedActions { get; set; } = [];
    public bool RawBarcodeRemembered { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class ProvisionalReceivingMutationRequest
{
    public Guid CommandId { get; set; }
    public Guid? LeaseToken { get; set; }
    [Required] public string DocumentRowVersion { get; set; } = string.Empty;
}

public sealed class CaptureProvisionalItemRequest : ProvisionalReceivingMutationRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(256)] public string? RawBarcode { get; set; }
    public int? UnitId { get; set; }
    [StringLength(100)] public string? UnitName { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; } = 1m;
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class EditProvisionalItemRequest : ProvisionalReceivingMutationRequest
{
    [Required] public string ItemRowVersion { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    public int? UnitId { get; set; }
    [StringLength(100)] public string? UnitName { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

public sealed class IncrementProvisionalItemRequest : ProvisionalReceivingMutationRequest
{
    [Required] public string ItemRowVersion { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.001", "999999999999999.999")] public decimal Quantity { get; set; } = 1m;
}

public sealed class RemoveProvisionalItemRequest : ProvisionalReceivingMutationRequest
{
    [Required] public string ItemRowVersion { get; set; } = string.Empty;
}

public sealed class ResolveProvisionalItemRequest
{
    public Guid CommandId { get; set; }
    [Range(1, int.MaxValue)] public int ProductVariantId { get; set; }
    [Range(1, int.MaxValue)] public int ProductUnitConversionId { get; set; }
    public bool RememberRawBarcode { get; set; }
    [Required] public string DocumentRowVersion { get; set; } = string.Empty;
    [Required] public string ItemRowVersion { get; set; } = string.Empty;
}

public sealed class QuickCreateAndResolveProvisionalItemRequest
{
    public Guid CommandId { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    public int? SupplierId { get; set; }
    public int? UnitId { get; set; }
    [StringLength(200)] public string? NewUnitName { get; set; }
    [StringLength(200)] public string? ProductName { get; set; }
    public bool RememberRawBarcode { get; set; }
    [Required] public string DocumentRowVersion { get; set; } = string.Empty;
    [Required] public string ItemRowVersion { get; set; } = string.Empty;
}
