using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class POSPaymentAdjustmentRequestConfiguration : IEntityTypeConfiguration<POSPaymentAdjustmentRequest>
{
    public void Configure(EntityTypeBuilder<POSPaymentAdjustmentRequest> b)
    {
        b.ToTable("POSPaymentAdjustmentRequests", t => t.HasCheckConstraint("CK_POSPaymentAdjustmentRequests_Target",
            "([DepositEntryId] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL) OR ([DepositEntryId] IS NULL AND [PaymentId] IS NOT NULL AND [OrderId] IS NOT NULL)"));
        b.HasOne(x => x.DepositEntry).WithMany().HasForeignKey(x => x.DepositEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.POSShift).WithMany().HasForeignKey(x => x.POSShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        // One pending change per sale: a mixed-tender change can affect allocation of its other payments.
        b.HasIndex(x => new { x.StoreId, x.OrderId }).IsUnique().HasFilter("[OrderId] IS NOT NULL AND [Status] = 0 AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.StoreId, x.DepositEntryId }).IsUnique().HasFilter("[DepositEntryId] IS NOT NULL AND [Status] = 0 AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.StoreId, x.Status, x.Id });
        b.HasIndex(x => new { x.StoreId, x.RequestedByUserId, x.Id });
        b.HasIndex(x => new { x.StoreId, x.POSShiftId, x.Id });
    }
}
