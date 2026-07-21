using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Web.Areas.Admin.ViewModels.Products;

public sealed class BarcodeHistoryIndexVm
{
    public BarcodeHistoryQueryRequest Filter { get; set; } = new();

    public PagedResult<BarcodeHistoryRowDto> Result { get; set; } = new();
}