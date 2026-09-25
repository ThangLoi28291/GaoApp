using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PosOperationReceiptConfiguration : IEntityTypeConfiguration<PosOperationReceipt>
{
    public void Configure(EntityTypeBuilder<PosOperationReceipt> b)
    {
        b.ToTable("PosOperationReceipts");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResponseJson).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.OperationId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.TerminalId, x.CreatedAtUtc });
    }
}
