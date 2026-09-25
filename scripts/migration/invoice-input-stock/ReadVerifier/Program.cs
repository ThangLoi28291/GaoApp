using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var server = args.ElementAtOrDefault(0) ?? @".\SQLEXPRESS";
var database = args.ElementAtOrDefault(1) ?? "GaoAppDb";
var storeId = int.Parse(args.ElementAtOrDefault(2) ?? "1");
var warehouseId = int.Parse(args.ElementAtOrDefault(3) ?? "1");
var cs = new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = database,
    IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true,
    ApplicationName = "GaoAppInvoiceStockReadVerifier" };
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs.ConnectionString).Options;
await using var db = new AppDbContext(options, new ReadTenant(storeId), new ReadUser());
var expected = await db.Database.SqlQuery<ExpectedBalance>($"""
    SELECT ProductVariantId, COUNT_BIG(*) AS MovementCount, SUM(QuantityChange) AS Balance,
           SUM(CASE WHEN QuantityChange>0 THEN QuantityChange ELSE 0 END) AS Received,
           -SUM(CASE WHEN QuantityChange<0 THEN QuantityChange ELSE 0 END) AS Issued
    FROM dbo.InvoiceInputStockSupplementalMovements
    WHERE StoreId={storeId} AND WarehouseId={warehouseId} AND IsDeleted=0
      AND LegacySourceKey LIKE N'GSTORE-IIS-V1|%'
    GROUP BY ProductVariantId
    """).ToListAsync();
if (expected.Count == 0) throw new InvalidOperationException("No imported test data to verify.");
var watch = Stopwatch.StartNew();
var all = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(storeId);
var readMs = watch.ElapsedMilliseconds;
var imported = all.Where(x => x.WarehouseId == warehouseId && x.LegacySourceKey?.StartsWith("GSTORE-IIS-V1|", StringComparison.Ordinal) == true).ToList();
var references = await db.InvoiceInputStockSupplementalMovements.AsNoTracking()
    .Where(x => x.StoreId == storeId && x.WarehouseId == warehouseId && x.LegacySourceKey.StartsWith("GSTORE-IIS-V1|"))
    .Select(x => new { x.LegacySourceKey, x.LegacyOrderId, x.LegacyInvoiceNumber, x.LegacyInvoiceSymbol })
    .ToDictionaryAsync(x => x.LegacySourceKey);
var legacyHeads = await db.InvoiceHeads.AsNoTracking().Where(x => x.StoreId == storeId && x.LegacySourceId != null)
    .Select(x => new { x.LegacySourceId, x.Id }).ToDictionaryAsync(x => x.LegacySourceId!.Value, x => x.Id);
foreach (var row in imported)
{
    var source = references[row.LegacySourceKey!];
    int? linkedHead = source.LegacyOrderId.HasValue && legacyHeads.TryGetValue(source.LegacyOrderId.Value, out var headId) ? headId : null;
    if (row.LegacyOrderId != source.LegacyOrderId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        || row.LegacyInvoiceNumber != source.LegacyInvoiceNumber || row.LegacyInvoiceSymbol != source.LegacyInvoiceSymbol
        || row.InvoiceHeadId != linkedHead || row.StockDocumentId != null || string.IsNullOrWhiteSpace(row.OperationLabel))
        throw new InvalidOperationException($"Legacy provenance projection mismatch: {row.LegacySourceKey}");
}
var groups = imported.GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key, x => x.ToList());
if (groups.Count != expected.Count) throw new InvalidOperationException("Projection omitted or added imported variants.");
foreach (var item in expected)
{
    if (!groups.TryGetValue(item.ProductVariantId, out var rows) || rows.Count != item.MovementCount
        || rows.Sum(x => x.Change) != item.Balance || rows.Sum(x => x.Held) != 0
        || rows.Where(x => x.Change > 0).Sum(x => x.Change) != item.Received
        || -rows.Where(x => x.Change < 0).Sum(x => x.Change) != item.Issued)
        throw new InvalidOperationException($"Projection mismatch for variant {item.ProductVariantId}.");
}
watch.Restart();
var page = InvoiceInputStockReadService.BuildPage(all, new InvoiceInputStockQuery { WarehouseId = warehouseId, PageSize = 100 });
var pageMs = watch.ElapsedMilliseconds;
var fullBalances = all.Where(x => x.WarehouseId == warehouseId).GroupBy(x => x.ProductVariantId)
    .ToDictionary(x => x.Key, x => x.Sum(v => v.Change - v.Held));
if (page.TotalItems != fullBalances.Count || page.NegativeCount != fullBalances.Count(x => x.Value < 0))
    throw new InvalidOperationException("Management page totals mismatch.");
foreach (var row in page.Balances)
    if (row.Available != fullBalances[row.ProductVariantId]) throw new InvalidOperationException("Management page balance mismatch.");
var sample = expected.OrderBy(x => x.Balance).First();
var ledger = InvoiceInputStockReadService.BuildPage(all, new InvoiceInputStockQuery {
    View = "ledger", WarehouseId = warehouseId, ProductVariantId = sample.ProductVariantId, PageSize = 100
});
if (ledger.Movements.Any(x => x.After != x.Before + x.Change)) throw new InvalidOperationException("Ledger continuity failed.");
Console.WriteLine(JsonSerializer.Serialize(new {
    Result = "READ_PROJECTION_PASS", Database = database, StoreId = storeId, WarehouseId = warehouseId,
    ImportedMovements = imported.Count, ImportedVariants = groups.Count,
    ImportedNegativeVariants = expected.Count(x => x.Balance < 0), OtherRuntimeMovements = all.Count - imported.Count,
    ManagementProducts = page.TotalItems, ManagementNegativeCount = page.NegativeCount,
    RepositoryReadMilliseconds = readMs, BuildPageMilliseconds = pageMs,
    SampleLedgerVariant = sample.ProductVariantId,
    ProvenanceVerifiedMovements = imported.Count,
    ProvenanceSamples = imported.Where(x => x.LegacySourceKey == "GSTORE-IIS-V1|I|937641"
        || x.LegacySourceKey == "GSTORE-IIS-V1|X|521596")
        .Select(x => new { x.LegacySourceKey, x.SourceCode, x.LegacyOrderId, x.LegacyInvoiceNumber, x.LegacyInvoiceSymbol, x.OperationLabel })
}, new JsonSerializerOptions { WriteIndented = true }));

sealed class ReadTenant(int storeId) : ITenantContext
{
    public int? StoreId => storeId;
    public bool IsHostAdmin => false;
    public string? Subdomain => null;
}
sealed class ReadUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
sealed class ExpectedBalance
{
    public int ProductVariantId { get; set; }
    public long MovementCount { get; set; }
    public decimal Balance { get; set; }
    public decimal Received { get; set; }
    public decimal Issued { get; set; }
}
