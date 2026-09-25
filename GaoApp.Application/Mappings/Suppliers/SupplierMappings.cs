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
            ContactName = source.ContactName,
            Phone = source.Phone,
            Email = source.Email,
            TaxCode = source.TaxCode,
            BankName = source.BankName,
            MaskedBankAccountNumber = MaskBankAccountNumber(source.BankAccountNumber),
            Status = source.IsActive,
            CreatedAtUtc = source.CreatedAtUtc
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
            BankAccountNumber = source.BankAccountNumber,
            BankAccountName = source.BankAccountName,
            BankName = source.BankName,
            Note = source.Note,
            Status = source.IsActive,
            RowVersion = source.RowVersion?.ToArray() ?? Array.Empty<byte>()
        };
    }

    private static string? MaskBankAccountNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var account = value.Trim();
        return account.Length <= 4 ? "****" : "**** " + account[^4..];
    }

}
