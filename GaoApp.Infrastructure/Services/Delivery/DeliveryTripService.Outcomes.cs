using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed partial class DeliveryTripService
{
    private sealed record OutcomeSource(int LineId, int VariantId, int DispatchJournalEntryId,
        decimal Dispatched, decimal AcceptedReturn, decimal CurrentCustody);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record OutcomeCommandEnvelope(int OrderId, DeliveryOutcomeRequest Request);

    private DeliveryCapability ReportCapability(int courier)
        => DeliveryCapability.View | (Actor == courier ? DeliveryCapability.ReportOwnOutcome : DeliveryCapability.ReportOnBehalf);
    private static bool Reportable(DeliveryState state)
        => state is DeliveryState.Delivering or DeliveryState.AwaitingReconciliation or DeliveryState.CancellationPending;
    private static DeliveryReportSource ReportSource(string source)
    {
        if (!Enum.TryParse<DeliveryReportSource>(source, false, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != source)
            throw Error(400, "REPORT_SOURCE_INVALID", "Nguồn báo cáo không hợp lệ.");
        return parsed;
    }
    private DeliveryActor ReportActor(DeliveryOrder order, int courier)
        => new(Store, Actor, ReportCapability(courier), new HashSet<int> { order.SourceWarehouseId },
            new HashSet<int> { order.SourceLegalEntityId });
    private static DeliveryOrigin Origin(DeliveryOrder order)
        => new(order.StoreId, order.SourceWarehouseId, order.SourceLegalEntityId, order.CreatedByUserId,
            order.CreatedTerminalId, order.CreatedShiftId, order.SourceCartId, Utc(order.CreatedAtUtc), order.CustomerId);
    private static string? ReturnNote(string? text)
    {
        var normalized = DeliveryTripPolicy.NormalizeNote(text);
        DeliveryValues.Require(normalized is null || !normalized.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')),
            "NOTE_INVALID", "Nội dung không chứa ký tự điều khiển.");
        return normalized;
    }
    private static void Proof([DoesNotReturnIf(false)] bool valid)
    {
        if (!valid) throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ kết quả/nguồn/trách nhiệm không khớp; chưa cho phép ghi tiếp.");
    }
    private static T ReadSnapshot<T>(string json, string hash)
    {
        Proof(Hash(json) == hash);
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw Error(409, "OUTCOME_PROOF_INVALID", "Thiếu chứng cứ kết quả."); }
        catch (JsonException) { throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ kết quả không đúng cấu trúc."); }
    }

    private async Task DepartedProof(DeliveryTrip trip, IReadOnlyList<DeliveryTripOrder> members, CancellationToken ct)
    {
        RequireState(trip, DeliveryTripState.Departed);
        Proof(trip.DepartedAtUtc.HasValue && trip.DepartedByUserId.HasValue && members.Count > 0 &&
            members.All(x => x.IsActive && x.CurrentCustodyEventId.HasValue));
        var departure = await db.DeliveryTripRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store &&
            x.TripId == trip.Id && x.Action == "depart", ct);
        Proof(departure is not null && departure.Revision <= trip.Revision && Hash(departure.SnapshotJson) == departure.SnapshotHash);
        try
        {
            using var document = JsonDocument.Parse(departure!.SnapshotJson);
            var root = document.RootElement;
            Proof(root.GetProperty("id").GetInt32() == trip.Id && root.GetProperty("state").GetString() == "Departed" &&
                root.GetProperty("revision").GetInt32() == departure.Revision &&
                root.GetProperty("courierUserId").GetInt32() == trip.CourierUserId &&
                root.GetProperty("departedByUserId").GetInt32() == trip.DepartedByUserId &&
                root.GetProperty("departedAtUtc").GetDateTime() == trip.DepartedAtUtc);
            var active = root.GetProperty("bindings").EnumerateArray().Where(x => x.GetProperty("isActive").GetBoolean()).ToArray();
            Proof(active.Length == members.Count);
            foreach (var member in members)
            {
                var matches = active.Where(x => x.GetProperty("id").GetInt32() == member.Id).ToArray();
                Proof(matches.Length == 1);
                var saved = matches[0];
                Proof(saved.GetProperty("deliveryOrderId").GetInt32() == member.DeliveryOrderId &&
                    saved.GetProperty("sourceWarehouseId").GetInt32() == member.SourceWarehouseId &&
                    saved.GetProperty("sourceLegalEntityId").GetInt32() == member.SourceLegalEntityId &&
                    saved.GetProperty("currentCustodyEventId").GetInt32() == member.CurrentCustodyEventId);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ xuất phát không đúng cấu trúc."); }
    }

    private async Task<OutcomeSource[]> OutcomeSources(DeliveryTrip trip, DeliveryTripOrder member,
        DeliveryOrder order, CancellationToken ct)
    {
        Proof(member.IsActive && member.TripId == trip.Id && member.DeliveryOrderId == order.Id &&
            member.SourceWarehouseId == order.SourceWarehouseId && member.SourceLegalEntityId == order.SourceLegalEntityId &&
            member.CurrentCustodyEventId.HasValue);
        var custody = await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct);
        Proof(custody is not null && custody.Context.TripId == trip.Id && custody.Context.CustodyEventId == member.CurrentCustodyEventId &&
            custody.Context.CourierUserId == trip.CourierUserId && custody.Context.DepartedAtUtc == Utc(trip.DepartedAtUtc!.Value));
        var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        var dispatch = journals.Where(x => x.Kind == DeliveryJournalKind.Dispatch).ToArray();
        Proof(dispatch.Length > 0 && dispatch.All(x => x.DeliveryOrderLineId.HasValue && x.VariantId.HasValue &&
            x.SourceWarehouseId == order.SourceWarehouseId && x.SourceLegalEntityId == order.SourceLegalEntityId) &&
            dispatch.Select(x => x.DeliveryOrderLineId).Distinct().Count() == dispatch.Length);
        Proof(journals.Where(x => x.Kind == DeliveryJournalKind.Return).All(x =>
            dispatch.Any(d => d.DeliveryOrderLineId == x.DeliveryOrderLineId && d.VariantId == x.VariantId) &&
            x.SourceWarehouseId == order.SourceWarehouseId && x.SourceLegalEntityId == order.SourceLegalEntityId && x.BaseQuantity > 0));
        var sources = new List<OutcomeSource>();
        foreach (var entry in dispatch.OrderBy(x => x.DeliveryOrderLineId))
        {
            var line = order.Lines.SingleOrDefault(x => x.Id == entry.DeliveryOrderLineId && x.VariantId == entry.VariantId);
            Proof(line is not null && entry.BaseQuantity > 0 && custody!.Quantities.ContainsKey(line.Id));
            var accepted = journals.Where(x => x.Kind == DeliveryJournalKind.Return && x.DeliveryOrderLineId == line!.Id).Sum(x => x.BaseQuantity);
            var current = custody!.Quantities[line!.Id];
            Proof(accepted >= 0 && current >= 0 && accepted + current == entry.BaseQuantity);
            try
            {
                DeliveryValues.Quantity(entry.BaseQuantity, "Xuất nguồn");
                DeliveryValues.Quantity(accepted, "Nhận trả");
                DeliveryValues.Quantity(current, "Trách nhiệm giữ hàng");
            }
            catch (DeliveryRuleException) { throw Error(409, "OUTCOME_PROOF_INVALID", "Số lượng chứng cứ nguồn không hợp lệ."); }
            sources.Add(new(line.Id, line.VariantId, entry.Id, entry.BaseQuantity, accepted, current));
        }
        return sources.ToArray();
    }

    public Task<DeliveryTripCommandAck> RecordOutcomeAsync(int id, int orderId, DeliveryOutcomeRequest request,
        CancellationToken ct = default)
    {
        _ = DeliveryTripPolicy.Version(request.ExpectedOrderVersion);
        var source = ReportSource(request.ReportSource);
        var cash = DeliveryOutcomePolicy.ParseMoney(request.ReportedCashText);
        var transfer = DeliveryOutcomePolicy.ParseMoney(request.ReportedTransferText);
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(x => x is null || x.LineId <= 0) ||
            request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count)
            throw Error(400, "LINE_INVALID", "Cần đầy đủ các dòng đã xuất, không trùng.");
        foreach (var line in request.Lines)
        { _ = DeliveryOutcomePolicy.ParseQuantity(line.DeliveredBaseQuantityText); _ = DeliveryOutcomePolicy.ParseQuantity(line.ReportedReturnBaseQuantityText); }
        return Command(id, request.ClientRequestId, "outcome", request.ExpectedTripVersion, new { orderId, request },
            ReportCapability(request.ReportedCourierUserId), null, async (value, members, _) =>
            {
                var trip = value!; await DepartedProof(trip, members, ct); await Courier(trip.CourierUserId, ct);
                if (trip.CourierUserId != request.ReportedCourierUserId) throw Error(400, "COURIER_MISMATCH", "Báo cáo phải thuộc người giao thật của chuyến.");
                var member = members.SingleOrDefault(x => x.DeliveryOrderId == orderId)
                    ?? throw Error(404, "DELIVERY_NOT_FOUND", "Đơn không thuộc chuyến.");
                var order = await Order(orderId, false, true, ct); Match(request.ExpectedOrderVersion, order.RowVersion);
                if (!Reportable(order.State)) throw Error(409, "REPORT_LOCKED", "Kết quả đã khóa hoặc đơn chưa xuất phát.");
                var sources = await OutcomeSources(trip, member, order, ct);
                var previous = await LatestOutcome(trip, member, order, sources, ct);
                var returned = await ReturnSnapshot(trip, ct);
                if (!sources.Select(x => x.LineId).SequenceEqual(request.Lines.Select(x => x.LineId).Order()))
                    throw Error(400, "LINE_INVALID", "Cần đúng toàn bộ dòng hàng đã xuất của đơn này.");
                var quantities = sources.Select(x => new DeliveryOutcomeQuantity(x.LineId, x.Dispatched, x.AcceptedReturn,
                    x.CurrentCustody, DeliveryOutcomePolicy.ParseQuantity(request.Lines.Single(l => l.LineId == x.LineId).DeliveredBaseQuantityText),
                    DeliveryOutcomePolicy.ParseQuantity(request.Lines.Single(l => l.LineId == x.LineId).ReportedReturnBaseQuantityText))).ToArray();
                var reason = DeliveryOutcomePolicy.Validate(request.Outcome, request.Reason, quantities, cash, transfer);
                var minimum = new[] { trip.DepartedAtUtc!.Value, previous?.RecordedAtUtc.UtcDateTime ?? DateTime.MinValue,
                    returned?.RecordedAtUtc.UtcDateTime ?? DateTime.MinValue }.Max();
                var now = await SqlUtcAtOrAfter(minimum, ct);
                var report = DeliveryReportPolicy.Record(ReportActor(order, trip.CourierUserId), Origin(order), order.State,
                    trip.CourierUserId, request.ReportedCourierUserId, source, Utc(trip.DepartedAtUtc.Value), Utc(now), request.CustomerReceivedAtUtc);
                if (order.State != DeliveryState.CancellationPending)
                    order.State = DeliveryWorkflowPolicy.Apply(order.State, DeliveryCommand.RecordOutcome, new(CourierAssigned: true, CustodyHandoverConfirmed: true));
                order.Revision++; await db.SaveChangesAsync(ct);
                var snapshot = new DeliveryOutcomeSnapshot("delivery-outcome-v1", Store, trip.Id, order.Id, member.Id,
                    member.CurrentCustodyEventId!.Value, order.SourceWarehouseId, order.SourceLegalEntityId, order.Revision,
                    Version(order.RowVersion), order.State.ToString(), request.ClientRequestId, Actor, trip.CourierUserId, source.ToString(), request.Outcome,
                    reason, request.ReportedCashText, request.ReportedTransferText, Utc(trip.DepartedAtUtc.Value), report.RecordedAtUtc,
                    report.CustomerReceivedAtUtc, sources.Select(x => new DeliveryOutcomeLineSnapshot(x.LineId, x.VariantId,
                        x.DispatchJournalEntryId, Qty(x.Dispatched), Qty(x.AcceptedReturn),
                        request.Lines.Single(l => l.LineId == x.LineId).DeliveredBaseQuantityText,
                        request.Lines.Single(l => l.LineId == x.LineId).ReportedReturnBaseQuantityText)).ToArray());
                await RecordOutcomeSnapshot(order, snapshot, ct); trip.Revision++; return trip;
            }, ct);
    }

    private async Task RecordOutcomeSnapshot(DeliveryOrder order, DeliveryOutcomeSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, Json); var hash = Hash(json);
        db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, ActorUserId = Actor,
            Action = "trip-outcome", AggregateVersion = Version(order.RowVersion), SnapshotJson = json, SnapshotHash = hash });
        db.DeliveryCommandReceipts.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, ActorUserId = Actor,
            ClientRequestId = DeliveryTripPolicy.ChildCommandId(snapshot.RootClientRequestId, order.Id), Operation = "trip-outcome",
            RequestHash = hash, OutcomeJson = json });
        var eventId = Guid.NewGuid();
        db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, EventId = eventId,
            Action = "trip-outcome", PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, deliveryId = order.Id,
                order.Code, order.Revision, action = "trip-outcome", actorUserId = Actor, recordedAtUtc = snapshot.RecordedAtUtc }, Json) });
        await db.SaveChangesAsync(ct);
    }

    public Task<DeliveryTripCommandAck> RecordReturnToStoreAsync(int id, DeliveryTripReturnRequest request, CancellationToken ct = default)
    {
        _ = ReturnNote(request.ConfirmationNote);
        DeliveryValues.Require(request.PhysicalReturnConfirmed && request.ConfirmedCourierUserId > 0,
            "PHYSICAL_RETURN_REQUIRED", "Cần xác nhận người giao thực tế đã trở về.");
        return Command(id, request.ClientRequestId, "return-to-store", request.ExpectedTripVersion, request,
            ReportCapability(request.ConfirmedCourierUserId), null, async (value, members, unusedTime) =>
            {
                var trip = value!; await DepartedProof(trip, members, ct); await Courier(trip.CourierUserId, ct);
                if (request.ConfirmedCourierUserId != trip.CourierUserId)
                    throw Error(400, "COURIER_MISMATCH", "Cần xác nhận đúng người giao thực tế của chuyến.");
                if (await ReturnSnapshot(trip, ct) is not null) throw Error(409, "RETURN_ALREADY_RECORDED", "Chuyến đã có chứng cứ trở về.");
                if (!members.Any()) throw Error(409, "CUSTODY_PROOF_INVALID", "Chuyến không có đơn đã bàn giao.");
                var reportable = false;
                foreach (var member in members)
                {
                    var order = await Order(member.DeliveryOrderId, false, false, ct);
                    var sources = await OutcomeSources(trip, member, order, ct);
                    _ = await LatestOutcome(trip, member, order, sources, ct);
                    reportable |= Reportable(order.State);
                }
                if (!reportable) throw Error(409, "REPORT_LOCKED", "Chuyến đã khóa kết quả.");
                trip.Revision++; return trip;
            }, ct);
    }

    private async Task<DeliveryOutcomeSnapshot?> LatestOutcome(DeliveryTrip trip, DeliveryTripOrder member, DeliveryOrder order,
        IReadOnlyList<OutcomeSource> sources, CancellationToken ct)
    {
        var revision = await db.DeliveryRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            x.Action == "trip-outcome").OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        if (revision is null) return null;
        var saved = ReadSnapshot<DeliveryOutcomeSnapshot>(revision.SnapshotJson, revision.SnapshotHash);
        try
        {
            Proof(saved.Schema == "delivery-outcome-v1" && saved.StoreId == Store && saved.TripId == trip.Id && saved.DeliveryOrderId == order.Id &&
                saved.TripOrderId == member.Id && saved.CustodyEventId == member.CurrentCustodyEventId &&
                saved.SourceWarehouseId == order.SourceWarehouseId && saved.SourceLegalEntityId == order.SourceLegalEntityId &&
                saved.OrderRevision == revision.Revision && saved.OrderRevision <= order.Revision && saved.OrderVersion == revision.AggregateVersion &&
                saved.ActorUserId == revision.ActorUserId && saved.CourierUserId == trip.CourierUserId && saved.RootClientRequestId != Guid.Empty &&
                saved.DepartedAtUtc == Utc(trip.DepartedAtUtc!.Value) && saved.RecordedAtUtc >= saved.DepartedAtUtc &&
                saved.Lines is not null && saved.Lines.Count == sources.Count &&
                saved.Lines.Select(x => x.LineId).SequenceEqual(sources.Select(x => x.LineId)));
            _ = DeliveryTripPolicy.Version(saved.OrderVersion);
            DeliveryValues.Utc(saved.RecordedAtUtc);
            var source = ReportSource(saved.ReportSource);
            var actorCapability = saved.ActorUserId == saved.CourierUserId ? DeliveryCapability.ReportOwnOutcome : DeliveryCapability.ReportOnBehalf;
            var actor = new DeliveryActor(Store, saved.ActorUserId, actorCapability, new HashSet<int> { order.SourceWarehouseId }, new HashSet<int> { order.SourceLegalEntityId });
            Proof(Enum.TryParse<DeliveryState>(saved.OrderState, false, out var reportedState) &&
                reportedState.ToString() == saved.OrderState && Reportable(reportedState));
            _ = DeliveryReportPolicy.Record(actor, Origin(order), reportedState, trip.CourierUserId,
                saved.CourierUserId, source, saved.DepartedAtUtc, saved.RecordedAtUtc, saved.CustomerReceivedAtUtc);
            var historical = new List<DeliveryOutcomeQuantity>();
            foreach (var line in saved.Lines!)
            {
                var actual = sources.Single(x => x.LineId == line.LineId);
                var accepted = DeliveryOutcomePolicy.ParseQuantity(line.AcceptedReturnedAtReportBaseQuantityText);
                Proof(line.VariantId == actual.VariantId && line.DispatchJournalEntryId == actual.DispatchJournalEntryId &&
                    DeliveryOutcomePolicy.ParseQuantity(line.DispatchedBaseQuantityText) == actual.Dispatched && accepted <= actual.AcceptedReturn);
                historical.Add(new(line.LineId, actual.Dispatched, accepted, actual.Dispatched - accepted,
                    DeliveryOutcomePolicy.ParseQuantity(line.DeliveredBaseQuantityText), DeliveryOutcomePolicy.ParseQuantity(line.ReportedReturnBaseQuantityText)));
            }
            Proof(DeliveryOutcomePolicy.Validate(saved.Outcome, saved.Reason, historical, DeliveryOutcomePolicy.ParseMoney(saved.ReportedCashText),
                DeliveryOutcomePolicy.ParseMoney(saved.ReportedTransferText)) == saved.Reason);
            var childKey = DeliveryTripPolicy.ChildCommandId(saved.RootClientRequestId, order.Id);
            var child = await db.DeliveryCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == childKey, ct);
            var root = await db.DeliveryTripCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == saved.RootClientRequestId, ct);
            Proof(child is not null && child.DeliveryOrderId == order.Id && child.ActorUserId == saved.ActorUserId && child.Operation == "trip-outcome" &&
                child.RequestHash == revision.SnapshotHash && child.OutcomeJson == revision.SnapshotJson && root is not null &&
                root.TripId == trip.Id && root.ActorUserId == saved.ActorUserId && root.Operation == "outcome" &&
                root.RequiredCapabilities == (DeliveryCapability.View | actorCapability));
            var ack = JsonSerializer.Deserialize<DeliveryTripCommandAck>(root!.OutcomeJson, Json);
            Proof(ack is not null && ack.ClientRequestId == saved.RootClientRequestId && ack.TripId == trip.Id && ack.Operation == "outcome" &&
                ack.RecordedAtUtc >= saved.RecordedAtUtc && ack.Orders.Count(x => x.DeliveryOrderId == order.Id &&
                    x.Revision == saved.OrderRevision && x.Version == saved.OrderVersion && x.State == saved.OrderState) == 1);
            var requestElement = await TripReceiptRequest(trip, root, ack, ct);
            Proof(requestElement.GetProperty("orderId").GetInt32() == order.Id);
            var original = requestElement.GetProperty("request").Deserialize<DeliveryOutcomeRequest>(Json);
            Proof(original is not null && original.ClientRequestId == saved.RootClientRequestId &&
                original.ReportedCourierUserId == saved.CourierUserId && original.ReportSource == saved.ReportSource &&
                original.Outcome == saved.Outcome && original.ReportedCashText == saved.ReportedCashText &&
                original.ReportedTransferText == saved.ReportedTransferText && original.CustomerReceivedAtUtc == saved.CustomerReceivedAtUtc &&
                (string.IsNullOrWhiteSpace(original.Reason) ? null : original.Reason.Trim()) == saved.Reason &&
                original.Lines is not null && original.Lines.Count == saved.Lines.Count && original.Lines.All(x =>
                    saved.Lines.Count(l => l.LineId == x.LineId && l.DeliveredBaseQuantityText == x.DeliveredBaseQuantityText &&
                        l.ReportedReturnBaseQuantityText == x.ReportedReturnBaseQuantityText) == 1));
        }
        catch (Exception ex) when (ex is DeliveryRuleException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException or KeyNotFoundException)
        { throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ kết quả không đúng nguồn/số lượng/thời điểm."); }
        return saved;
    }

    private async Task<DeliveryTripReturnSnapshot?> ReturnSnapshot(DeliveryTrip trip, CancellationToken ct)
    {
        var revisions = await db.DeliveryTripRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == trip.Id &&
            x.Action == "return-to-store").ToListAsync(ct);
        if (revisions.Count == 0) return null;
        Proof(revisions.Count == 1);
        var revision = revisions[0]; Proof(Hash(revision.SnapshotJson) == revision.SnapshotHash);
        try
        {
            using var document = JsonDocument.Parse(revision.SnapshotJson);
            var saved = document.RootElement.GetProperty("returnToStore").Deserialize<DeliveryTripReturnSnapshot>(Json)!;
            Proof(saved is not null && saved.Schema == "delivery-return-v1" && saved.StoreId == Store && saved.TripId == trip.Id &&
                saved.TripRevision == revision.Revision && revision.Revision <= trip.Revision && saved.ActorUserId == revision.ActorUserId &&
                saved.CourierUserId == trip.CourierUserId && saved.RootClientRequestId != Guid.Empty && saved.PhysicalReturnConfirmed &&
                saved.DepartedAtUtc == Utc(trip.DepartedAtUtc!.Value) && saved.RecordedAtUtc >= saved.DepartedAtUtc);
            DeliveryValues.Utc(saved!.RecordedAtUtc); Proof(ReturnNote(saved.ConfirmationNote) == saved.ConfirmationNote);
            var receipt = await db.DeliveryTripCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == saved.RootClientRequestId, ct);
            Proof(receipt is not null && receipt.TripId == trip.Id && receipt.ActorUserId == saved.ActorUserId && receipt.Operation == "return-to-store" &&
                receipt.RequiredCapabilities == (DeliveryCapability.View | (saved.ActorUserId == saved.CourierUserId ?
                    DeliveryCapability.ReportOwnOutcome : DeliveryCapability.ReportOnBehalf)));
            var ack = JsonSerializer.Deserialize<DeliveryTripCommandAck>(receipt!.OutcomeJson, Json);
            Proof(ack is not null && ack.ClientRequestId == saved.RootClientRequestId && ack.TripId == trip.Id &&
                ack.Operation == "return-to-store" && ack.AppliedRevision == saved.TripRevision &&
                ack.AppliedVersion == revision.AggregateVersion && ack.RecordedAtUtc == saved.RecordedAtUtc);
            var requestElement = await TripReceiptRequest(trip, receipt, ack, ct);
            var original = requestElement.Deserialize<DeliveryTripReturnRequest>(Json);
            Proof(original is not null && original.ClientRequestId == saved.RootClientRequestId &&
                original.ConfirmedCourierUserId == saved.CourierUserId && original.PhysicalReturnConfirmed &&
                ReturnNote(original.ConfirmationNote) == saved.ConfirmationNote);
            return saved;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or DeliveryRuleException or NullReferenceException)
        { throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ trở về không đúng cấu trúc/thời điểm."); }
    }

    private async Task<JsonElement> TripReceiptRequest(DeliveryTrip trip, DeliveryTripCommandReceipt receipt,
        DeliveryTripCommandAck ack, CancellationToken ct)
    {
        var revision = await db.DeliveryTripRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store &&
            x.TripId == trip.Id && x.Revision == ack.AppliedRevision, ct);
        Proof(revision is not null && revision.ActorUserId == receipt.ActorUserId && revision.Action == receipt.Operation &&
            revision.AggregateVersion == ack.AppliedVersion && Hash(revision.SnapshotJson) == revision.SnapshotHash);
        using var document = JsonDocument.Parse(revision.SnapshotJson);
        var body = document.RootElement;
        Proof(body.GetProperty("id").GetInt32() == trip.Id && body.GetProperty("revision").GetInt32() == ack.AppliedRevision &&
            body.GetProperty("state").GetString() == "Departed" && body.GetProperty("courierUserId").GetInt32() == trip.CourierUserId &&
            body.GetProperty("actorUserId").GetInt32() == receipt.ActorUserId && body.GetProperty("action").GetString() == receipt.Operation &&
            body.GetProperty("recordedAtUtc").GetDateTimeOffset() == ack.RecordedAtUtc);
        var request = body.GetProperty("request").Clone();
        // Recreate the original typed command: JsonElement strings can escape DateTimeOffset's '+',
        // producing different bytes from the date converter despite the same timestamp.
        var requestHash = receipt.Operation switch
        {
            "outcome" => ReceiptRequestHash(trip, receipt, request.Deserialize<OutcomeCommandEnvelope>(Json)
                ?? throw Error(409, "OUTCOME_PROOF_INVALID", "Thiếu nội dung lệnh kết quả.")),
            "return-to-store" => ReceiptRequestHash(trip, receipt, request.Deserialize<DeliveryTripReturnRequest>(Json)
                ?? throw Error(409, "OUTCOME_PROOF_INVALID", "Thiếu nội dung lệnh trở về.")),
            _ => throw Error(409, "OUTCOME_PROOF_INVALID", "Thao tác chứng cứ không hợp lệ.")
        };
        Proof(requestHash == receipt.RequestHash);
        return request;
    }

    private string ReceiptRequestHash<T>(DeliveryTrip trip, DeliveryTripCommandReceipt receipt, T request)
        => Hash(JsonSerializer.Serialize(new { schema = "delivery-trip-v1", store = Store, actor = receipt.ActorUserId,
            id = trip.Id, operation = receipt.Operation, request }, Json));

    private async Task ValidateOutcomeReplay(DeliveryTripCommandReceipt receipt, DeliveryTripCommandAck ack,
        IReadOnlyList<DeliveryTripOrder> bindings, Guid key, string operation, CancellationToken ct)
    {
        try
        {
            var trip = await Trip(receipt.TripId, false, ct);
            var members = bindings.Where(x => x.IsActive).OrderBy(x => x.Position).ToArray();
            await DepartedProof(trip, members, ct);
            Proof(ack.ClientRequestId == key && ack.TripId == trip.Id && ack.Operation == operation && !ack.Replayed &&
                ack.AppliedRevision > 0 && ack.AppliedRevision <= trip.Revision && ack.RecordedAtUtc >= Utc(trip.DepartedAtUtc!.Value) &&
                ack.Orders is not null && ack.Orders.Select(x => x.DeliveryOrderId).SequenceEqual(members.Select(x => x.DeliveryOrderId)));
            _ = DeliveryTripPolicy.Version(ack.AppliedVersion); DeliveryValues.Utc(ack.RecordedAtUtc);
            _ = await TripReceiptRequest(trip, receipt, ack, ct);
            foreach (var applied in ack.Orders)
            { _ = DeliveryTripPolicy.Version(applied.Version); Proof(applied.Revision > 0); }
            foreach (var member in members)
            {
                var order = await Order(member.DeliveryOrderId, false, false, ct);
                var sources = await OutcomeSources(trip, member, order, ct);
                _ = await LatestOutcome(trip, member, order, sources, ct);
            }
            _ = await ReturnSnapshot(trip, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or DeliveryRuleException or NullReferenceException)
        { throw Error(409, "OUTCOME_PROOF_INVALID", "Chứng cứ phản hồi không đúng cấu trúc/nguồn."); }
    }

    private async Task<DeliveryTripDetailDto> ProjectOutcomes(DeliveryTrip trip, DeliveryTripDetailDto detail, CancellationToken ct)
    {
        var own = await Has(DeliveryCapability.ReportOwnOutcome, ct);
        var proxy = await Has(DeliveryCapability.ReportOnBehalf, ct);
        var courierActive = await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == trip.CourierUserId &&
            x.IsActive && x.User.IsActive, ct);
        var selectedPermission = courierActive && (Actor == trip.CourierUserId ? own : proxy);
        var members = await Members(trip.Id, false, ct);
        var returned = await ReturnSnapshot(trip, ct);
        var rows = new List<DeliveryTripOrderDto>();
        var minimum = trip.DepartedAtUtc ?? trip.HandedOverAtUtc ?? trip.CreatedAtUtc;
        if (trip.State == DeliveryTripState.Departed) await DepartedProof(trip, members, ct);
        foreach (var row in detail.Orders)
        {
            var member = members.SingleOrDefault(x => x.DeliveryOrderId == row.DeliveryOrderId);
            DeliveryOrderOutcomeDto? projection = null;
            var canReport = false;
            if (member is not null && trip.State == DeliveryTripState.Departed)
            {
                var order = await Order(row.DeliveryOrderId, false, false, ct);
                var sources = await OutcomeSources(trip, member, order, ct);
                var saved = await LatestOutcome(trip, member, order, sources, ct);
                canReport = selectedPermission && Reportable(order.State);
                if (saved is not null)
                {
                    minimum = saved.RecordedAtUtc.UtcDateTime > minimum ? saved.RecordedAtUtc.UtcDateTime : minimum;
                    var currentValid = saved.Lines.All(x => DeliveryOutcomePolicy.ParseQuantity(x.ReportedReturnBaseQuantityText) >=
                        sources.Single(s => s.LineId == x.LineId).AcceptedReturn);
                    projection = new(saved.OrderRevision, saved.OrderVersion, saved.TripId, saved.DeliveryOrderId, saved.CustodyEventId,
                        saved.ActorUserId, await Name(saved.ActorUserId, ct), saved.CourierUserId, await Name(saved.CourierUserId, ct),
                        saved.ReportSource, saved.Outcome, saved.Reason, saved.ReportedCashText, saved.ReportedTransferText,
                        saved.RecordedAtUtc, saved.CustomerReceivedAtUtc, saved.Lines.Select(x => new DeliveryOutcomeLineDto(x.LineId,
                            x.DeliveredBaseQuantityText, x.ReportedReturnBaseQuantityText, x.AcceptedReturnedAtReportBaseQuantityText,
                            Qty(DeliveryOutcomePolicy.ParseQuantity(x.ReportedReturnBaseQuantityText) - sources.Single(s => s.LineId == x.LineId).AcceptedReturn))).ToArray())
                    { IsCurrentValid = currentValid };
                }
            }
            rows.Add(row with { Outcome = projection, CanReportOutcome = canReport });
        }
        DeliveryTripReturnDto? arrival = null;
        if (returned is not null)
        {
            minimum = returned.RecordedAtUtc.UtcDateTime > minimum ? returned.RecordedAtUtc.UtcDateTime : minimum;
            arrival = new(returned.TripRevision, returned.ActorUserId, await Name(returned.ActorUserId, ct), returned.CourierUserId,
                await Name(returned.CourierUserId, ct), returned.RecordedAtUtc, returned.ConfirmationNote);
        }
        var allComplete = members.Count > 0 && rows.Where(x => x.IsActive).All(x => x.Outcome is { IsCurrentValid: true });
        var canReportAny = rows.Any(x => x.CanReportOutcome);
        return detail with { Orders = rows, ServerTimeUtc = Utc(await SqlUtcAtOrAfter(minimum, ct)), ReturnToStore = arrival,
            AllOutcomesComplete = allComplete, ReadyForReconciliation = arrival is not null && allComplete &&
                rows.Where(x => x.IsActive).All(x => x.State is "AwaitingReconciliation" or "CancellationPending"),
            OutcomeCapabilities = new(Actor == trip.CourierUserId && own && canReportAny,
                Actor != trip.CourierUserId && proxy && canReportAny,
                trip.State == DeliveryTripState.Departed && arrival is null && selectedPermission && canReportAny) };
    }
}
