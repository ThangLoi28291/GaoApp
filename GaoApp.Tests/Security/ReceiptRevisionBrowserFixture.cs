using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

/// <summary>
/// Browser-only receipt prepared inside FullApplicationFixture's disposable SQL database.
/// Revision evidence comes from the same HTTP workflow used by employees and approvers.
/// </summary>
internal static class ReceiptRevisionBrowserFixture
{
    internal sealed record Seed(int ReceiptId, int ChangedLineId, int SecondChangedLineId,
        int AddedLineId, int UnchangedLineId, decimal SavedPrice, int TotalLineCount);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var original = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int unchangedLineId, changedLineId, secondChangedLineId, changedUnitId, secondChangedUnitId;
        int addedVariantId, addedUnitId;
        const decimal savedPrice = 12345m;

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var template = await db.ProductVariants.Include(x => x.Product)
                .SingleAsync(x => x.Id == store.VariantId);
            var receipt = await db.StockDocuments.Include(x => x.Lines)
                .SingleAsync(x => x.Id == original.ReceiptId);
            var unitsByName = (await db.Units.ToListAsync())
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            Unit ResolveUnit(string name)
            {
                if (unitsByName.TryGetValue(name, out var existing)) return existing;
                var created = new Unit { StoreId = store.StoreId, Code = $"UX-{unitsByName.Count + 1:00}", Name = name };
                unitsByName.Add(name, created);
                return created;
            }
            receipt.DocumentNo = "PN-UX-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            receipt.DirectReceiptReason = "Nhập bổ sung hàng bán tại cửa hàng";
            receipt.Note = "Đã đối chiếu hàng thực nhận; kiểm tra các dòng sửa và bổ sung trước khi duyệt.";

            var pack = receipt.Lines.Single(x => x.ProductUnitConversionId == original.PackId);
            var carton = receipt.Lines.Single(x => x.ProductUnitConversionId == original.CartonId);
            unchangedLineId = pack.Id;
            changedLineId = carton.Id;
            changedUnitId = carton.UnitId!.Value;
            template.Price = 4500m;
            template.CostPrice = 3100m;
            SetAmounts(pack, 6m, 12000m);
            SetAmounts(carton, 2m, 72000m);

