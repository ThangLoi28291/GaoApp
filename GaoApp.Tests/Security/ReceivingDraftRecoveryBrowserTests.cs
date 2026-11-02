using System.Diagnostics;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceivingDraftRecoveryBrowserTests
{
    [Fact]
    public async Task Phone_preserves_ten_items_and_unsent_input_across_browser_restart_and_reauthentication()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var account = await app.AddAccountAsync(store, "*");
        int receiptId, unitId; var choices = new List<object>(); var variantIds = new List<int>(); string inventoryBefore;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var template = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            unitId = template.Product.BaseUnitId;
            var unitName = await db.Units.Where(x => x.Id == unitId).Select(x => x.Name).SingleAsync();
            for (var i = 1; i <= 10; i++)
            {
                var variant = new ProductVariant { StoreId = store.StoreId, ProductId = template.ProductId,
                    Sku = "RECOVERY-" + Guid.NewGuid().ToString("N"), ProductVariantName = $"Món nhận hàng điện thoại {i}", Price = 1000, IsActive = true };
                db.Add(variant); await db.SaveChangesAsync();
                variantIds.Add(variant.Id);
                var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                    UnitId = unitId, Factor = 1, Price = 1000, IsBaseUnit = true, IsDefaultForSale = true, IsActive = true };
                db.Add(conversion); await db.SaveChangesAsync();
                choices.Add(new { productVariantId = variant.Id, productUnitConversionId = conversion.Id, unitId,
                    productName = variant.ProductVariantName, unitName, factor = 1, price = 1000 });
            }
            var receipt = new StockDocument { StoreId = store.StoreId, DocumentNo = "MOBILE-RECOVERY-" + Guid.NewGuid().ToString("N")[..8],
                WarehouseId = store.WarehouseId, SupplierId = template.Product.SupplierId, ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Kiểm tra lưu nháp điện thoại", IsMerchandisePaid = true };
            db.Add(receipt); await db.SaveChangesAsync(); receiptId = receipt.Id;
            inventoryBefore = JsonSerializer.Serialize(await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue }).ToArrayAsync());
        }
        var root = FullApplicationFixture.SourceRoot(); var output = Path.Combine(root, "Logs", "receiving-recovery-20261101"); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "receiving-recovery.browser.cjs"));
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}",
            user = account.Name, password = account.Password, terminal = store.TerminalId, receiptId, unitId, choices, output }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), log);
        Assert.True(process.ExitCode == 0, log);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var document = await check.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
        Assert.Equal(StockDocumentStatus.Draft, document.Status); Assert.Equal(10, document.Lines.Count);
        Assert.Equal(4m, document.Lines.Single(x => x.ProductVariantId == variantIds[0]).Quantity);
        Assert.Equal(7m, document.Lines.Single(x => x.ProductVariantId == variantIds[9]).Quantity);
        Assert.All(document.Lines.Where(x => x.ProductVariantId != variantIds[0] && x.ProductVariantId != variantIds[9]), x => Assert.Equal(1m, x.Quantity));
        Assert.Empty(await check.StockDocumentProvisionalItems.Where(x => x.StockDocumentId == receiptId && !x.IsDeleted && x.Status == StockDocumentProvisionalItemStatus.Unresolved).ToArrayAsync());
        Assert.Equal(inventoryBefore, JsonSerializer.Serialize(await check.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue }).ToArrayAsync()));
    }
}
