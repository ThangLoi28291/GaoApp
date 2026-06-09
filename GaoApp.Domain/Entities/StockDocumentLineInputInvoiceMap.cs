using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Map từng dòng nhập kho với dòng hóa đơn XML.
/// Đây là bảng quyết định dòng nhập đó có thuộc hóa đơn XML hay không.
/// </summary>
[Table("StockDocumentLineInputInvoiceMap")]
public class StockDocumentLineInputInvoiceMap : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = default!;

    public int StockDocumentLineId { get; set; }
    public StockDocumentLine StockDocumentLine { get; set; } = default!;

    /// <summary>
    /// Nullable vì user có thể chọn dòng này không thuộc XML.
    /// </summary>
    public int? InputInvoiceDetailId { get; set; }
    public InputInvoiceDetail? InputInvoiceDetail { get; set; }

    /// <summary>
    /// true: dòng này thuộc hóa đơn XML.
    /// false: dòng này không thuộc hóa đơn XML.
    /// </summary>
    public bool UseInputInvoice { get; set; } = false;

    public InputInvoiceMatchStatus MatchStatus { get; set; } = InputInvoiceMatchStatus.None;

    [Column(TypeName = "decimal(18,3)")]
    public decimal QuantityDifference { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountDifference { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}