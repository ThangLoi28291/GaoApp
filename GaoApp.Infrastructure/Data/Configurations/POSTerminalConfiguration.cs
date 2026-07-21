using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSTerminalConfiguration : IEntityTypeConfiguration<POSTerminal>
{
    public void Configure(EntityTypeBuilder<POSTerminal> builder)
    {
        builder.ToTable("POSTerminals");

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.LocalIp)
            .HasMaxLength(100);

        builder.Property(x => x.DeviceName)
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .HasMaxLength(300);

        /// <summary>
        /// 1 store không được có 2 terminal trùng code.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.Code })
            .IsUnique();

        // Giữ lại IP cũ để tương thích dữ liệu cũ.
        // Chỉ unique khi LocalIp có giá trị, tránh lỗi nhiều terminal có LocalIp = null.
        builder.HasIndex(x => new { x.StoreId, x.LocalIp })
            .IsUnique()
            .HasFilter("[LocalIp] IS NOT NULL");

        builder.HasMany(x => x.Shifts)
            .WithOne(x => x.Terminal)
            .HasForeignKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}