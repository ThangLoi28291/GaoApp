// GaoApp.Infrastructure/Persistence/Configurations/AdminMenuItemConfiguration.cs
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Persistence.Configurations;

public class AdminMenuItemConfiguration : IEntityTypeConfiguration<AdminMenuItem>
{
    public void Configure(EntityTypeBuilder<AdminMenuItem> builder)
    {
        builder.ToTable("AdminMenuItems");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Area).HasMaxLength(80);
        builder.Property(x => x.Controller).HasMaxLength(120);
        builder.Property(x => x.Action).HasMaxLength(120);
        builder.Property(x => x.Url).HasMaxLength(300);
        builder.Property(x => x.Icon).HasMaxLength(120);
        builder.Property(x => x.PermissionCode).HasMaxLength(200);

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.ParentId, x.SortOrder });
    }
}