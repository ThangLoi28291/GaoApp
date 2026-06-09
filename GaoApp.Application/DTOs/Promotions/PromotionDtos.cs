using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Promotions;

public sealed class PromotionListItemDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public PromotionType Type { get; set; }

    public string TypeText { get; set; } = "";

    public PromotionDiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; }

    public decimal? ComboFixedPrice { get; set; }

    public decimal? BuyQuantity { get; set; }

    public decimal? GetQuantity { get; set; }

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    public bool IsActive { get; set; }

    public int Priority { get; set; }

    public string? CustomerPriceTier { get; set; }

    public int ItemCount { get; set; }

    public int ComboRuleCount { get; set; }

    public string StatusText { get; set; } = "";
}

public sealed class PromotionEditDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public PromotionType Type { get; set; } = PromotionType.ProductDiscount;

    public PromotionDiscountType DiscountType { get; set; } = PromotionDiscountType.Percentage;

    public decimal DiscountValue { get; set; }

    public decimal? ComboFixedPrice { get; set; }

    public string? ComboNote { get; set; }

    public decimal? BuyQuantity { get; set; }

    public decimal? GetQuantity { get; set; }

    public bool RequireGiftQuantityInCart { get; set; } = true;

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public int Priority { get; set; }

    public string? CustomerPriceTier { get; set; }

    public byte[]? RowVersion { get; set; }

    public List<PromotionItemEditDto> Items { get; set; } = new();

    public List<PromotionComboRuleEditDto> ComboRules { get; set; } = new();
}

public sealed class PromotionItemEditDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ProductUnitConversionId { get; set; }

    public decimal MinQuantity { get; set; } = 1m;

    public string? ProductName { get; set; }

    public string? VariantName { get; set; }

    public string? UnitName { get; set; }
}

public sealed class PromotionComboRuleEditDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ProductUnitConversionId { get; set; }

    public decimal RequiredQuantity { get; set; } = 1m;

    public string? ProductName { get; set; }

    public string? VariantName { get; set; }

    public string? UnitName { get; set; }
}

public sealed class SavePromotionRequest
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public PromotionType Type { get; set; }

    public PromotionDiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; }

    public decimal? ComboFixedPrice { get; set; }

    public string? ComboNote { get; set; }

    public decimal? BuyQuantity { get; set; }

    public decimal? GetQuantity { get; set; }

    public bool RequireGiftQuantityInCart { get; set; } = true;

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public int Priority { get; set; }

    public string? CustomerPriceTier { get; set; }

    public byte[]? RowVersion { get; set; }

    public List<PromotionItemEditDto> Items { get; set; } = new();

    public List<PromotionComboRuleEditDto> ComboRules { get; set; } = new();
}