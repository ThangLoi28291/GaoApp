using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping User ↔ Store.
/// </summary>
public class UserInStoreConfiguration : IEntityTypeConfiguration<UserInStore>
{
    public void Configure(EntityTypeBuilder<UserInStore> builder)
    {
        builder.ToTable("UserInStores");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IsActive)
            .IsRequired();

        // 1 user chỉ có 1 role trong 1 store
        builder.HasIndex(x => new { x.StoreId, x.UserId })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.RoleId });

        builder.HasIndex(x => new { x.StoreId, x.IsActive });

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserInStores)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Role)
            .WithMany(x => x.UserInStores)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}