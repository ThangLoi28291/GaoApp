using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

/// <summary>Real approval workspace data, restricted to FullApplicationFixture's disposable database.</summary>
internal static class ReceiptApprovalWorkspaceBrowserFixture
{
    internal sealed record Seed(int ReceiptId, int ChangedLineId, int SecondChangedLineId,
        int AddedLineId, int UnchangedLineId, decimal SavedPrice, int TotalLineCount, int TaxId);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var revision = await ReceiptRevisionBrowserFixture.PrepareAsync(app, store);
        int historyId, historyLineId, taxId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.Include(x => x.Lines)
                .SingleAsync(x => x.Id == revision.ReceiptId);
            receipt.IsMerchandisePaid = false;
            var tax = new Tax { StoreId = store.StoreId, Code = "UX-VAT-8", Name = "VAT 8% (kiểm thử)", Rate = 8m };
            db.Taxes.Add(tax);
            var source = receipt.Lines.Single(x => x.Id == revision.UnchangedLineId);
            var history = new StockDocument
            {
                StoreId = store.StoreId, DocumentNo = "PN-UX-HISTORY-" + Guid.NewGuid().ToString("N")[..8],
                DocumentDate = DateTime.UtcNow.AddDays(-3), WarehouseId = receipt.WarehouseId,
                SupplierId = receipt.SupplierId, ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Dữ liệu kiểm thử lần nhập trước", IsMerchandisePaid = true,
                Lines = new List<StockDocumentLine>
                {
                    new()
                    {
                        ProductVariantId = source.ProductVariantId,
                        ProductUnitConversionId = source.ProductUnitConversionId, UnitId = source.UnitId,
                        UnitNameSnapshot = source.UnitNameSnapshot, ProductNameSnapshot = source.ProductNameSnapshot,
                        SkuSnapshot = source.SkuSnapshot, BarcodeSnapshot = source.BarcodeSnapshot,
                        LineNo = 1, Factor = source.Factor, Quantity = 1m, BaseQuantity = source.Factor,
                        UnitPriceBeforeVat = 12000m, UnitPriceAfterVat = 12000m, UnitCost = 12000m, LineTotal = 12000m
                    }
                }
            };
            db.StockDocuments.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id; historyLineId = history.Lines.Single().Id; taxId = tax.Id;
        }

        // Establish purchase-price history through the real workflow, so the final browser
        // approval must acknowledge an actual last-purchase difference. This also posts
        // its own stock once; the browser harness captures the resulting baseline.
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = $"/admin/api/stock-documents/{historyId}";
        var state = await manager.JsonAsync(HttpMethod.Get, url);
        await manager.JsonAsync(HttpMethod.Post, url + "/submit-approval", new
        { rowVersion = state.GetProperty("rowVersion").GetString(), note = "Lịch sử giá cho kiểm thử giao diện" });
        state = await manager.JsonAsync(HttpMethod.Get, url);
        await manager.JsonAsync(HttpMethod.Post, url + "/approve-commercial", new
        {
            rowVersion = state.GetProperty("rowVersion").GetString(),
            supplierId = state.GetProperty("supplierId").GetInt32(), isMerchandisePaid = false,
            acceptPriceVariance = true, waitForInputInvoice = false,
            lines = new[] { new { stockDocumentLineId = historyLineId, unitPriceBeforeVat = 12000m } }
        });
        return new(revision.ReceiptId, revision.ChangedLineId, revision.SecondChangedLineId,
            revision.AddedLineId, revision.UnchangedLineId, revision.SavedPrice, revision.TotalLineCount, taxId);
    }
}
