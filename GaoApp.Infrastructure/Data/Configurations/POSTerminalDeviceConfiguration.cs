using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSTerminalDeviceConfiguration : IEntityTypeConfiguration<POSTerminalDevice>
{
    public void Configure(EntityTypeBuilder<POSTerminalDevice> builder)
    {
        builder.ToTable("POSTerminalDevices");

        builder.Property(x => x.DeviceKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.DeviceName)
            .HasMaxLength(150);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(500);

        builder.Property(x => x.LastIp)
            .HasMaxLength(100);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Một DeviceKey chỉ được tồn tại 1 lần trong 1 store.
        builder.HasIndex(x => new { x.StoreId, x.DeviceKey })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.TerminalId, x.IsActive });

        builder.HasOne(x => x.Terminal)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}