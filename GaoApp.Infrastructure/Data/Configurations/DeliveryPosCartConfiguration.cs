using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

// EF SQL Server must avoid OUTPUT without INTO when these narrowly scoped triggers exist.
public sealed class DeliveryPosCartConfiguration : IEntityTypeConfiguration<Order>,
    IEntityTypeConfiguration<OrderLine>, IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<Order> b) => b.ToTable(t => t.HasTrigger("TR_Orders_DeliverySource"));
    public void Configure(EntityTypeBuilder<OrderLine> b) => b.ToTable(t => t.HasTrigger("TR_OrderLines_DeliverySource"));
    public void Configure(EntityTypeBuilder<OrderPayment> b) => b.ToTable(t => t.HasTrigger("TR_OrderPayments_DeliverySource"));
}
