using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.AttributeValues;

public static class AttributeValueMappings
{
    public static AttributeValueListItemDto ToListItemDto(this AttributeValue source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AttributeValueListItemDto
        {
            Id = source.Id,
            AttributeId = source.AttributeId,
            AttributeName = source.Attribute?.Name ?? string.Empty,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            CreatedBy = source.CreatedBy,
            UpdatedBy = source.UpdatedBy
        };
    }

    public static AttributeValueEditDto ToEditDto(this AttributeValue source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AttributeValueEditDto
        {
            Id = source.Id,
            AttributeId = source.AttributeId,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            CreatedBy = source.CreatedBy,
            UpdatedBy = source.UpdatedBy,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static AttributeValueEditDto ToEditDto(this CreateAttributeValueRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AttributeValueEditDto
        {
            AttributeId = source.AttributeId,
            Code = source.Code ?? string.Empty,
            Name = source.Name,
            Status = source.Status
        };
    }

    public static AttributeValueEditDto ToEditDto(this UpdateAttributeValueRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AttributeValueEditDto
        {
            Id = source.Id,
            AttributeId = source.AttributeId,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static AttributeValue ToEntity(this AttributeValueEditDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AttributeValue
        {
            AttributeId = source.AttributeId,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc ?? default,
            UpdatedAtUtc = source.UpdatedAtUtc,
            CreatedBy = source.CreatedBy,
            UpdatedBy = source.UpdatedBy
        };
    }
}
