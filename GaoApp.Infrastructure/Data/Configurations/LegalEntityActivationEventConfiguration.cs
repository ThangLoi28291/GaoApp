using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class LegalEntityActivationEventConfiguration
    : IEntityTypeConfiguration<LegalEntityActivationEvent>
{
    public void Configure(EntityTypeBuilder<LegalEntityActivationEvent> builder)
    {
        builder.ToTable("LegalEntityActivationEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.Action).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ChangedByUserName).HasMaxLength(200);
        builder.Property(x => x.PreflightSnapshotJson).HasColumnType("nvarchar(max)");

        builder.HasOne(x => x.Store)
            .WithMany(x => x.LegalEntityActivationEvents)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.OccurredAtUtc, x.IsDeleted });
    }
}
