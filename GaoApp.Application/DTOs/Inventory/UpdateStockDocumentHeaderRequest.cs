public sealed class UpdateStockDocumentHeaderRequest
{
    public int StockDocumentId { get; set; }
    public string? RowVersion { get; set; }

    public int? LegalEntityId { get; set; }
    public int? WarehouseId { get; set; }
    public int? SupplierId { get; set; }

    public DateTime? DocumentDate { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Chỉ dùng nếu flow UI có cho nhập ghi chú duyệt.
    /// Nếu nghiệp vụ duyệt tách endpoint riêng thì field này nên tách ra.
    /// </summary>
    public string? ApprovalNote { get; set; }
}
