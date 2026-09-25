using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.ProductAttributes;

public static class ProductAttributeMappings
{
    public static ProductAttributeListItemDto ToListItemDto(this ProductAttribute source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductAttributeListItemDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc
        };
    }

    public static ProductAttributeEditDto ToEditDto(this ProductAttribute source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductAttributeEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static ProductAttributeEditDto ToEditDto(this CreateProductAttributeRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductAttributeEditDto
        {
            Code = source.Code ?? string.Empty,
            Name = source.Name,
            Status = source.Status
        };
    }

    public static ProductAttributeEditDto ToEditDto(this UpdateProductAttributeRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductAttributeEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static ProductAttribute ToEntity(this ProductAttributeEditDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductAttribute
        {
            Code = source.Code,
            Name = source.Name,
            Status = source.Status
        };
    }
}