            // Eleven different catalog items make the real Razor table long enough to test
            // scrolling, filtering, unchanged prices and labels beside a long product name.
            var samples = new (string Name, string Unit, decimal Quantity, decimal Cost, decimal Sale)[]
            {
                ("Mì ăn liền Hảo Hảo tôm chua cay 75g", "Gói", 30m, 3500m, 4500m),
                ("Nước mắm Nam Ngư 3 trong 1 chai 500ml", "Chai", 12m, 23800m, 29000m),
                ("Dầu ăn thực vật Tường An chai 1 lít", "Chai", 10m, 43500m, 52000m),
                ("Đường tinh luyện Biên Hòa túi 1kg", "Túi", 20m, 22800m, 28000m),
                ("Bánh quy bơ Cosy hộp thiếc 382g", "Hộp", 8m, 78000m, 95000m),
                ("Cà phê hòa tan G7 3 trong 1 hộp 21 gói", "Hộp", 12m, 54500m, 66000m),
                ("Nước rửa chén Sunlight chanh túi 750g", "Túi", 15m, 25200m, 32000m),
                ("Khăn giấy lụa Pulppy 3 lớp gói 150 tờ", "Gói", 24m, 13700m, 18000m),
                ("Bột giặt OMO Matic cửa trước hương lavender túi 2,7kg", "Túi", 6m, 128000m, 149000m),
                ("Ngũ cốc dinh dưỡng nguyên cám ít đường bổ sung canxi và vitamin cho cả gia đình gói 500g", "Gói", 9m, 68500m, 65000m),
                ("Nước suối La Vie chai 500ml", "Chai", 48m, 3300m, 5000m)
            };
            var extraLines = new List<StockDocumentLine>();
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                var unit = ResolveUnit(sample.Unit);
                var product = new Product
                {
                    StoreId = store.StoreId, Name = sample.Name, Alias = $"receipt-ux-{i + 1:00}",
                    CategoryId = template.Product.CategoryId, SupplierId = template.Product.SupplierId,
                    BaseUnit = unit, BasePrice = sample.Sale, IsActive = true, IsSellable = true
                };
                var variant = new ProductVariant
                {
                    StoreId = store.StoreId, Product = product, Sku = $"UX-{i + 1:00}",
                    ProductVariantName = sample.Name, CostPrice = sample.Cost, Price = sample.Sale
                };
                var conversion = new ProductUnitConversion
                {
                    StoreId = store.StoreId, ProductVariant = variant, Unit = unit,
                    Factor = 1m, IsBaseUnit = true, IsDefaultForSale = true, Price = sample.Sale
                };
                db.ProductVariantUnitBarcodes.Add(new ProductVariantUnitBarcode
                {
                    StoreId = store.StoreId, ProductUnitConversion = conversion,
                    Barcode = $"893000000{i + 1:0000}", BarcodeType = BarcodeType.Internal,
                    IsPrimary = true, IsActive = true
                });
                var line = new StockDocumentLine
                {
                    LineNo = i + 3, ProductVariant = variant, ProductUnitConversion = conversion,
                    Unit = unit, UnitNameSnapshot = sample.Unit, ProductNameSnapshot = sample.Name,
                    SkuSnapshot = variant.Sku, BarcodeSnapshot = $"893000000{i + 1:0000}", Factor = 1m
                };
                SetAmounts(line, sample.Quantity, sample.Cost);
                receipt.Lines.Add(line);
                extraLines.Add(line);
            }

            var addedUnit = ResolveUnit("Gói");
            var addedProduct = new Product
            {
                StoreId = store.StoreId, Name = "Bánh gạo One One vị tôm nướng gói 150g",
                Alias = "receipt-ux-added", CategoryId = template.Product.CategoryId,
                SupplierId = template.Product.SupplierId, BaseUnit = addedUnit, BasePrice = 22000m
            };
            var addedVariant = new ProductVariant
            {
                StoreId = store.StoreId, Product = addedProduct, Sku = "UX-BO-SUNG",
                ProductVariantName = addedProduct.Name, CostPrice = 17500m, Price = 22000m
            };
            db.ProductUnitConversions.Add(new ProductUnitConversion
            {
                StoreId = store.StoreId, ProductVariant = addedVariant, Unit = addedUnit,
                Factor = 1m, IsBaseUnit = true, IsDefaultForSale = true, Price = 22000m
            });
            receipt.TotalAmount = receipt.Lines.Sum(x => x.LineTotal);
            await db.SaveChangesAsync();
            secondChangedLineId = extraLines[9].Id;
            secondChangedUnitId = extraLines[9].UnitId!.Value;
            addedVariantId = addedVariant.Id;
            addedUnitId = addedUnit.Id;
        }

        using var manager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Approve));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Create,
            PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Delete));
        var url = $"/admin/api/stock-documents/{original.ReceiptId}";
        async Task<JsonElement> StateAsync() => await manager.JsonAsync(HttpMethod.Get, url);
        async Task TransitionAsync(FullApplicationFixture.Client actor, string action, string note)
        {
            var state = await StateAsync();
            await actor.JsonAsync(HttpMethod.Post, url + "/" + action,
                new { rowVersion = state.GetProperty("rowVersion").GetString(), note });
        }

        await TransitionAsync(employee, "submit-approval", "Đã nhận hàng, gửi quản lý duyệt lần đầu");
        var beforePrices = await StateAsync();
        var unchanged = beforePrices.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt32() == unchangedLineId);
        await manager.JsonAsync(HttpMethod.Post, url + "/price-draft", new
        {
            rowVersion = beforePrices.GetProperty("rowVersion").GetString(),
            lines = new[] { new { stockDocumentLineId = unchangedLineId,
                rowVersion = unchanged.GetProperty("rowVersion").GetString(), unitPriceBeforeVat = savedPrice } }
        });
        await TransitionAsync(manager, "reject", "Thiếu một thùng sữa và bánh gạo; kiểm tra lại số lượng ngũ cốc.");
        var beforeFirstCorrection = await StateAsync();
        await employee.JsonAsync(HttpMethod.Put, url + $"/lines/{changedLineId}", new
        {
            rowVersion = beforeFirstCorrection.GetProperty("rowVersion").GetString(),
            lineRowVersion = beforeFirstCorrection.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == changedLineId).GetProperty("rowVersion").GetString(),
            unitId = changedUnitId, quantity = 3m, note = "Nhập bù 1 thùng vừa giao bổ sung"
        });
        var beforeSecondCorrection = await StateAsync();
        await employee.JsonAsync(HttpMethod.Put, url + $"/lines/{secondChangedLineId}", new
        {
            rowVersion = beforeSecondCorrection.GetProperty("rowVersion").GetString(),
            lineRowVersion = beforeSecondCorrection.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == secondChangedLineId).GetProperty("rowVersion").GetString(),
            unitId = secondChangedUnitId, quantity = 12m, note = "Đã kiểm đếm lại: nhận đủ 12 gói"
        });
        var added = await employee.JsonAsync(HttpMethod.Post, url + "/lines", new
        {
            productVariantId = addedVariantId, unitId = addedUnitId, quantity = 12m,
            unitCost = 17500m, note = "Hàng thiếu đã được nhà cung cấp giao bù"
        });
        var addedLineId = added.GetProperty("lineId").GetInt32();
        await TransitionAsync(employee, "submit-approval", "Đã sửa số lượng và bổ sung hàng thiếu, nhờ quản lý duyệt lại.");

        // Employee endpoints deliberately clear commercial prices. Set the newly
        // received item's draft through the approver after resubmission, when the
        // physical correction round is already frozen.
        var resubmitted = await StateAsync();
        var addedLine = resubmitted.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt32() == addedLineId);
        await manager.JsonAsync(HttpMethod.Post, url + "/price-draft", new
        {
            rowVersion = resubmitted.GetProperty("rowVersion").GetString(),
            lines = new[] { new { stockDocumentLineId = addedLineId,
                rowVersion = addedLine.GetProperty("rowVersion").GetString(), unitPriceBeforeVat = 17500m } }
        });

        var final = await StateAsync();
        var review = final.GetProperty("revisionReview");
        var finalLines = final.GetProperty("lines");
        if (review.GetProperty("changedLineCount").GetInt32() != 2 ||
            review.GetProperty("addedLineCount").GetInt32() != 1 || finalLines.GetArrayLength() != 14 ||
            finalLines.EnumerateArray().Any(x => x.GetProperty("unitPriceBeforeVat").GetDecimal() <= 0m) ||
            finalLines.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == unchangedLineId)
                .GetProperty("unitPriceBeforeVat").GetDecimal() != savedPrice)
            throw new InvalidOperationException("Browser fixture did not produce the expected correction round and saved price. " +
                JsonSerializer.Serialize(new { receiptId = original.ReceiptId, changedLineId, secondChangedLineId,
                    addedLineId, unchangedLineId, savedPrice, revisionReview = review,
                    lines = finalLines.EnumerateArray().Select(x => new {
                        id = x.GetProperty("id").GetInt32(),
                        quantity = x.GetProperty("quantity").GetDecimal(),
                        price = x.GetProperty("unitPriceBeforeVat").GetDecimal(),
                        change = x.GetProperty("revisionChangeKind") }) }));

        return new(original.ReceiptId, changedLineId, secondChangedLineId, addedLineId,
            unchangedLineId, savedPrice, finalLines.GetArrayLength());
    }

    private static void SetAmounts(StockDocumentLine line, decimal quantity, decimal price)
    {
        line.Quantity = quantity;
        line.BaseQuantity = quantity * line.Factor;
        line.UnitCost = price;
        line.UnitPriceBeforeVat = price;
        line.UnitPriceAfterVat = price;
        line.LineTotal = quantity * price;
    }
}
