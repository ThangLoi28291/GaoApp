using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed partial class OrderRepository
{
    // A note/code resemblance is never a link. Only a posted settlement in this
    // store can replace a source row, and its real sale must still be accessible.
    private IQueryable<DeliverySaleListLink> DeliverySaleLinks(int storeId)
        => from journal in _db.DeliveryJournalEntries.AsNoTracking()
               .Where(j => j.StoreId == storeId && !j.IsDeleted && j.Kind == DeliveryJournalKind.Settlement)
           join delivery in _db.DeliveryOrders.Where(d => d.StoreId == storeId && !d.IsDeleted && d.State == DeliveryState.Settled)
               on journal.DeliveryOrderId equals delivery.Id
           join source in _db.Orders.Where(o => o.StoreId == storeId && !o.IsDeleted)
               on delivery.SourceCartId equals source.Id
           join sale in _db.Orders.Where(o => o.StoreId == storeId && !o.IsDeleted && o.CompletedAtUtc.HasValue &&
               (o.Status == OrderStatus.Completed || o.Status == OrderStatus.Refunded || o.Status == OrderStatus.Voided))
               on journal.SaleOrderId equals (int?)sale.Id
           select new DeliverySaleListLink
           {
               DeliveryOrderId = delivery.Id, Code = delivery.Code, State = delivery.State,
               SourceOrderId = source.Id, SourceOrderNumber = source.OrderNumber,
               SaleOrderId = sale.Id, SaleOrderNumber = sale.OrderNumber
           };

    private static OrderDeliveryInfoDto DeliverySaleInfo(DeliverySaleListLink link, int currentOrderId)
    {
        var info = DeliveryInfo(link.DeliveryOrderId, link.Code, link.State);
        info.SourceOrderId = link.SourceOrderId;
        info.SaleOrderId = link.SaleOrderId;
        info.SaleOrderNumber = link.SaleOrderNumber;
        info.IsSettlementSale = currentOrderId == link.SaleOrderId;
        return info;
    }

    private sealed class DeliverySaleListLink
    {
        public int DeliveryOrderId { get; set; }
        public string Code { get; set; } = "";
        public DeliveryState State { get; set; }
        public int SourceOrderId { get; set; }
        public string? SourceOrderNumber { get; set; }
        public int SaleOrderId { get; set; }
        public string? SaleOrderNumber { get; set; }
    }
}
