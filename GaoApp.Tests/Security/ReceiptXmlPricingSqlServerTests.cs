using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Purchases;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptXmlPricingSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/pricing/xml-mappings";

    [Fact]
    public async Task Pricing_mapping_is_scoped_versioned_and_remembers_explicit_units_without_receipt_line_matching_or_posting()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var purchaseOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Approve));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var version = await ReceiptVersionAsync(app, store, seed.ReceiptId);
        var body = Mapping(seed, version, seed.DetailIds[0], store.VariantId, seed.CartonId);
        var before = await ProtectedSnapshotAsync(app, store, seed.ReceiptId);
        using (var response = await viewer.Http.PostAsJsonAsync(Url(seed.ReceiptId), body)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await purchaseOnly.Http.PostAsJsonAsync(Url(seed.ReceiptId), body)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await foreign.Http.PostAsJsonAsync(Url(seed.ReceiptId), body)) Assert.False(response.IsSuccessStatusCode);
        using (var request = new HttpRequestMessage(HttpMethod.Post, Url(seed.ReceiptId)) { Content = JsonContent.Create(body) })
        {
            request.Headers.Add("RequestVerificationToken", "invalid");
            using var response = await manager.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, "AAAAAAAAAAA=", seed.DetailIds[0], store.VariantId, seed.CartonId)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), new
               { receiptRowVersion = version, inputInvoiceHeadId = seed.OtherHeadId, inputInvoiceDetailId = seed.DetailIds[0], productVariantId = store.VariantId, productUnitConversionId = seed.CartonId }))
            Assert.False(response.IsSuccessStatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[0], app.Stores[1].VariantId, seed.CartonId)))
            Assert.False(response.IsSuccessStatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[0], store.VariantId, 0)))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ProtectedSnapshotAsync(app, store, seed.ReceiptId));

        // XML "TH" is an explicit supplier alias for the selected catalog conversion "Thùng".
        var confirmed = await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), body);
        Assert.Equal("Confirmed", confirmed.GetProperty("stateName").GetString());
        Assert.Equal(seed.CartonId, confirmed.GetProperty("productUnitConversionId").GetInt32());
        Assert.Equal(24m, confirmed.GetProperty("confirmedFactor").GetDecimal());
        Assert.Equal(72m, confirmed.GetProperty("derivedBaseQuantity").GetDecimal());
        var mappingVersion = confirmed.GetProperty("mappingRowVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(mappingVersion));
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), body);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[0], store.VariantId, seed.PackId)))
            Assert.False(response.IsSuccessStatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[0], store.VariantId, seed.CartonId, "AAAAAAAAAAA=")))
            Assert.False(response.IsSuccessStatusCode);
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[1], store.VariantId, seed.PackId));
        var invoices = await manager.JsonAsync(HttpMethod.Get, $"/admin/api/stock-documents/{seed.ReceiptId}/input-invoices");
        var details = Assert.Single(invoices.EnumerateArray()).GetProperty("details").EnumerateArray().ToArray();
        var rememberedRepeat = Assert.Single(details, x => x.GetProperty("id").GetInt32() == seed.DetailIds[2]).GetProperty("itemCatalogMapping");
        Assert.Equal("Confirmed", rememberedRepeat.GetProperty("stateName").GetString());
        Assert.Equal(seed.PackId, rememberedRepeat.GetProperty("productUnitConversionId").GetInt32());
        Assert.Equal(before, await ProtectedSnapshotAsync(app, store, seed.ReceiptId));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(2, await db.InputInvoiceItemCatalogMaps.CountAsync());
            Assert.Equal(2, await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed));
            Assert.Empty(await db.StockDocumentLineInputInvoiceMaps.ToListAsync());
        }
        var nextReceiptId = await NextInvoiceReceiptAsync(app, store, seed);
        var nextInvoices = await manager.JsonAsync(HttpMethod.Get, $"/admin/api/stock-documents/{nextReceiptId}/input-invoices");
        var nextMapping = Assert.Single(Assert.Single(nextInvoices.EnumerateArray()).GetProperty("details").EnumerateArray()).GetProperty("itemCatalogMapping");
        Assert.Equal("Confirmed", nextMapping.GetProperty("stateName").GetString());
        Assert.Equal(seed.CartonId, nextMapping.GetProperty("productUnitConversionId").GetInt32());
        Assert.Equal(mappingVersion, nextMapping.GetProperty("mappingRowVersion").GetString());
        Assert.Equal(before, await ProtectedSnapshotAsync(app, store, seed.ReceiptId));

        foreach (var status in new[] { StockDocumentStatus.Draft, StockDocumentStatus.Confirmed })
        {
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
            {
                var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
                receipt.Status = status;
                receipt.ConfirmedLegalEntityId = status == StockDocumentStatus.Confirmed
                    ? await db.Warehouses.Where(x => x.Id == receipt.WarehouseId).Select(x => x.LegalEntityId).SingleAsync()
                    : null;
                await db.SaveChangesAsync();
            }
            var currentVersion = await ReceiptVersionAsync(app, store, seed.ReceiptId);
            var protectedBefore = await ProtectedSnapshotAsync(app, store, seed.ReceiptId);
            using var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Mapping(seed, currentVersion, seed.DetailIds[0], store.VariantId, seed.CartonId, mappingVersion));
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(protectedBefore, await ProtectedSnapshotAsync(app, store, seed.ReceiptId));
        }
    }

    [Fact]
    public async Task Xml_import_preserves_exact_money_repeat_lines_manual_programs_and_local_drafts_until_explicit_apply()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        var account = await app.AddAccountAsync(store, "*");
        using var manager = await app.LoginAsync(account);
        var version = await ReceiptVersionAsync(app, store, seed.ReceiptId);
        // A supplier code/unit already remembered on a previous bill can be added in bulk.
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Mapping(seed, version, seed.DetailIds[1], store.VariantId, seed.PackId));
        var before = await PostingSnapshotAsync(app, store, seed.ReceiptId);
        await CheckBrowserAsync(app, account, seed);
        Assert.Equal(before, await PostingSnapshotAsync(app, store, seed.ReceiptId));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Empty(await db.StockDocumentLineInputInvoiceMaps.ToListAsync());
        Assert.Equal(PurchaseReceiptPricingPlanState.Applied, (await db.PurchaseReceiptPricingPlans.SingleAsync()).State);
        Assert.Equal(StockDocumentStatus.PendingApproval, (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        Assert.Contains(await db.StockDocumentLines.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync(), x => x.UnitPriceBeforeVat != 10m);
        Assert.Equal(2, await db.InputInvoiceItemCatalogMaps.CountAsync());
    }

    private sealed record Seed(int ReceiptId, int PackId, int CartonId, int BaseConversionId, int PackUnitId, int CartonUnitId, int HeadId, int OtherHeadId, int[] DetailIds);

    private static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var receiptSeed = await PricingAllocationTestData.Seed(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptSeed.ReceiptId);
        foreach (var line in receipt.Lines)
        {
            line.Quantity = line.Factor == 24m ? 3m : 3.25m;
            line.BaseQuantity = line.Quantity * line.Factor;
            line.LineTotal = line.Quantity * line.UnitPriceBeforeVat;
        }
        receipt.SubtotalBeforeVat = receipt.TotalAmount = receipt.Lines.Sum(x => x.LineTotal);
        await db.SaveChangesAsync();
        var baseUnitId = await db.ProductVariants.Where(x => x.Id == store.VariantId).Select(x => x.Product.BaseUnitId).SingleAsync();
        var baseConversion = await db.ProductUnitConversions.FirstOrDefaultAsync(x => x.ProductVariantId == store.VariantId && x.UnitId == baseUnitId && x.Factor == 1m);
        if (baseConversion is null)
        {
            baseConversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId, UnitId = baseUnitId, Factor = 1m, IsBaseUnit = true, IsActive = true };
            db.Add(baseConversion); await db.SaveChangesAsync();
        }
        InputInvoiceHead Head(string number) => new()
        {
            StoreId = store.StoreId, SellerName = "XML pricing supplier", SellerTaxCode = "0312770607", BuyerTaxCode = "0101234567",
            InvoiceSeries = "C26XML", InvoiceNumber = number, InvoiceDate = new(2026, 10, 4), ResolvedSupplierId = receipt.SupplierId,
            SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved,
            TotalBeforeTax = 175000m, TotalPaymentAmount = 175000m
        };
        var head = Head(Guid.NewGuid().ToString("N"));
        var other = Head(Guid.NewGuid().ToString("N"));
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head); InputInvoiceIdentityPolicy.ApplyRequiredIdentity(other);
        InputInvoiceDetail Detail(int line, string code, string unit, decimal qty, decimal price, decimal amount) => new()
        {
            LineNo = line, SupplierItemCode = code, NormalizedSupplierItemCode = code.ToUpperInvariant(),
            ItemName = line == 5 ? "Chiết khấu trên XML" : "Tên nhà cung cấp - sữa tươi", NormalizedItemName = line == 5 ? "CHIẾT KHẤU TRÊN XML" : "TÊN NHÀ CUNG CẤP - SỮA TƯƠI",
            UnitName = unit, NormalizedUnitName = unit.ToUpperInvariant(), Quantity = qty, UnitPrice = price, LineAmount = amount, VatRate = "8%"
        };
        head.Details.Add(Detail(1, "MILK-TH", "TH", 3m, 33333.33m, 100000m));
        head.Details.Add(Detail(2, "MILK-PACK", "Lốc", 2m, 25000m, 50000m));
        head.Details.Add(Detail(3, "MILK-PACK", "Lốc", 1m, 25000m, 25000m));
        head.Details.Add(Detail(4, "GIFT-HOP", "Hộp", 1m, 0m, 0m));
        head.Details.Add(Detail(5, "DISCOUNT", "", 0m, 0m, -1000m));
        db.AddRange(head, other); await db.SaveChangesAsync();
        db.Add(new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = head.Id });
        await db.SaveChangesAsync();
        var units = await db.ProductUnitConversions.Where(x => x.Id == receiptSeed.PackId || x.Id == receiptSeed.CartonId).Select(x => new { x.Id, x.UnitId }).ToListAsync();
        return new(receipt.Id, receiptSeed.PackId, receiptSeed.CartonId, baseConversion.Id,
            units.Single(x => x.Id == receiptSeed.PackId).UnitId, units.Single(x => x.Id == receiptSeed.CartonId).UnitId,
            head.Id, other.Id, head.Details.OrderBy(x => x.LineNo).Select(x => x.Id).ToArray());
    }

    private static object Mapping(Seed seed, string version, int detailId, int variantId, int conversionId, string? mappingVersion = null)
        => new { receiptRowVersion = version, inputInvoiceHeadId = seed.HeadId, inputInvoiceDetailId = detailId,
            productVariantId = variantId, productUnitConversionId = conversionId, mappingRowVersion = mappingVersion };

    private static async Task<int> NextInvoiceReceiptAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, Seed seed)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var original = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
        var receipt = new StockDocument { StoreId = store.StoreId, DocumentNo = "XML-NEXT-" + Guid.NewGuid().ToString("N")[..10],
            Type = StockDocumentType.Receipt, Status = StockDocumentStatus.PendingApproval, ReceiptSource = PurchaseReceiptSource.Direct,
            WarehouseId = store.WarehouseId, SupplierId = original.SupplierId,
            Lines = [new StockDocumentLine { ProductVariantId = store.VariantId, ProductUnitConversionId = seed.CartonId,
                UnitId = seed.CartonUnitId, UnitNameSnapshot = "Thùng", Factor = 24m, Quantity = 1m, BaseQuantity = 24m, LineNo = 1,
                ProductNameSnapshot = "Sữa tươi không đường", UnitPriceBeforeVat = 10m, UnitCost = 10m, LineTotal = 10m }] };
        var invoice = new InputInvoiceHead { StoreId = store.StoreId, SellerName = "XML pricing supplier", SellerTaxCode = "0312770607",
            BuyerTaxCode = "0101234567", InvoiceSeries = "C26XML", InvoiceNumber = Guid.NewGuid().ToString("N"), InvoiceDate = new(2026, 10, 4),
            ResolvedSupplierId = original.SupplierId, SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved,
            Details = [new InputInvoiceDetail { LineNo = 1, SupplierItemCode = "MILK-TH", NormalizedSupplierItemCode = "MILK-TH",
                ItemName = "Tên trên hóa đơn lần sau", NormalizedItemName = "TÊN TRÊN HÓA ĐƠN LẦN SAU", UnitName = "TH", NormalizedUnitName = "TH",
                Quantity = 1m, UnitPrice = 33000m, LineAmount = 33000m }] };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
        db.AddRange(receipt, invoice); await db.SaveChangesAsync();
        db.Add(new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = invoice.Id });
        await db.SaveChangesAsync();
        return receipt.Id;
    }

    private static async Task<string> ReceiptVersionAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int id)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return Convert.ToBase64String(await db.StockDocuments.Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync());
    }

    private static async Task<string> ProtectedSnapshotAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int id, bool includeReconciliation = true)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new
        {
            Header = await db.StockDocuments.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Status, x.SupplierId, x.RowVersion, x.SubtotalBeforeVat, x.TotalAmount, x.ApprovedAtUtc, x.ConfirmedAtUtc }).SingleAsync(),
            Lines = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.RowVersion, x.Quantity, x.BaseQuantity, x.UnitCost, x.UnitPriceBeforeVat, x.LineTotal }).ToListAsync(),
            Associations = await db.StockDocumentInputInvoiceMaps.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.InputInvoiceHeadId, x.IsDeleted }).ToListAsync(),
            LineMapCount = await db.StockDocumentLineInputInvoiceMaps.CountAsync(),
            Reconciliation = includeReconciliation ? await db.StockDocumentInputInvoiceReconciliations.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.EvidenceFingerprint }).ToListAsync() : null,
            Inventory = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToListAsync(),
            Payables = await db.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync(),
            PlanCount = await db.PurchaseReceiptPricingPlans.CountAsync()
        });
    }

    private static async Task<string> PostingSnapshotAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int id)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new
        {
            Header = await db.StockDocuments.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Status, x.SupplierId, x.WarehouseId, x.DocumentDate, x.ApprovedAtUtc, x.ConfirmedAtUtc }).SingleAsync(),
            Lines = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ProductVariantId, x.UnitId, x.ProductUnitConversionId, x.Factor, x.Quantity, x.BaseQuantity }).ToListAsync(),
            Inventory = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToListAsync(),
            Payables = await db.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync()
        });
    }

    private static async Task CheckBrowserAsync(FullApplicationFixture app, FullApplicationFixture.Account account, Seed seed)
    {
        var root = FullApplicationFixture.SourceRoot();
        var evidence = Environment.GetEnvironmentVariable("GAOAPP_XML_PRICING_EVIDENCE_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "GaoAppXmlPricingBrowser_" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "receipt-xml-pricing.browser.cjs"));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{account.Store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
            terminalId = account.Store.TerminalId, receiptId = seed.ReceiptId, variantId = account.Store.VariantId,
            headId = seed.HeadId, detailIds = seed.DetailIds, cartonUnitId = seed.CartonUnitId, packUnitId = seed.PackUnitId, evidenceRoot = evidence
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
