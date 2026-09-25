using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Products;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using System.Diagnostics;
var positional = args.Where(x => !x.StartsWith("--")).ToArray();
var server = positional.ElementAtOrDefault(0) ?? @".\SQLEXPRESS";
var target = positional.ElementAtOrDefault(1) ?? "GaoAppDb";
var source = positional.ElementAtOrDefault(2) ?? "DataGaoStore";
var storeId = int.Parse(positional.ElementAtOrDefault(3) ?? "1");
var cs = new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = target,
    IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, ApplicationName = "GaoAppInvoiceMigrationVerifier" };
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs.ConnectionString, x => x.CommandTimeout(180)).Options;
await using var db = new AppDbContext(options, new ReadTenant(storeId), new ReadUser());
if (args.Contains("--schema-script"))
{
    var sql = db.GetService<IMigrator>().GenerateScript("20260923140000_AddInvoiceStockLegacyDocumentReferences", "20260923160000_AddLegacyInvoiceImport");
    await File.WriteAllTextAsync("scripts/migration/invoices/Apply-Schema.sql", sql);
    Console.WriteLine("Generated Apply-Schema.sql from the EF migration.");
    return;
}
var watch = Stopwatch.StartNew();
var quotedSource = "[" + source.Replace("]", "]]") + "]";
// Database identifier is bracket-quoted above; it cannot be a SQL parameter.
#pragma warning disable EF1002
var expected = (await db.Database.SqlQueryRaw<SourceCounts>($"""
    SELECT (SELECT COUNT_BIG(*) FROM {quotedSource}.dbo.InvoiceHead) Heads,
           (SELECT COUNT_BIG(*) FROM {quotedSource}.dbo.InvoiceDetail) Details,
           (SELECT COUNT_BIG(*) FROM {quotedSource}.dbo.InvoiceHead WHERE NULLIF(LTRIM(RTRIM(InvoiceNumber)),N'') IS NOT NULL) Issued,
           (SELECT COUNT_BIG(*) FROM {quotedSource}.dbo.InvoiceHead WHERE OrderCategoryID=13) Corrections
    """).ToListAsync()).Single();
#pragma warning restore EF1002
var heads = db.InvoiceHeads.Where(x => x.StoreId == storeId && x.LegacySourceId != null);
var headCount = await heads.LongCountAsync();
var detailCount = await db.InvoiceDetails.LongCountAsync(x => x.StoreId == storeId && x.LegacySourceId != null);
var issued = await heads.LongCountAsync(x => x.ProviderStatus == InvoiceProviderStatus.Issued);
var corrections = await heads.LongCountAsync(x => x.LegacyOrderCategoryId == 13 && x.LegacyReadOnly && x.IsLocked && x.ProviderStatus == InvoiceProviderStatus.LocalDraft);
if (headCount != expected.Heads || detailCount != expected.Details || issued != expected.Issued || corrections != expected.Corrections)
    throw new InvalidOperationException("Source/header/detail/status counts differ.");
var read = new InvoiceReadService(new InvoiceRepository(db), new ProductVariantRepository(db));
var page = await read.GetInvoicesAsync(new InvoiceListQueryDto { DisplayMode = InvoiceListDisplayMode.All, PageSize = 20 });
if (!page.IsSuccess || page.Value.TotalItems != await db.InvoiceHeads.CountAsync(x => x.StoreId == storeId))
    throw new InvalidOperationException("Admin invoice list hides imported headers.");
