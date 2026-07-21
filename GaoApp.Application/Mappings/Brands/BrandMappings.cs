using GaoApp.Application.DTOs.Brands;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Brands;

public static class BrandMappings
{
    public static BrandListItemDto ToListItemDto(this Brand source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new BrandListItemDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.IsActive
        };
    }

    public static BrandEditDto ToEditDto(this Brand source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new BrandEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            Status = source.IsActive,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static BrandEditDto ToEditDto(this CreateBrandRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new BrandEditDto
        {
            Code = source.Code ?? string.Empty,
            Name = source.Name,
            Description = source.Description,
            Status = source.Status
        };
    }

    public static BrandEditDto ToEditDto(this UpdateBrandRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new BrandEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            Status = source.Status,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static Brand ToEntity(this BrandEditDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Brand
        {
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            IsActive = source.Status
        };
    }
}
