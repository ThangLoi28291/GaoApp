using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("LegalEntities", table =>
        {
            table.HasCheckConstraint(
                "CK_LegalEntities_SalePriority_Positive",
                "[SalePriority] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.LegalName)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.TaxCode)
            .HasMaxLength(50);

        builder.Property(x => x.Address)
            .HasMaxLength(1200);

        builder.Property(x => x.Phone)
            .HasMaxLength(30);

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.SalePriority)
            .IsRequired();

        builder.Property(x => x.IsDefaultForPurchase)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(x => new { x.StoreId, x.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => new { x.StoreId, x.TaxCode })
            .IsUnique()
            .HasFilter("[TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0");

        builder.HasIndex(x => new { x.StoreId, x.SalePriority })
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");

        builder.HasIndex(x => x.StoreId)
            .IsUnique()
            .HasFilter("[IsDefaultForPurchase] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

        builder.HasIndex(x => new { x.StoreId, x.DefaultWarehouseId })
            .IsUnique()
            .HasFilter("[DefaultWarehouseId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(x => new { x.StoreId, x.IsActive, x.IsDeleted });

        builder.HasOne(x => x.Store)
            .WithMany(x => x.LegalEntities)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);

        // Composite FK khóa cứng việc tham chiếu kho khác Store.
        builder.HasOne(x => x.DefaultWarehouse)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.DefaultWarehouseId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK khóa cứng việc tham chiếu cấu hình hóa đơn khác Store.
        builder.HasOne(x => x.InvoiceProviderSetting)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.InvoiceProviderSettingId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
