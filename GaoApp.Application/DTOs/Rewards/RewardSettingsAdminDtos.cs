namespace GaoApp.Application.DTOs.Rewards;

public sealed class RewardSettingsAdminDto
{
    public List<RewardCategoryOptionDto> Categories { get; init; } = new();
    public string? CategorySelectionVersion { get; init; }
    public bool Exists { get; init; }

    public int? Id { get; init; }

    public decimal MoneyPerPoint { get; init; }

    public int PointsPerVoucher { get; init; }

    public decimal VoucherValue { get; init; }

    public bool IsEnabled { get; init; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public decimal RequiredAmountPerVoucher
        => MoneyPerPoint > 0 && PointsPerVoucher > 0
            ? MoneyPerPoint * PointsPerVoucher
            : 0;

    public decimal EquivalentRewardRatePercent
        => RequiredAmountPerVoucher > 0
            ? VoucherValue / RequiredAmountPerVoucher * 100m
            : 0;
}

public sealed class SaveRewardSettingsRequest
{
    public bool UpdateCategoryExclusions { get; set; }
    public List<int> ExcludedCategoryIds { get; set; } = new();
    public string? CategorySelectionVersion { get; set; }
    public decimal MoneyPerPoint { get; set; }

    public int PointsPerVoucher { get; set; }

    public decimal VoucherValue { get; set; }

    public bool IsEnabled { get; set; }

    public string? Note { get; set; }

    public string? RowVersion { get; set; }

    public bool ConfirmRateChange { get; set; }
}

public sealed class RewardCategoryOptionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsExcluded { get; init; }
}
