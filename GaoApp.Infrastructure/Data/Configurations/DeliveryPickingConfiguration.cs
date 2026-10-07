using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class DeliveryPickingConfiguration : IEntityTypeConfiguration<DeliveryPickingWork>, IEntityTypeConfiguration<DeliveryPickingLine>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table) where T : BaseStoreEntity
    {
        b.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted]=0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryOrder>().WithMany().HasForeignKey("StoreId", "DeliveryOrderId")
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryPickingWork> b)
    {
        Base(b, "DeliveryPickingWorks");
        b.HasAlternateKey(x => new { x.StoreId, x.DeliveryOrderId });
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.PickerUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ApprovedByUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.CustomerConfirmationNote).HasMaxLength(1000);
        b.Property(x => x.ApprovedTotal).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryPickingWorks_Approval",
            "([ApprovedRevision] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedTotal] IS NULL) OR ([ApprovedRevision] IS NOT NULL AND [ApprovedRevision]>0 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedTotal] IS NOT NULL AND [ApprovedTotal]>0 AND [ApprovedTotal]=ROUND([ApprovedTotal],0))"));
    }
    public void Configure(EntityTypeBuilder<DeliveryPickingLine> b)
    {
        Base(b, "DeliveryPickingLines");
        b.HasOne<DeliveryPickingWork>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Quote).WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ReporterUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId }).IsUnique();
        b.Property(x => x.ReportFactKind).HasMaxLength(30).IsRequired();
        b.Property(x => x.ShortageReason).HasMaxLength(1000);
        foreach (var p in new[] { "ReportedQuantity", "ApprovedQuantity", "ApprovedOriginalCoverage" }) b.Property<decimal?>(p).HasPrecision(18, 4);
        // Required decimal properties keep their CLR nullability; configure them explicitly.
        b.Property(x => x.PlannedQuantity).IsRequired().HasPrecision(18, 4);
        b.Property(x => x.PlannedOriginalCoverage).IsRequired().HasPrecision(18, 4);
        b.Property(x => x.ApprovedNet).HasPrecision(18, 2);
        b.ToTable(t => {
            t.HasTrigger("TR_DeliveryPickingLines_Bounds");
            t.HasCheckConstraint("CK_DeliveryPickingLines_Quantities",
                "[PlannedQuantity]>=0 AND [PlannedOriginalCoverage]>=0 AND ([ReportedQuantity] IS NULL OR [ReportedQuantity]>=0 AND [ReportedQuantity]<=[PlannedQuantity]) AND ([ApprovedQuantity] IS NULL OR [ReportedQuantity] IS NOT NULL AND [ApprovedQuantity]>=0 AND [ApprovedQuantity]<=[ReportedQuantity]) AND ([ApprovedOriginalCoverage] IS NULL OR [ApprovedOriginalCoverage]>=0 AND [ApprovedOriginalCoverage]<=[PlannedOriginalCoverage])");
            t.HasCheckConstraint("CK_DeliveryPickingLines_Report",
                "([ReportedQuantity] IS NULL AND [ReporterUserId] IS NULL AND [ReportedAtUtc] IS NULL AND [ReportFactKind]='unreported') OR ([ReportedQuantity] IS NOT NULL AND [ReporterUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND ([ReportFactKind]='picker-report' OR [ReportFactKind]='plan-removal' AND [ReportedQuantity]=0) AND ([ReportedQuantity]>0 AND [ReportedQuantity]=[PlannedQuantity] OR NULLIF(LTRIM(RTRIM([ShortageReason])), '') IS NOT NULL))");
            t.HasCheckConstraint("CK_DeliveryPickingLines_Approval",
                "([ApprovedQuantity] IS NULL AND [ApprovedOriginalCoverage] IS NULL AND [ApprovedNet] IS NULL) OR ([ApprovedQuantity] IS NOT NULL AND [ApprovedOriginalCoverage] IS NOT NULL AND [ApprovedNet] IS NOT NULL AND [ApprovedNet]>=0 AND [ApprovedNet]=ROUND([ApprovedNet],0) AND ([ApprovedQuantity]>0 OR [ApprovedOriginalCoverage]=0 AND [ApprovedNet]=0))");
            t.HasCheckConstraint("CK_DeliveryPickingLines_Inactive", "[IsActive]=1 OR [PlannedQuantity]=0 AND [PlannedOriginalCoverage]=0");
        });
    }
}
