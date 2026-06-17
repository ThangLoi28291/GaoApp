using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceProviderSettingConfiguration : IEntityTypeConfiguration<InvoiceProviderSetting>
{
    public void Configure(EntityTypeBuilder<InvoiceProviderSetting> b)
    {
        b.ToTable("InvoiceProviderSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.ProviderCode)
            .IsRequired()
            .HasMaxLength(50);

        b.Property(x => x.IsProduction)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.BaseUrl)
            .IsRequired()
            .HasMaxLength(500);

        b.Property(x => x.Username)
            .IsRequired()
            .HasMaxLength(150);

        b.Property(x => x.Password)
            .IsRequired()
            .HasMaxLength(500);

        b.Property(x => x.SupplierTaxCode)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(x => x.InvoiceType)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(x => x.TemplateCode)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(x => x.InvoiceSeries)
            .IsRequired()
            .HasMaxLength(25);

        b.Property(x => x.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        b.Property(x => x.ExchangeRate)
            .HasPrecision(18, 2)
            .HasDefaultValue(1m);

        b.Property(x => x.PaymentMethodName)
            .HasMaxLength(50);

        b.Property(x => x.CusGetInvoiceRight)
            .IsRequired()
            .HasDefaultValue(true);

        b.Property(x => x.DefaultPaymentStatus)
            .IsRequired()
            .HasDefaultValue(true);

        b.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        b.Property(x => x.Note)
            .HasMaxLength(500);
        b.Property(x => x.AuthMode)
    .HasConversion<byte>()
    .IsRequired()
    .HasDefaultValue(InvoiceProviderAuthMode.BasicAuth);

        // Một store có thể có nhiều cấu hình:
        // - nhiều MST
        // - nhiều mẫu
        // - nhiều ký hiệu
        // Nhưng cùng bộ Provider + MST + Template + Series thì không nên trùng.
        b.HasIndex(x => new
        {
            x.StoreId,
            x.ProviderCode,
            x.SupplierTaxCode,
            x.TemplateCode,
            x.InvoiceSeries
        })
        .IsUnique()
        .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.IsActive, x.IsDeleted });
    }
}