using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Suppliers;

public static class SupplierMappings
{
    public static SupplierListItemDto ToListItemDto(this Supplier source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SupplierListItemDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Phone = source.Phone,
            Status = source.IsActive
        };
    }

    public static SupplierEditDto ToEditDto(this Supplier source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SupplierEditDto
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Phone = source.Phone,
            Email = source.Email,
            Address = source.Address,
            ContactName = source.ContactName,
            TaxCode = source.TaxCode,
            Note = source.Note,
            Status = source.IsActive,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }
}
