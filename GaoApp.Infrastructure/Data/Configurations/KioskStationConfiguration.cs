using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace GaoApp.Infrastructure.Data.Configurations;
public sealed class KioskStationConfiguration : IEntityTypeConfiguration<KioskStation>
{
    public void Configure(EntityTypeBuilder<KioskStation> b)
    {
        b.ToTable("KioskStations");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.StoreId, x.TerminalId }).IsUnique();
        b.HasIndex(x => x.DeviceHash).IsUnique().HasFilter("[DeviceHash] IS NOT NULL");
        b.HasIndex(x => x.ActivationHash).IsUnique().HasFilter("[ActivationHash] IS NOT NULL");
        b.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SystemUser).WithMany().HasForeignKey(x => x.SystemUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
