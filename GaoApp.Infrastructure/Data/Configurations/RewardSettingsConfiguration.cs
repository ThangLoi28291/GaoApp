using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class RewardSettingsConfiguration
    : IEntityTypeConfiguration<RewardSettings>
{
    public void Configure(EntityTypeBuilder<RewardSettings> b)
    {
        b.Property(x => x.MoneyPerPoint)
            .HasColumnType("decimal(18,2)");

        b.Property(x => x.VoucherValue)
            .HasColumnType("decimal(18,2)");

        b.Property(x => x.IsEnabled)
            .HasDefaultValue(true);

        b.Property(x => x.Note)
            .HasMaxLength(500);

        // Mỗi store chỉ có 1 cấu hình
        b.HasIndex(x => x.StoreId)
            .IsUnique();

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}