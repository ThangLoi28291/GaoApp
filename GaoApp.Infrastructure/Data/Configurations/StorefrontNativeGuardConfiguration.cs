using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

// These annotations keep EF writes compatible with the actual SQL proof triggers.
public sealed class StorefrontNativeGuardConfiguration : IEntityTypeConfiguration<DeliveryOrder>, IEntityTypeConfiguration<DeliveryOrderLine>,
    IEntityTypeConfiguration<StorefrontOrder>, IEntityTypeConfiguration<OrderPayment>, IEntityTypeConfiguration<StorefrontSettlementLink>,
    IEntityTypeConfiguration<InvoiceHead>, IEntityTypeConfiguration<InvoiceDetail>, IEntityTypeConfiguration<POSShiftCashTransaction>,
    IEntityTypeConfiguration<TreasuryEntry>, IEntityTypeConfiguration<OperatingExpense>, IEntityTypeConfiguration<BankReceiptConsumption>,
    IEntityTypeConfiguration<StorefrontFulfillmentWork>, IEntityTypeConfiguration<StorefrontCustodyIncident>, IEntityTypeConfiguration<OrderLegalEntityAllocation>,
    IEntityTypeConfiguration<Order>, IEntityTypeConfiguration<SalesReturn>, IEntityTypeConfiguration<OrderLine>, IEntityTypeConfiguration<BankNotificationReceipt>
{
    public void Configure(EntityTypeBuilder<DeliveryOrder> b) => b.ToTable(t => t.HasTrigger("TR_DeliveryOrders_StorefrontSource"));
    public void Configure(EntityTypeBuilder<DeliveryOrderLine> b) => b.ToTable(t => t.HasTrigger("TR_DeliveryOrderLines_StorefrontSource"));
    public void Configure(EntityTypeBuilder<StorefrontOrder> b) => b.ToTable(t => { t.HasTrigger("TR_StorefrontOrders_NativeMode"); t.HasTrigger("TR_StorefrontOrders_FulfillmentProof"); });
    public void Configure(EntityTypeBuilder<OrderPayment> b) => b.ToTable(t => t.HasTrigger("TR_OrderPayments_StorefrontSource"));
    public void Configure(EntityTypeBuilder<StorefrontSettlementLink> b) => b.ToTable(t => t.HasTrigger("TR_StorefrontSettlementLinks_NativeSource"));
    public void Configure(EntityTypeBuilder<InvoiceHead> b) => b.ToTable(t => t.HasTrigger("TR_InvoiceHeads_StorefrontSource"));
    public void Configure(EntityTypeBuilder<InvoiceDetail> b) => b.ToTable(t => t.HasTrigger("TR_InvoiceDetails_StorefrontSource"));
    public void Configure(EntityTypeBuilder<POSShiftCashTransaction> b) => b.ToTable(t => t.HasTrigger("TR_POSShiftCashTransactions_StorefrontClaim"));
    public void Configure(EntityTypeBuilder<TreasuryEntry> b) => b.ToTable(t => t.HasTrigger("TR_TreasuryEntries_StorefrontClaim"));
    public void Configure(EntityTypeBuilder<OperatingExpense> b) => b.ToTable(t => t.HasTrigger("TR_OperatingExpenses_StorefrontClaim"));
    public void Configure(EntityTypeBuilder<BankReceiptConsumption> b) => b.ToTable(t => t.HasTrigger("TR_BankReceiptConsumptions_CodClaim"));
    public void Configure(EntityTypeBuilder<StorefrontFulfillmentWork> b) => b.ToTable(t => t.HasTrigger("TR_StorefrontFulfillmentWorks_Identity"));
    public void Configure(EntityTypeBuilder<StorefrontCustodyIncident> b) => b.ToTable(t => t.HasTrigger("TR_StorefrontCustodyIncidents_Identity"));
    public void Configure(EntityTypeBuilder<OrderLegalEntityAllocation> b) => b.ToTable(t => t.HasTrigger("TR_OrderLegalEntityAllocations_StorefrontSource"));
    public void Configure(EntityTypeBuilder<Order> b) => b.ToTable(t => t.HasTrigger("TR_Orders_StorefrontFinancialSource"));
    public void Configure(EntityTypeBuilder<SalesReturn> b) => b.ToTable(t => t.HasTrigger("TR_SalesReturns_StorefrontFinancialSource"));
    public void Configure(EntityTypeBuilder<OrderLine> b) => b.ToTable(t => t.HasTrigger("TR_OrderLines_StorefrontSnapshot"));
    public void Configure(EntityTypeBuilder<BankNotificationReceipt> b) => b.ToTable(t => t.HasTrigger("TR_BankNotificationReceipts_NativeSnapshot"));
}
