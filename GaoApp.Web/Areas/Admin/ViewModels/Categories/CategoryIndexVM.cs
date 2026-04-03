using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Categories;

namespace GaoApp.Web.Areas.Admin.ViewModels.Categories;

public class CategoryIndexVM
{
    public string SearchString { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<CategoryListItemDto> Paged { get; set; } = new();
}
