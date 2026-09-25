using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Map giữa phiếu nhập kho và hóa đơn XML đầu vào.
/// Mỗi phiếu chỉ có tối đa một map hoạt động; một hóa đơn vẫn có thể phục vụ
/// nhiều phiếu đủ điều kiện.
/// </summary>
[Table("StockDocumentInputInvoiceMap")]
public class StockDocumentInputInvoiceMap : BaseStoreEntity, IAuditTrackedEntity
{
    public const string ActiveReceiptIndexName =
        "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active";

    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = default!;

    public int InputInvoiceHeadId { get; set; }
    public InputInvoiceHead InputInvoiceHead { get; set; } = default!;

    [StringLength(500)]
    public string? Note { get; set; }
}
