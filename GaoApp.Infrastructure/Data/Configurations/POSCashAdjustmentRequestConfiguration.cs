using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class POSCashAdjustmentRequestConfiguration : IEntityTypeConfiguration<POSCashAdjustmentRequest>
{
    public void Configure(EntityTypeBuilder<POSCashAdjustmentRequest> b)
    {
        b.ToTable("POSCashAdjustmentRequests");
        b.HasOne(x => x.Transaction).WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.POSShift).WithMany().HasForeignKey(x => x.POSShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.TransactionId }).IsUnique().HasFilter("[Status] = 0 AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.StoreId, x.Status, x.Id });
        b.HasIndex(x => new { x.StoreId, x.RequestedByUserId, x.Id });
    }
}
