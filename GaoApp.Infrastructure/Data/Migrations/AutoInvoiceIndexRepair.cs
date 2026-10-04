using System.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed record AutoInvoiceIndexRepairResult(string Status, IReadOnlyList<string> Indexes);

/// <summary>Repairs only the three known missing nonunique FK indexes. No migration or data writes.</summary>
public sealed class AutoInvoiceIndexRepair(AppDbContext db)
{
    private static readonly (string Record, string Name, string Sql)[] Repairs =
    [
        ("index|dbo.autoinvoiceoperations|ix_autoinvoiceoperations_invoiceheadid|invoiceheadid:0||0|0|0|0|-",
            "IX_AutoInvoiceOperations_InvoiceHeadId", "CREATE INDEX [IX_AutoInvoiceOperations_InvoiceHeadId] ON [dbo].[AutoInvoiceOperations] ([InvoiceHeadId]);"),
        ("index|dbo.autoinvoiceoperationsources|ix_autoinvoiceoperationsources_autoinvoiceoperationid|autoinvoiceoperationid:0||0|0|0|0|-",
            "IX_AutoInvoiceOperationSources_AutoInvoiceOperationId", "CREATE INDEX [IX_AutoInvoiceOperationSources_AutoInvoiceOperationId] ON [dbo].[AutoInvoiceOperationSources] ([AutoInvoiceOperationId]);"),
        ("index|dbo.autoinvoiceoperationsources|ix_autoinvoiceoperationsources_invoiceheadid|invoiceheadid:0||0|0|0|0|-",
            "IX_AutoInvoiceOperationSources_InvoiceHeadId", "CREATE INDEX [IX_AutoInvoiceOperationSources_InvoiceHeadId] ON [dbo].[AutoInvoiceOperationSources] ([InvoiceHeadId]);")
    ];

    private SqlServerDatabaseBaselinePreflight Preflight() => new(db, new EfCoreDatabaseMigrationCatalog(db),
        new SqlServerDatabaseObjectInventoryReader(db), new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));

    public async Task<AutoInvoiceIndexRepairResult> RunAsync(bool apply, CancellationToken ct = default)
    {
        var preflight = await Preflight().InspectAsync(ct);
        if (preflight.State is DatabaseCompatibilityState.CurrentBaseline or DatabaseCompatibilityState.SupportedPendingUpgrade)
            return new("ALREADY_COMPATIBLE", []);
        if (preflight.SafeReasonCode != "StructuralSchemaMismatch") throw new DatabaseCompatibilityException(preflight);

        var report = await new DatabaseSchemaInspection(db).ReadAsync(ct);
        var names = Validate(report);
        if (!apply) return new("REPAIR_PLAN_VERIFIED", names);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Recheck on the transaction connection. Any unexpected schema change aborts all index creation.
        report = await new DatabaseSchemaInspection(db).ReadAsync(ct);
        names = Validate(report);
        foreach (var repair in Repairs.Where(x => report.ExpectedOnly.Contains(x.Record)))
            await db.Database.ExecuteSqlRawAsync(repair.Sql, ct);
        var after = await Preflight().InspectAsync(ct);
        if (!after.IsAllowed) throw new DatabaseCompatibilityException(after);
        var historyAfter = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (!report.AppliedMigrations.SequenceEqual(historyAfter, StringComparer.Ordinal))
            throw new MigratorConfigurationException("Migration history changed during repair. Transaction rolled back.");
        await transaction.CommitAsync(ct);
        return new("AUTO_INVOICE_INDEX_REPAIR_VERIFIED", names);
    }

    private static string[] Validate(DatabaseSchemaInspectionReport report)
    {
        if (report.Status != "StructuralSchemaMismatch" || report.ActualOnly.Count != 0 || report.ExpectedOnly.Count == 0
            || report.ExpectedOnly.Any(x => !Repairs.Any(r => r.Record == x))
            || !report.AppliedMigrations.Contains("20260924100000_AddAutoInvoiceIssuance"))
            throw new MigratorConfigurationException("Repair refused: differences are not limited to the three reviewed AutoInvoice indexes. No changes applied.");
        return Repairs.Where(x => report.ExpectedOnly.Contains(x.Record)).Select(x => x.Name).ToArray();
    }
}
