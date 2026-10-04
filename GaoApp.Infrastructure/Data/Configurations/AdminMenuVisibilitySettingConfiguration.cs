using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class AdminMenuVisibilitySettingConfiguration : IEntityTypeConfiguration<AdminMenuVisibilitySetting>
{
    public void Configure(EntityTypeBuilder<AdminMenuVisibilitySetting> builder)
    {
        builder.ToTable("AdminMenuVisibilitySettings", t => t.HasCheckConstraint("CK_AdminMenuVisibilitySettings_Target",
            "([RoleId] IS NOT NULL AND [UserInStoreId] IS NULL) OR ([RoleId] IS NULL AND [UserInStoreId] IS NOT NULL)"));
        builder.HasIndex(x => new { x.StoreId, x.RoleId }).IsUnique().HasFilter("[RoleId] IS NOT NULL");
        builder.HasIndex(x => new { x.StoreId, x.UserInStoreId }).IsUnique().HasFilter("[UserInStoreId] IS NOT NULL");
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserInStore>().WithMany().HasForeignKey(x => x.UserInStoreId).OnDelete(DeleteBehavior.Restrict);
    }
}
