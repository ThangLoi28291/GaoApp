using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

// New73 SQL only. The previous delivery catalog and every historical migration retain their bytes.
internal static class DeliveryTripSchemaSqlCatalog
{
    internal const string MigrationId = "20261008100000_AddDeliveryTrips";
    internal const string DownGuardSql = "IF EXISTS (SELECT 1 FROM DeliveryTrips) OR EXISTS (SELECT 1 FROM DeliveryTripOrders) OR EXISTS (SELECT 1 FROM DeliveryCustodyEvents) OR EXISTS (SELECT 1 FROM DeliveryCustodyLines) OR EXISTS (SELECT 1 FROM DeliveryTripRevisions) OR EXISTS (SELECT 1 FROM DeliveryTripCommandReceipts) OR EXISTS (SELECT 1 FROM DeliveryTripOutboxMessages) THROW 51039, 'Trip and custody history is occupied; use a forward fix.', 1;";
    internal static readonly string TripTransitionSql = """
CREATE TRIGGER [TR_DeliveryTrips_Transition] ON [DeliveryTrips] AFTER INSERT, UPDATE, DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR CONVERT(varbinary(max),i.Code)<>CONVERT(varbinary(max),d.Code) OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAtUtc<>d.CreatedAtUtc OR i.IsDeleted<>0 OR i.Revision<>d.Revision+1 OR d.State=0 AND i.State NOT IN (0,1,3) OR d.State=1 AND i.State NOT IN (1,2) OR d.State IN (2,3) AND i.State<>d.State OR d.State<>0 AND (CONVERT(varbinary(max),ISNULL(i.Note,N''))<>CONVERT(varbinary(max),ISNULL(d.Note,N'')) OR (i.Note IS NULL AND d.Note IS NOT NULL) OR (i.Note IS NOT NULL AND d.Note IS NULL)) OR d.State IN (2,3) AND i.CourierUserId<>d.CourierUserId OR d.HandedOverAtUtc IS NOT NULL AND (i.HandedOverAtUtc IS NULL OR i.HandedOverAtUtc<>d.HandedOverAtUtc OR i.HandedOverByUserId IS NULL OR i.HandedOverByUserId<>d.HandedOverByUserId) OR d.DepartedAtUtc IS NOT NULL AND (i.DepartedAtUtc IS NULL OR i.DepartedAtUtc<>d.DepartedAtUtc OR i.DepartedByUserId IS NULL OR i.DepartedByUserId<>d.DepartedByUserId) OR d.AbandonedAtUtc IS NOT NULL AND (i.AbandonedAtUtc IS NULL OR i.AbandonedAtUtc<>d.AbandonedAtUtc OR i.AbandonedByUserId IS NULL OR i.AbandonedByUserId<>d.AbandonedByUserId))
 THROW 51030, 'Trip identity, transitions and recorded server times cannot be rewritten.', 1;
 IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND (i.State<>0 OR i.Revision<>1))
 THROW 51030, 'New trips begin as draft without custody or departure.', 1;
 IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id LEFT JOIN UserInStores m ON m.StoreId=i.StoreId AND m.UserId=i.CourierUserId LEFT JOIN Users u ON u.Id=i.CourierUserId WHERE (d.Id IS NULL OR i.CourierUserId<>d.CourierUserId OR i.State<>d.State AND i.State IN (1,2)) AND (m.Id IS NULL OR m.IsActive=0 OR m.IsDeleted=1 OR u.Id IS NULL OR u.IsActive=0 OR u.IsDeleted=1))
 THROW 51030, 'New custody or departure requires an active same-store courier.', 1;
 IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE (d.State=0 AND i.State=1 OR d.State=1 AND i.CourierUserId<>d.CourierUserId OR d.State=1 AND i.State=2) AND (NOT EXISTS (SELECT 1 FROM DeliveryTripOrders b WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND b.IsActive=1) OR EXISTS (SELECT 1 FROM DeliveryTripOrders b LEFT JOIN DeliveryCustodyEvents e ON e.StoreId=b.StoreId AND e.Id=b.CurrentCustodyEventId AND e.TripId=b.TripId AND e.TripOrderId=b.Id AND e.DeliveryOrderId=b.DeliveryOrderId LEFT JOIN DeliveryOrders o ON o.StoreId=b.StoreId AND o.Id=b.DeliveryOrderId WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND b.IsActive=1 AND (e.Id IS NULL OR e.CourierUserId<>i.CourierUserId OR i.State=1 AND (o.State<>4 OR e.ConfirmedAtUtc>i.HandedOverAtUtc AND d.State=0) OR i.State=2 AND (o.State<>5 OR e.ConfirmedAtUtc>i.DepartedAtUtc) OR NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=e.StoreId AND j.DeliveryOrderId=e.DeliveryOrderId AND j.Kind=1 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id AND l.DispatchJournalEntryId=j.Id))))))
 THROW 51030, 'Every active order requires complete current physical custody before handover, transfer or departure.', 1;
 IF EXISTS (SELECT 1 FROM inserted i WHERE i.State=3 AND EXISTS (SELECT 1 FROM DeliveryTripOrders b WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND (b.IsActive=1 OR b.CurrentCustodyEventId IS NOT NULL)))
 THROW 51030, 'Abandonment releases planned bindings only and cannot discard physical custody.', 1;
END;
""";
    internal static readonly string BindingSourceSql = """
CREATE TRIGGER [TR_DeliveryTripOrders_Source] ON [DeliveryTripOrders] AFTER INSERT, UPDATE, DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id JOIN DeliveryTrips t ON t.StoreId=d.StoreId AND t.Id=d.TripId WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR i.TripId<>d.TripId OR i.DeliveryOrderId<>d.DeliveryOrderId OR i.SourceWarehouseId<>d.SourceWarehouseId OR i.SourceLegalEntityId<>d.SourceLegalEntityId OR i.IsDeleted<>0 OR d.CurrentCustodyEventId IS NOT NULL AND (i.IsActive<>d.IsActive OR i.Position<>d.Position OR i.AssignedApprovedRevision<>d.AssignedApprovedRevision OR CONVERT(varbinary(max),i.AssignedOrderVersion)<>CONVERT(varbinary(max),d.AssignedOrderVersion) OR i.CurrentCustodyEventId IS NULL) OR (i.IsActive<>d.IsActive OR i.Position<>d.Position OR i.AssignedApprovedRevision<>d.AssignedApprovedRevision OR CONVERT(varbinary(max),i.AssignedOrderVersion)<>CONVERT(varbinary(max),d.AssignedOrderVersion)) AND (t.State<>0 OR d.CurrentCustodyEventId IS NOT NULL))
 THROW 51031, 'Binding origin is immutable; only draft unconfirmed assignments may be refreshed, reordered or removed.', 1;
 IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId WHERE d.Id IS NULL AND (t.State<>0 OR i.CurrentCustodyEventId IS NOT NULL OR i.IsActive<>1))
 THROW 51031, 'New bindings are planned draft assignments.', 1;
 IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId LEFT JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CurrentCustodyEventId WHERE i.CurrentCustodyEventId IS NOT NULL AND ISNULL(d.CurrentCustodyEventId,0)<>i.CurrentCustodyEventId AND (i.IsActive<>1 OR e.Id IS NULL OR e.TripId<>i.TripId OR e.TripOrderId<>i.Id OR e.DeliveryOrderId<>i.DeliveryOrderId OR e.SourceWarehouseId<>i.SourceWarehouseId OR e.SourceLegalEntityId<>i.SourceLegalEntityId OR d.CurrentCustodyEventId IS NULL AND (t.State<>0 OR e.Kind<>1 OR e.CourierUserId<>t.CourierUserId) OR d.CurrentCustodyEventId IS NOT NULL AND (t.State<>1 OR e.Kind<>2 OR e.PreviousCustodyEventId IS NULL OR e.PreviousCustodyEventId<>d.CurrentCustodyEventId) OR NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=e.StoreId AND j.DeliveryOrderId=e.DeliveryOrderId AND j.Kind=1 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id AND l.DispatchJournalEntryId=j.Id))))
 THROW 51031, 'Current custody must advance through a complete same-source physical receipt.', 1;
END;
""";
    internal static readonly string CustodySourceSql = """
CREATE TRIGGER [TR_DeliveryCustodyEvents_Source] ON [DeliveryCustodyEvents] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryTripOrders b ON b.StoreId=i.StoreId AND b.Id=i.TripOrderId JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId LEFT JOIN DeliveryCustodyEvents p ON p.StoreId=i.StoreId AND p.Id=i.PreviousCustodyEventId LEFT JOIN UserInStores m ON m.StoreId=i.StoreId AND m.UserId=i.CourierUserId LEFT JOIN Users u ON u.Id=i.CourierUserId WHERE b.IsActive<>1 OR i.ConfirmedAtUtc>SYSUTCDATETIME() OR m.Id IS NULL OR m.IsActive=0 OR m.IsDeleted=1 OR u.Id IS NULL OR u.IsActive=0 OR u.IsDeleted=1 OR i.Kind=1 AND (t.State<>0 OR b.CurrentCustodyEventId IS NOT NULL OR i.CourierUserId<>t.CourierUserId) OR i.Kind=2 AND (t.State<>1 OR p.Id IS NULL OR b.CurrentCustodyEventId<>i.PreviousCustodyEventId OR p.TripId<>i.TripId OR p.TripOrderId<>i.TripOrderId OR p.DeliveryOrderId<>i.DeliveryOrderId OR p.SourceWarehouseId<>i.SourceWarehouseId OR p.SourceLegalEntityId<>i.SourceLegalEntityId OR i.PreviousCourierUserId<>p.CourierUserId OR i.PreviousCourierUserId<>t.CourierUserId OR i.ConfirmedAtUtc<p.ConfirmedAtUtc))
 THROW 51032, 'Physical receipts require the current draft or predeparture custody source and active named courier.', 1;
 IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=i.StoreId AND j.DeliveryOrderId=i.DeliveryOrderId AND j.Kind=1) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=i.StoreId AND j.DeliveryOrderId=i.DeliveryOrderId AND j.Kind=1 AND (j.SourceWarehouseId<>i.SourceWarehouseId OR j.SourceLegalEntityId<>i.SourceLegalEntityId OR j.CreatedAtUtc>i.ConfirmedAtUtc)))
 THROW 51032, 'A physical receipt follows genuine dispatch evidence from the order source.', 1;
END;
""";
    internal static readonly string CustodyLineSourceSql = """
CREATE TRIGGER [TR_DeliveryCustodyLines_Source] ON [DeliveryCustodyLines] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CustodyEventId JOIN DeliveryTripOrders b ON b.StoreId=e.StoreId AND b.Id=e.TripOrderId LEFT JOIN DeliveryJournalEntries j ON j.Id=i.DispatchJournalEntryId LEFT JOIN DeliveryOrderLines q ON q.Id=i.DeliveryOrderLineId OUTER APPLY (SELECT SUM(r.BaseQuantity) Quantity FROM DeliveryJournalEntries r WHERE r.StoreId=e.StoreId AND r.DeliveryOrderId=e.DeliveryOrderId AND r.DeliveryOrderLineId=i.DeliveryOrderLineId AND r.Kind=2) received WHERE b.CurrentCustodyEventId=e.Id OR j.Id IS NULL OR j.StoreId<>i.StoreId OR j.Kind<>1 OR j.DeliveryOrderId<>e.DeliveryOrderId OR j.DeliveryOrderLineId IS NULL OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId IS NULL OR j.VariantId<>i.VariantId OR j.SourceWarehouseId<>i.SourceWarehouseId OR j.SourceLegalEntityId<>i.SourceLegalEntityId OR q.Id IS NULL OR q.StoreId<>i.StoreId OR q.DeliveryOrderId<>e.DeliveryOrderId OR q.VariantId<>i.VariantId OR i.CumulativeAcceptedReturnAtConfirmation<>ISNULL(received.Quantity,0) OR i.ConfirmedBaseQuantity<>j.BaseQuantity-i.CumulativeAcceptedReturnAtConfirmation OR j.CreatedAtUtc>e.ConfirmedAtUtc OR e.Kind=2 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines p WHERE p.StoreId=i.StoreId AND p.CustodyEventId=e.PreviousCustodyEventId AND p.DeliveryOrderLineId=i.DeliveryOrderLineId AND p.DispatchJournalEntryId=i.DispatchJournalEntryId AND p.VariantId=i.VariantId AND p.SourceWarehouseId=i.SourceWarehouseId AND p.SourceLegalEntityId=i.SourceLegalEntityId AND p.CumulativeAcceptedReturnAtConfirmation<=i.CumulativeAcceptedReturnAtConfirmation AND p.ConfirmedBaseQuantity+p.CumulativeAcceptedReturnAtConfirmation=i.ConfirmedBaseQuantity+i.CumulativeAcceptedReturnAtConfirmation))
 THROW 51033, 'Custody line must preserve its exact dispatch and accepted-return responsibility source.', 1;
 IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CustodyEventId JOIN DeliveryJournalEntries j ON j.Id=i.DispatchJournalEntryId CROSS APPLY (SELECT CONVERT(uniqueidentifier,SUBSTRING(HASHBYTES('SHA2_256',CONVERT(varchar(200),'GaoApp:DeliveryTrip:v1:'+LOWER(CONVERT(varchar(36),e.RootClientRequestId))+':'+CONVERT(varchar(20),e.DeliveryOrderId)+':inventory-dispatch')),1,16)) ChildId) child WHERE e.Kind=1 AND CONVERT(varbinary(max),j.PostingKey)<>CONVERT(varbinary(max),N'dispatch:'+REPLACE(LOWER(CONVERT(nvarchar(36),child.ChildId)),N'-',N'')+N':'+CONVERT(nvarchar(20),i.DeliveryOrderLineId)))
 THROW 51033, 'Initial custody cannot adopt an unrelated historical dispatch.', 1;
END;
""";
    internal const string TripNamespaceSql = "CREATE TRIGGER [TR_DeliveryTripCommandReceipts_Namespace] ON [DeliveryTripCommandReceipts] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCommandReceipts r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.ClientRequestId=i.ClientRequestId) THROW 51034, 'Command GUID is already owned by an order receipt.', 1; END;";
    internal const string OrderNamespaceSql = "CREATE TRIGGER [TR_DeliveryCommandReceipts_TripNamespace] ON [DeliveryCommandReceipts] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryTripCommandReceipts r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.ClientRequestId=i.ClientRequestId) THROW 51034, 'Command GUID is already owned by a trip receipt.', 1; END;";
    private static string Immutable(string table) => "CREATE TRIGGER [TR_" + table + "_Immutable] ON [" + table + "] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;";
    internal sealed record Entry(string Table, string Name, string Sql);
    internal static readonly Entry[] Entries =
    [
        new("DeliveryTrips", "TR_DeliveryTrips_Transition", TripTransitionSql),
        new("DeliveryTripOrders", "TR_DeliveryTripOrders_Source", BindingSourceSql),
        new("DeliveryCustodyEvents", "TR_DeliveryCustodyEvents_Source", CustodySourceSql),
        new("DeliveryCustodyLines", "TR_DeliveryCustodyLines_Source", CustodyLineSourceSql),
        new("DeliveryTripCommandReceipts", "TR_DeliveryTripCommandReceipts_Namespace", TripNamespaceSql),
        new("DeliveryCommandReceipts", "TR_DeliveryCommandReceipts_TripNamespace", OrderNamespaceSql),
        new("DeliveryCustodyEvents", "TR_DeliveryCustodyEvents_Immutable", Immutable("DeliveryCustodyEvents")),
        new("DeliveryCustodyLines", "TR_DeliveryCustodyLines_Immutable", Immutable("DeliveryCustodyLines")),
        new("DeliveryTripRevisions", "TR_DeliveryTripRevisions_Immutable", Immutable("DeliveryTripRevisions")),
        new("DeliveryTripCommandReceipts", "TR_DeliveryTripCommandReceipts_Immutable", Immutable("DeliveryTripCommandReceipts")),
        new("DeliveryTripOutboxMessages", "TR_DeliveryTripOutboxMessages_Immutable", Immutable("DeliveryTripOutboxMessages"))
    ];
    internal static bool TryGetTrigger(string migrationId, SqlOperation operation, out (string Table, DatabaseTriggerSchema Trigger) result)
    {
        result = default;
        if (migrationId != MigrationId) return false;
        var normalized = DeliverySchemaSqlCatalog.NormalizeDefinition(operation.Sql);
        var entry = Entries.SingleOrDefault(x => DeliverySchemaSqlCatalog.NormalizeDefinition(x.Sql) == normalized);
        if (operation.SuppressTransaction || entry is null) throw new InvalidOperationException("Trip migration contains unrecognized trigger SQL.");
        result = (entry.Table, new(DatabaseSchemaNormalization.NormalizeIdentifier(entry.Name), normalized,
            IsDisabled: false, IsNotForReplication: false, UsesAnsiNulls: true, UsesQuotedIdentifier: true));
        return true;
    }
}
