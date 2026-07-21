using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Map giữa phiếu nhập kho và hóa đơn XML đầu vào.
/// Cho phép 1 phiếu nhập gắn nhiều XML.
/// </summary>
[Table("StockDocumentInputInvoiceMap")]
public class StockDocumentInputInvoiceMap : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = default!;

    public int InputInvoiceHeadId { get; set; }
    public InputInvoiceHead InputInvoiceHead { get; set; } = default!;

    [StringLength(500)]
    public string? Note { get; set; }
}