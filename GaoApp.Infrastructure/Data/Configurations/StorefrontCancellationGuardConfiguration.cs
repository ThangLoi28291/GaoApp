using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontCancellationGuardConfiguration : IEntityTypeConfiguration<StorefrontOrder>,
    IEntityTypeConfiguration<StorefrontRefund>, IEntityTypeConfiguration<InventoryTransaction>,
    IEntityTypeConfiguration<TreasuryEntry>, IEntityTypeConfiguration<POSShiftCashTransaction>
{
    public void Configure(EntityTypeBuilder<StorefrontOrder> b) => b.ToTable(t => t.HasTrigger("TR_StorefrontOrders_CancellationV7"));
    public void Configure(EntityTypeBuilder<StorefrontRefund> b) => b.ToTable(t => t.HasTrigger("TR_StorefrontRefunds_ObligationV7"));
    public void Configure(EntityTypeBuilder<InventoryTransaction> b) => b.ToTable(t => t.HasTrigger("TR_InventoryTransactions_CancellationV7"));
    public void Configure(EntityTypeBuilder<TreasuryEntry> b) => b.ToTable(t => t.HasTrigger("TR_TreasuryEntries_RefundClaimV7"));
    public void Configure(EntityTypeBuilder<POSShiftCashTransaction> b) => b.ToTable(t => t.HasTrigger("TR_POSShiftCashTransactions_RefundClaimV7"));
}
