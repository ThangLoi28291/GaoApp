using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontBankSourceGuardConfiguration : IEntityTypeConfiguration<StoreBankAccount>,IEntityTypeConfiguration<StoreAcbSettings>,
    IEntityTypeConfiguration<AcbQrSession>,IEntityTypeConfiguration<AcbCallbackRoute>,IEntityTypeConfiguration<OrderPayment>,
    IEntityTypeConfiguration<StorefrontOrder>,IEntityTypeConfiguration<StorefrontPaymentAllocation>,IEntityTypeConfiguration<StorefrontBankReceipt>,
    IEntityTypeConfiguration<StorefrontPaymentIntent>,IEntityTypeConfiguration<BankNotificationDevice>
{
    public void Configure(EntityTypeBuilder<StoreBankAccount> b)=>b.ToTable("StoreBankAccounts",t=>t.HasTrigger("TR_StoreBankAccounts_StorefrontBinding"));
    public void Configure(EntityTypeBuilder<StoreAcbSettings> b)=>b.ToTable("StoreAcbSettings",t=>t.HasTrigger("TR_StoreAcbSettings_StorefrontBinding"));
    public void Configure(EntityTypeBuilder<AcbQrSession> b)=>b.ToTable("AcbQrSessions",t=>t.HasTrigger("TR_AcbQrSessions_BankBinding"));
    public void Configure(EntityTypeBuilder<AcbCallbackRoute> b)=>b.ToTable("AcbCallbackRoutes",t=>t.HasTrigger("TR_AcbCallbackRoutes_StorefrontBinding"));
    public void Configure(EntityTypeBuilder<OrderPayment> b)=>b.ToTable("OrderPayments",t=>t.HasTrigger("TR_OrderPayments_BankSource"));
    public void Configure(EntityTypeBuilder<StorefrontOrder> b)=>b.ToTable("StorefrontOrders",t=>t.HasTrigger("TR_StorefrontOrders_QrPayment"));
    public void Configure(EntityTypeBuilder<StorefrontPaymentAllocation> b)=>b.ToTable("StorefrontPaymentAllocations",t=>t.HasTrigger("TR_StorefrontPaymentAllocations_BankSource"));
    public void Configure(EntityTypeBuilder<StorefrontBankReceipt> b)=>b.ToTable("StorefrontBankReceipts",t=>t.HasTrigger("TR_StorefrontBankReceipts_GlobalSource"));
    public void Configure(EntityTypeBuilder<StorefrontPaymentIntent> b)=>b.ToTable("StorefrontPaymentIntents",t=>t.HasTrigger("TR_StorefrontPaymentIntents_BankBinding"));
    public void Configure(EntityTypeBuilder<BankNotificationDevice> b)=>b.ToTable("BankNotificationDevices",t=>t.HasTrigger("TR_BankNotificationDevices_StorefrontIdentity"));
}
