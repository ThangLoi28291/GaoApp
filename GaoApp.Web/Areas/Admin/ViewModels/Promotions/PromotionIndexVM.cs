using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Promotions;

public sealed class PromotionIndexVM
{
    public PromotionType? Type { get; set; }

    public bool? IsActive { get; set; }

    public string? CustomerPriceTier { get; set; }

    public string? SearchString { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public PagedResult<PromotionListItemDto> Paged { get; set; } = new();
}