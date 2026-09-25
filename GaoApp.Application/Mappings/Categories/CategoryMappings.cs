using GaoApp.Application.DTOs.Categories;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Categories;

public static class CategoryMappings
{
    public static CategoryListItemDto ToListItemDto(this Category source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CategoryListItemDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            ParentName = source.Parent?.Name,
            SortOrder = source.SortOrder,
            IsActive = source.IsActive,
            CreatedAtUtc = source.CreatedAtUtc
        };
    }

    public static CategoryEditDto ToEditDto(this Category source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CategoryEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            ParentId = source.ParentId,
            SortOrder = source.SortOrder,
            IsActive = source.IsActive
        };
    }

    public static Category ToEntity(this CategoryEditDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Category
        {
            Id = source.Id,
            Code = source.Code ?? string.Empty,
            Name = source.Name,
            ParentId = source.ParentId,
            SortOrder = source.SortOrder,
            IsActive = source.IsActive
        };
    }
}
