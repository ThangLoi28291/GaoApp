using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Categories;

namespace GaoApp.Web.Areas.Admin.ViewModels.Categories;

public class CategoryIndexVM
{
    public string SearchString { get; set; } = "";
    public bool? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalCategoryCount { get; set; }
    public int ActiveCategoryCount { get; set; }
    public int InactiveCategoryCount { get; set; }

    public PagedResult<CategoryListItemDto> Paged { get; set; } = new();
}
