using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OperatingExpenseConfiguration : IEntityTypeConfiguration<OperatingExpense>
{
    public void Configure(EntityTypeBuilder<OperatingExpense> b)
    {
        b.ToTable("OperatingExpenses", t => {
            t.HasCheckConstraint("CK_OperatingExpenses_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_OperatingExpenses_Period", "[RecognitionTo] >= [RecognitionFrom]");
            t.HasCheckConstraint("CK_OperatingExpenses_Status", "[Status] IN ('draft','confirmed','voided')");
        });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Category).HasMaxLength(30).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.RecognitionFrom).HasColumnType("date");
        b.Property(x => x.RecognitionTo).HasColumnType("date");
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
        b.Property(x => x.ReceiptReference).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.VoidReason).HasMaxLength(500);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.RecognitionFrom, x.RecognitionTo });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
