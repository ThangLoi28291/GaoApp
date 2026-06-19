using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceCorrectionCaseConfiguration : IEntityTypeConfiguration<InvoiceCorrectionCase>
{
    public void Configure(EntityTypeBuilder<InvoiceCorrectionCase> b)
    {
        b.ToTable("InvoiceCorrectionCases");

        b.HasKey(x => x.Id);

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.OriginalInvoiceHeadId)
            .IsRequired();

        b.Property(x => x.Type)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<byte>()
            .IsRequired()
            .HasDefaultValue(InvoiceCorrectionStatus.Draft);

        b.Property(x => x.Reason)
            .HasMaxLength(255)
            .IsRequired();

        b.Property(x => x.AgreementDocumentNo)
            .HasMaxLength(255)
            .IsRequired();

        b.Property(x => x.Note)
            .HasMaxLength(1000);

        b.Property(x => x.LastErrorCode)
            .HasMaxLength(100);

        b.Property(x => x.LastErrorMessage)
            .HasMaxLength(1000);

        b.Property(x => x.CreatedAtUtc)
            .IsRequired();

        b.Property(x => x.AgreementDateUtc)
            .IsRequired();

        // Hồ sơ xử lý sai sót trỏ về hóa đơn gốc.
        b.HasOne(x => x.OriginalInvoiceHead)
            .WithMany()
            .HasForeignKey(x => x.OriginalInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hồ sơ xử lý sai sót trỏ tới hóa đơn mới được tạo ra.
        b.HasOne(x => x.NewInvoiceHead)
            .WithMany()
            .HasForeignKey(x => x.NewInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.OriginalInvoiceHeadId, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.NewInvoiceHeadId, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.Type, x.Status, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.CreatedAtUtc, x.IsDeleted });
    }
}