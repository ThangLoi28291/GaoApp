using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class OrderInventoryIssueActionConfiguration : IEntityTypeConfiguration<OrderInventoryIssueAction>
{
    public void Configure(EntityTypeBuilder<OrderInventoryIssueAction> builder)
    {
        builder.ToTable("OrderInventoryIssueActions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActionType)
            .IsRequired();

        builder.Property(x => x.ActionAtUtc)
            .IsRequired();

        builder.Property(x => x.ReferenceType)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(2000);

        builder.HasOne(x => x.OrderInventoryIssue)
            .WithMany(x => x.Actions)
            .HasForeignKey(x => x.OrderInventoryIssueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ActorUser)
            .WithMany(x => x.InventoryIssueActions)
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrderInventoryIssueId, x.ActionAtUtc, x.IsDeleted });

        builder.HasIndex(x => new { x.StoreId, x.ActionType, x.ActionAtUtc, x.IsDeleted });

        builder.HasIndex(x => new { x.StoreId, x.ReferenceType, x.ReferenceId, x.IsDeleted });
    }
}