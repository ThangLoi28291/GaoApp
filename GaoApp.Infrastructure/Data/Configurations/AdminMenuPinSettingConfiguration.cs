using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class AdminMenuPinSettingConfiguration : IEntityTypeConfiguration<AdminMenuPinSetting>
{
    public void Configure(EntityTypeBuilder<AdminMenuPinSetting> builder)
    {
        builder.ToTable("AdminMenuPinSettings");
        builder.Property(x => x.PinnedMenuIdsJson).HasMaxLength(64).IsRequired();
        // Keep the model, manual migration and SQL Server's generated unique
        // index definition identical. StoreId is required, so this covers every row.
        builder.HasIndex(x => x.StoreId).IsUnique().HasFilter("[StoreId] IS NOT NULL");
    }
}