var orphan = await heads.AsNoTracking().Where(x => x.OrderId == null).OrderBy(x => x.Id).FirstOrDefaultAsync();
if (orphan != null)
{
    var detail = await read.GetInvoiceDetailAsync(orphan.Id);
    if (!detail.IsSuccess || detail.Value.LegacySourceId != orphan.LegacySourceId || !detail.Value.LegacyReadOnly || detail.Value.OrderId != null)
        throw new InvalidOperationException("Unlinked archived invoice cannot be read correctly.");
    var search = await read.GetInvoicesAsync(new InvoiceListQueryDto { OrderId = checked((int)orphan.LegacySourceId!.Value) });
    if (!search.IsSuccess || !search.Value.Items.Any(x => x.Id == orphan.Id))
        throw new InvalidOperationException("Legacy OrderID lookup omitted unlinked invoice.");
}
var converted = await db.InvoiceDetails.AsNoTracking().Where(x => x.StoreId == storeId && x.LegacySourceId != null && x.LegacyUnitFactor != 1).FirstOrDefaultAsync();
if (converted != null)
{
    var detail = await read.GetInvoiceDetailAsync(converted.InvoiceHeadId);
    var line = detail.Value.Details.Single(x => x.Id == converted.Id);
    if (line.Quantity != converted.Quantity || line.UnitName != converted.UnitName || line.UnitPrice != converted.UnitPrice)
        throw new InvalidOperationException("Invoice display changed original units/quantity/price.");
}
var expectedStock = await db.InvoiceInputStockSupplementalMovements.AsNoTracking()
    .Where(x => x.StoreId == storeId && x.LegacySourceKey.StartsWith("GSTORE-IIS-V1|"))
    .GroupBy(x => new { x.WarehouseId, x.ProductVariantId })
    .Select(g => new { g.Key.WarehouseId, g.Key.ProductVariantId, Quantity = g.Sum(x => x.QuantityChange), Count = g.Count() }).ToListAsync();
var movements = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(storeId);
var importedStock = movements.Where(x => x.LegacySourceKey?.StartsWith("GSTORE-IIS-V1|", StringComparison.Ordinal) == true).ToList();
var groups = importedStock.GroupBy(x => (x.WarehouseId, x.ProductVariantId)).ToDictionary(x => x.Key, x => x.ToList());
foreach (var stock in expectedStock)
    if (!groups.TryGetValue((stock.WarehouseId, stock.ProductVariantId), out var rows) || rows.Count != stock.Count || rows.Sum(x => x.Change) != stock.Quantity)
        throw new InvalidOperationException("Legacy stock quantities changed.");
var headIds = await heads.Select(x => x.Id).ToListAsync();
var headIdSet = headIds.ToHashSet();
if (movements.Any(x => x.InvoiceHeadId.HasValue && headIdSet.Contains(x.InvoiceHeadId.Value) && x.LegacySourceKey == null))
    throw new InvalidOperationException("Imported issued invoice was counted again in stock.");
var linkedOutbound = importedStock.Count(x => x.LegacySourceKey!.StartsWith("GSTORE-IIS-V1|X|", StringComparison.Ordinal) && x.InvoiceHeadId.HasValue);
var outbound = importedStock.Count(x => x.LegacySourceKey!.StartsWith("GSTORE-IIS-V1|X|", StringComparison.Ordinal));
if (linkedOutbound != outbound) throw new InvalidOperationException("Historical outbound is missing a link to the new invoice.");
Console.WriteLine(JsonSerializer.Serialize(new {
    Result = "INVOICE_AND_STOCK_READ_PASS", Target = target, Source = source, StoreId = storeId,
    ImportedHeads = headCount, ImportedDetails = detailCount, Issued = issued, Drafts = headCount - issued,
    ReadOnlyCorrections = corrections, AdminListTotal = page.Value.TotalItems,
    UnlinkedOrders = await heads.CountAsync(x => x.OrderId == null),
    StockMovements = importedStock.Count, StockVariants = expectedStock.Count, LinkedOutbound = linkedOutbound,
    StockNegativeVariants = expectedStock.Count(x => x.Quantity < 0),
    SampleUnlinkedInvoiceId = orphan?.Id, SampleUnlinkedLegacyOrderId = orphan?.LegacySourceId,
    ElapsedMilliseconds = watch.ElapsedMilliseconds
}, new JsonSerializerOptions { WriteIndented = true }));
sealed class SourceCounts { public long Heads { get; set; } public long Details { get; set; } public long Issued { get; set; } public long Corrections { get; set; } }
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
