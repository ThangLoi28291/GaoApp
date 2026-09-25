using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Units;

namespace GaoApp.Web.Areas.Admin.ViewModels.Units;

public sealed class UnitIndexVM
{
    public string? SearchString { get; set; } = "";

    public bool? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalUnitCount { get; set; }

    public int ActiveUnitCount { get; set; }

    public int InactiveUnitCount { get; set; }

    public PagedResult<UnitListItemDto> Paged { get; set; } = new();
}