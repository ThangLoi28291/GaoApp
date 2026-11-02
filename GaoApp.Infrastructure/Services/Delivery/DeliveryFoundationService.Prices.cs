using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed partial class DeliveryFoundationService
{
    public async Task<DeliverySalePriceAck> ChangeSalePricesAsync(int id, DeliverySalePriceRequest request, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.EditSalePrice, ct);
        RequireKey(request.ClientRequestId); var version = Version(request.ExpectedVersion);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000 || reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw Error(400, "PRICE_REASON_REQUIRED", "Nhập lý do sửa giá, tối đa 1000 ký tự.");
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > 500 ||
            request.Lines.Any(x => x is null || x.LineId <= 0) || request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count)
            throw Error(400, "LINE_INVALID", "Chọn dòng hàng cần đổi giá, không trùng.");
        var input = new Dictionary<int, decimal>();
        foreach (var line in request.Lines)
        {
            if (line.UnitPriceText is null || !System.Text.RegularExpressions.Regex.IsMatch(line.UnitPriceText, @"\A(?:0|[1-9]\d{0,15})(?:\.\d{1,2})?\z") ||
                !decimal.TryParse(line.UnitPriceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) ||
                price <= 0 || price >= DeliveryValues.MoneyLimit) throw Error(400, "PRICE_INVALID", "Giá bán phải dương, tối đa 2 số lẻ và không có dấu phân cách hàng nghìn.");
            input.Add(line.LineId, price);
        }
        var hash = Hash(JsonSerializer.Serialize(new { operation = DeliverySalePrices.Action, store = Store, actor = Actor, id,
            version, reason, lines = input.OrderBy(x => x.Key) }, Json));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + request.ClientRequestId, ct);
            await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + id, ct);
            await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.EditSalePrice, ct);
            var order = await Load(id, true, ct);
            var receipt = await db.DeliveryCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == request.ClientRequestId, ct);
            if (receipt is not null)
            {
                if (receipt.DeliveryOrderId != id || receipt.ActorUserId != Actor || receipt.Operation != DeliverySalePrices.Action || receipt.RequestHash != hash)
                    throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung khác.");
                var replay = JsonSerializer.Deserialize<DeliverySalePriceAck>(receipt.OutcomeJson, Json) ?? throw Error(409, "RECEIPT_INVALID", "Thiếu kết quả điều chỉnh giá.");
                await tx.CommitAsync(ct); return replay with { Replayed = true };
            }
            if (Convert.ToBase64String(order.RowVersion) != version) throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi sửa giá.");
            if (!DeliverySalePrices.Editable(order.State) || await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Settlement, ct))
                throw Error(409, "PRICE_CHANGE_LOCKED", "Chỉ sửa giá khi đơn chưa chốt hàng, tiền và chưa hủy.");
            var picked = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).ToListAsync(ct);
            var basis = order.Lines.Where(quote => picked.SingleOrDefault(x => x.DeliveryOrderLineId == quote.Id)?.IsActive ?? true)
                .Select(quote =>
                {
                    var line = picked.SingleOrDefault(x => x.DeliveryOrderLineId == quote.Id);
                    var quantity = line?.ApprovedQuantity ?? line?.PlannedQuantity ?? quote.OrderedQuantity;
                    var net = line?.ApprovedNet ?? DeliveryPickingPolicy.Prorate(quote.Net, quote.OrderedQuantity, quantity);
                    return (Quote: quote, Quantity: quantity, Net: net);
                }).Where(x => x.Quantity > 0).ToArray();
            var previous = await DeliverySalePrices.ReadAsync(db, order, ct);
            var prices = new Dictionary<int, decimal>(previous?.Prices ?? []);
            var before = input.Keys.ToDictionary(key => key, key => prices.GetValueOrDefault(key, order.Lines.SingleOrDefault(x => x.Id == key)?.UnitPrice ?? 0));
            foreach (var pair in input)
            {
                var quote = order.Lines.SingleOrDefault(x => x.Id == pair.Key);
                if (quote is null || quote.UnitPrice <= 0 || !basis.Any(x => x.Quote.Id == pair.Key))
                    throw Error(400, "LINE_INVALID", "Chỉ đổi giá dòng hàng đang giao; hàng tặng giá 0 được giữ nguyên.");
                prices[pair.Key] = pair.Value;
            }
            if (input.All(x => before[x.Key] == x.Value)) throw Error(400, "PRICE_UNCHANGED", "Giá bán chưa thay đổi.");
            decimal total = 0;
            foreach (var line in basis)
                total += DeliverySalePrices.Calculate(line.Quote, line.Quantity, line.Net, prices).Net;
            DeliveryValues.Money(total, "Tổng giá bán");
            if (total <= 0) throw Error(400, "PRICE_TOTAL_INVALID", "Tổng giá bán cần lớn hơn 0.");
            order.Revision++; await db.SaveChangesAsync(ct);
            var now = DateTime.UtcNow;
            var actorName = await db.Users.Where(x => x.Id == Actor).Select(x => x.FullName ?? x.UserName).SingleAsync(ct);
            var change = new DeliveryPriceChange("delivery-sale-price-v1", Store, id, order.Revision,
                Convert.ToBase64String(order.RowVersion), Actor, actorName, now, reason, prices, before);
            var snapshot = JsonSerializer.SerializeToNode(Detail(order), Json)!.AsObject();
            snapshot["priceChange"] = JsonSerializer.SerializeToNode(change, Json);
            var text = snapshot.ToJsonString(Json);
            var ack = new DeliverySalePriceAck(request.ClientRequestId, id, DeliverySalePrices.Action, order.Revision, change.Version, false);
            db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = id, Revision = order.Revision, ActorUserId = Actor,
                Action = DeliverySalePrices.Action, AggregateVersion = change.Version, SnapshotJson = text, SnapshotHash = Hash(text) });
            db.DeliveryCommandReceipts.Add(new() { StoreId = Store, DeliveryOrderId = id, ClientRequestId = request.ClientRequestId,
                ActorUserId = Actor, Operation = DeliverySalePrices.Action, RequestHash = hash, OutcomeJson = JsonSerializer.Serialize(ack, Json) });
            var eventId = Guid.NewGuid();
            db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = id, Revision = order.Revision, EventId = eventId,
                Action = DeliverySalePrices.Action, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, deliveryId = id, order.Code,
                    order.Revision, version = change.Version, state = order.State.ToString(), action = DeliverySalePrices.Action, actorUserId = Actor, recordedAtUtc = now }, Json) });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return ack;
        }
        catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi sửa giá."); }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
}
