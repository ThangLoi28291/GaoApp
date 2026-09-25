using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.TaxCode).HasMaxLength(50);
        var normalizedTaxCode = b.Property(x => x.NormalizedTaxCode)
            .HasMaxLength(50)
            .HasComputedColumnSql(
                "CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([TaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''))",
                stored: true);
        // SQL owns this value. Skipping DML read-back also keeps the current
        // model compatible with tests that deliberately stop at an older schema.
        normalizedTaxCode.ValueGeneratedNever();
        normalizedTaxCode.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        normalizedTaxCode.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        b.Property(x => x.BankAccountNumber).HasMaxLength(50);
        b.Property(x => x.BankAccountName).HasMaxLength(250);
        b.Property(x => x.BankName).HasMaxLength(250);
        b.Property(x => x.Note).HasMaxLength(500);

        // RowVersion đúng chuẩn byte[] => rowversion/timestamp
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        // Unique theo Store
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();
        b.HasIndex(x => new
            {
                x.StoreId,
                x.NormalizedTaxCode,
                x.IsDeleted,
                x.IsActive
            })
            .HasDatabaseName("IX_Suppliers_StoreId_NormalizedTaxCode_State");

        // Soft delete filter (nếu bạn đã làm global filter thì bỏ dòng này)
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
