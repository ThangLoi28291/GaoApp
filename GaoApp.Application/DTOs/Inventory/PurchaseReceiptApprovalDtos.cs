using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class FreightAllocationInputDto
{
    public int StockDocumentLineId { get; set; }

    [Range(typeof(decimal), "0", "99999999999999.99")]
    public decimal Amount { get; set; }
}

/// <summary>
/// Financial values confirmed by the approver for one physically received line.
/// Product, unit and quantity deliberately are not part of this command: the
/// server always keeps those values from the submitted receipt.
/// </summary>
public sealed class PurchaseReceiptFinancialLineInputDto
{
    public int StockDocumentLineId { get; set; }

    [Range(typeof(decimal), "0.01", "99999999999999.99")]
    public decimal UnitPriceBeforeVat { get; set; }

    /// <summary>
    /// Last confirmed price shown to the approver in this receipt unit. The
    /// server rejects a stale snapshot and independently reads current history.
    /// </summary>
    [Range(typeof(decimal), "0", "99999999999999.99")]
    public decimal? ExpectedLastPurchaseUnitPriceBeforeVat { get; set; }
    public int? TaxId { get; set; }
}

/// <summary>
/// Atomic commercial approval command. Prices, VAT, merchandise settlement and
/// freight are validated and persisted in the same database transaction that
/// posts inventory, FIFO and payables.
/// </summary>
public sealed class ApprovePurchaseReceiptCommercialRequest
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? ApprovalNote { get; set; }

    /// <summary>
    /// Explicit manager/admin acceptance when the locked confirmation state
    /// shows that this receipt increases purchase-order overdelivery.
    /// </summary>
    public bool AcceptOverdelivery { get; set; }

    [StringLength(1000)]
    public string? OverdeliveryNote { get; set; }

    /// <summary>
    /// Explicit manager/admin acknowledgement when at least one submitted
    /// price differs from the latest confirmed purchase price. The server
    /// independently recomputes the variance before confirmation.
    /// </summary>
    public bool AcceptPriceVariance { get; set; }

    public bool HasVat { get; set; }
    public int? SupplierId { get; set; }
    public bool IsMerchandisePaid { get; set; }
    [StringLength(250)]
    public string? MerchandisePayeeName { get; set; }

    [MinLength(1)]
    public List<PurchaseReceiptFinancialLineInputDto> Lines { get; set; } = new();

    public bool HasFreight { get; set; }
    [Range(typeof(decimal), "0", "99999999999999.99")]
    public decimal FreightTotal { get; set; }

    [StringLength(250)]
    public string? FreightPayeeName { get; set; }

    [StringLength(1000)]
    public string? FreightNote { get; set; }
    public bool IsFreightPaid { get; set; }
    public bool ResetAutomaticAllocation { get; set; }
    public List<FreightAllocationInputDto> Allocations { get; set; } = new();
}

public sealed class UpdatePurchaseReceiptApprovalRequest
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;
    public bool HasFreight { get; set; }

    [Range(typeof(decimal), "0", "99999999999999.99")]
    public decimal FreightTotal { get; set; }

    [StringLength(250)]
    public string? FreightPayeeName { get; set; }

    [StringLength(1000)]
    public string? FreightNote { get; set; }
    public bool IsFreightPaid { get; set; }
    public bool ResetAutomaticAllocation { get; set; }
    public List<FreightAllocationInputDto> Allocations { get; set; } = new();
}
