using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class SaveReceiptPriceDraftRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MinLength(1), MaxLength(500)] public List<ReceiptPriceDraftLine> Lines { get; set; } = [];
}

public sealed class ReceiptPriceDraftLine
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int StockDocumentLineId { get; set; }
    [Range(typeof(decimal), "0.01", "99999999999999.99")] public decimal UnitPriceBeforeVat { get; set; }
}

public sealed record ReceiptPriceDraftResult(string RowVersion, DateTime SavedAtUtc,
    IReadOnlyDictionary<int, string> LineVersions, IReadOnlyDictionary<int, decimal> LineAmountsBeforeVat);
