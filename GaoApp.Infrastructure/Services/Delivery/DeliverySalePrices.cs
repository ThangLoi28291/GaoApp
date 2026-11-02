using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

internal sealed record DeliveryPriceChange(string Schema, int StoreId, int OrderId, int Revision,
    string Version, int ActorUserId, string ActorName, DateTime ChangedAtUtc, string Reason,
    Dictionary<int, decimal> Prices, Dictionary<int, decimal> PreviousPrices);

internal static class DeliverySalePrices
{
    internal const string Action = "sale-price-change";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static bool Editable(DeliveryState state) => state is DeliveryState.Created or DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover or DeliveryState.HandedOver or
        DeliveryState.Delivering or DeliveryState.AwaitingReconciliation or DeliveryState.ReadyToSettle or DeliveryState.CancellationPending;
    internal static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    internal static string Price(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string Money(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);

    internal static async Task<Dictionary<int, DeliveryPriceChange>> ReadManyAsync(AppDbContext db,
        IReadOnlyList<DeliveryOrder> orders, CancellationToken ct)
    {
        var ids = orders.Select(x => x.Id).ToArray();
        var revisions = await db.DeliveryRevisions.AsNoTracking().Where(x => x.StoreId == db.CurrentStoreId &&
            ids.Contains(x.DeliveryOrderId) && x.Action == Action && !db.DeliveryRevisions.Any(y =>
                y.StoreId == x.StoreId && y.DeliveryOrderId == x.DeliveryOrderId && y.Action == Action && y.Revision > x.Revision)).ToListAsync(ct);
        var result = new Dictionary<int, DeliveryPriceChange>();
        foreach (var revision in revisions)
        {
            try
            {
                using var json = JsonDocument.Parse(revision.SnapshotJson);
                var saved = json.RootElement.GetProperty("priceChange").Deserialize<DeliveryPriceChange>(Json)!;
                var order = orders.Single(x => x.Id == revision.DeliveryOrderId);
                if (Hash(revision.SnapshotJson) != revision.SnapshotHash || saved is null || saved.Schema != "delivery-sale-price-v1" ||
                    saved.StoreId != order.StoreId || saved.OrderId != order.Id || saved.Revision != revision.Revision ||
                    saved.Revision > order.Revision || saved.Version != revision.AggregateVersion || saved.ActorUserId != revision.ActorUserId ||
                    saved.Prices is null || saved.Prices.Count == 0 || saved.Prices.Count > order.Lines.Count ||
                    saved.Prices.Any(x => !order.Lines.Any(l => l.Id == x.Key) || x.Value <= 0 ||
                        x.Value >= DeliveryValues.MoneyLimit || decimal.Round(x.Value, 2) != x.Value)) throw new JsonException();
                result.Add(order.Id, saved);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            { throw new DeliveryFoundationException(409, "PRICE_HISTORY_INVALID", "Lịch sử điều chỉnh giá không khớp; cần đối chiếu trước khi chốt."); }
        }
        return result;
    }
    internal static async Task<DeliveryPriceChange?> ReadAsync(AppDbContext db, DeliveryOrder order, CancellationToken ct)
        => (await ReadManyAsync(db, [order], ct)).GetValueOrDefault(order.Id);

    internal static (decimal Gross, decimal Net) Calculate(DeliveryOrderLine quote, decimal quantity,
        decimal originalNet, IReadOnlyDictionary<int, decimal> prices)
    {
        var gross = DeliveryValues.RoundMoney(quote.Gross * quantity / quote.OrderedQuantity);
        if (!prices.TryGetValue(quote.Id, out var price)) return (gross, originalNet);
        var discount = gross - originalNet;
        var updated = DeliveryValues.PricedAmount(quantity, price);
        DeliveryValues.Require(discount >= 0 && updated >= discount, "PRICE_BELOW_DISCOUNT",
            $"{quote.ItemName}: giá mới thấp hơn giảm giá đã duyệt. Hãy chọn giá đủ bù khoản giảm đang giữ.");
        return (updated, updated - discount);
    }

    internal static DeliveryPickingDetailDto Overlay(DeliveryPickingDetailDto detail, DeliveryOrder order,
        DeliveryPriceChange? change, bool canEditSalePrice)
    {
        var prices = change?.Prices ?? [];
        var lines = detail.Lines.Select(line =>
        {
            var quote = order.Lines.Single(x => x.Id == line.LineId);
            var draft = Calculate(quote, Number(line.PlannedQuantityText), Number(line.DraftNetText), prices);
            decimal? approved = line.ApprovedQuantityText is not null && line.ApprovedNetText is not null
                ? Calculate(quote, Number(line.ApprovedQuantityText), Number(line.ApprovedNetText), prices).Net : null;
            return line with { UnitPriceText = prices.TryGetValue(line.LineId, out var price) ? Price(price) : line.UnitPriceText,
                DraftNetText = Money(draft.Net), ApprovedNetText = approved.HasValue ? Money(approved.Value) : null };
        }).ToArray();
        var total = detail.ApprovedTotalText is null ? lines.Where(x => x.IsActive).Sum(x => Number(x.DraftNetText))
            : lines.Where(x => x.IsActive && x.ApprovedNetText is not null).Sum(x => Number(x.ApprovedNetText!));
        DeliveryValues.Money(total, "Tổng giá bán");
        var editable = canEditSalePrice && Editable(order.State);
        var rows = lines.Where(x => x.IsActive && Number(x.ApprovedQuantityText ?? x.PlannedQuantityText) > 0)
            .Select(line =>
            {
                var quote = order.Lines.Single(q => q.Id == line.LineId);
                var original = detail.Lines.Single(x => x.LineId == line.LineId);
                var quantity = line.ApprovedQuantityText ?? line.PlannedQuantityText;
                var amounts = Calculate(quote, Number(quantity), Number(original.ApprovedNetText ?? original.DraftNetText), prices);
                return new DeliverySalePriceLineDto(line.LineId, line.ItemName, line.UnitName, quantity,
                    line.UnitPriceText, Money(amounts.Gross - amounts.Net), line.ApprovedNetText ?? line.DraftNetText, editable && quote.UnitPrice > 0);
            }).ToArray();
        return detail with { Lines = lines,
            DraftTotalText = Money(lines.Where(x => x.IsActive).Sum(x => Number(x.DraftNetText))),
            ApprovedTotalText = detail.ApprovedTotalText is null ? null : Money(total),
            SalePrices = new(editable, Money(total), rows, change?.ChangedAtUtc, change?.ActorName, change?.Reason) };
    }
}
