using GaoApp.Application.Common;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Domain.Entities;

namespace GaoApp.Web.Areas.Admin.ViewModels.AttributeValues;

public sealed class AttributeValueIndexVM
{
    public string? SearchString { get; set; }
    public int? AttributeId { get; set; } // filter
    public int Page { get; set; }
    public int PageSize { get; set; }

    public bool? Status { get; set; }
    public PagedResult<AttributeValueListItemDto> Paged { get; set; } = new();

    public List<ProductAttribute> Attributes { get; set; } = new(); // dropdown
}
