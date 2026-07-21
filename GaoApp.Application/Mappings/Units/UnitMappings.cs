using GaoApp.Application.DTOs.Units;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Units;

public static class UnitMappings
{
    public static UnitListItemDto ToListItemDto(this Unit source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UnitListItemDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.IsActive,
            IsBase = source.IsBase,
            SortOrder = source.SortOrder
        };
    }

    public static UnitEditDto ToEditDto(this Unit source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UnitEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.IsActive,
            IsBase = source.IsBase,
            SortOrder = source.SortOrder,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static UnitEditDto ToEditDto(this CreateUnitRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UnitEditDto
        {
            Code = source.Code ?? string.Empty,
            Name = source.Name,
            Status = source.Status,
            IsBase = source.IsBase,
            SortOrder = source.SortOrder
        };
    }

    public static UnitEditDto ToEditDto(this UpdateUnitRequest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UnitEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Status = source.Status,
            IsBase = source.IsBase,
            SortOrder = source.SortOrder,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    public static Unit ToEntity(this UnitEditDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Unit
        {
            Code = source.Code,
            Name = source.Name,
            IsActive = source.Status,
            IsBase = source.IsBase,
            SortOrder = source.SortOrder
        };
    }
}
