using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public sealed record ReceiptSellingPriceDto(
    PurchaseReceiptSource ReceiptSource, string ProductName, string BaseUnitName,
    string ProductVersion, string VariantVersion, string LineVersion,
    List<ReceiptSellingPriceUnitDto> Units, List<ReceiptSellingPriceHistoryDto> History)
{
    public decimal ProductPrice { get; init; }
    public decimal BaseRetailPrice { get; init; }
    public int ProductPriceUnitId { get; init; }
    public string CatalogVersion { get; init; } = "";
}

public sealed record ReceiptRetailComparisonDto(int LineId, int VariantId, int UnitId, string UnitName, decimal? Price);

// Id 0 represents the variant's base selling price when it has no active conversions.
public sealed record ReceiptSellingPriceUnitDto(int Id, string UnitName, decimal Factor,
    decimal Price, string RowVersion)
{
    public decimal? WholesalePrice { get; init; }
    public bool IsBaseUnit { get; init; }
}

public sealed record ReceiptSellingPriceHistoryDto(DateTime AtUtc, string? UserName, string? Summary);

public sealed class UpdateReceiptSellingPricesRequest
{
    [Required] public string ProductVersion { get; set; } = "";
    [Required] public string VariantVersion { get; set; } = "";
    [Required] public string LineVersion { get; set; } = "";
    [Required] public string CatalogVersion { get; set; } = "";
    public int? ProductPriceUnitId { get; set; }
    public bool ReviewOnly { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999999")]
    public decimal EstimatedBaseCost { get; set; }
    [RegularExpression("^(margin|markup)$")] public string Basis { get; set; } = "margin";
    [Required, MaxLength(100)]
    public List<UpdateReceiptSellingPriceUnit> Units { get; set; } = new();
}

public sealed class UpdateReceiptSellingPriceUnit
{
    [Range(0, int.MaxValue)] public int Id { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    [Range(typeof(decimal), "0.01", "999999999999")]
    public decimal Price { get; set; }
    public bool UpdateWholesalePrice { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")]
    public decimal? WholesalePrice { get; set; }
    [Range(typeof(decimal), "0", "95")] public decimal TargetPercent { get; set; } = 10m;
    [Range(typeof(decimal), "0", "95")] public decimal WholesaleTargetPercent { get; set; } = 10m;
}

public sealed record ReceiptPriceReviewsDto(PurchaseReceiptSource ReceiptSource, List<ReceiptPriceReviewLineDto> Lines);
public sealed record ReceiptPriceReviewLineDto(int LineId, bool HasChanges, bool Reviewed,
    bool NeedsAttention, bool BelowCost, DateTime? ReviewedAtUtc, string? ReviewedBy,
    decimal? ReviewedBaseCost, decimal? CurrentBaseCost)
{
    public bool CatalogCurrent { get; init; }
}
