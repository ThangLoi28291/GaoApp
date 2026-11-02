using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only called by the standalone browser probe, against its owned disposable GUID database.
internal static class ReceivingListBrowserFixture
{
    internal sealed record Seed(FullApplicationFixture.Account Employee, FullApplicationFixture.Account Denied,
        int OtherLegalEntityId, int OtherWarehouseId, int[] ActiveIds);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var employee = await app.AddAccountAsync(store, AppPermissions.InventoryStockDocumentCreate);
        var denied = await app.AddAccountAsync(store, AppPermissions.InventoryStockDocumentView);
        int[] activeIds;
        int otherLegalEntityId, otherWarehouseId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var original = await db.Warehouses.Include(x => x.LegalEntity).SingleAsync();
            original.Name = "Kho trung tâm";
            original.LegalEntity.Name = "HKD Trung Tâm";
            var supplier = await db.Suppliers.SingleAsync();
            supplier.Name = "Nhà phân phối Thành Công & Đối tác";
            var legal = new LegalEntity { StoreId = store.StoreId, Code = "PHONE-HKD", Name = "HKD Chi Nhánh", LegalName = "HKD Chi Nhánh", SalePriority = 2 };
            db.Add(legal); await db.SaveChangesAsync();
            var warehouse = new Warehouse { StoreId = store.StoreId, LegalEntityId = legal.Id, Code = "PHONE-KHO", Name = "Kho chi nhánh phía Đông" };
            db.Add(warehouse); await db.SaveChangesAsync();
            legal.DefaultWarehouseId = warehouse.Id;
            var po = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "PHONE-PO", Title = "Đơn mua kiểm tra điện thoại",
                SupplierId = supplier.Id, LegalEntityId = legal.Id, ExpectedWarehouseId = warehouse.Id };
            db.Add(po); await db.SaveChangesAsync();
            var receipts = Enumerable.Range(0, 24).Select(i => new StockDocument {
                StoreId = store.StoreId, WarehouseId = i % 2 == 0 ? original.Id : warehouse.Id,
                DocumentNo = $"PHONE-{i:D3}", DocumentDate = new DateTime(2026, 10, 25),
                DocumentTitle = i == 23 ? "Nhập bổ sung sữa dinh dưỡng và hàng tiêu dùng cho chương trình cuối tháng <kiểm tra> & đối chiếu"
                    : $"Nhập hàng buổi sáng đợt {i + 1}",
                SupplierId = i == 0 ? null : supplier.Id, PurchaseOrderId = i % 4 == 0 ? po.Id : null,
                Status = i % 3 == 0 ? StockDocumentStatus.Draft : i % 3 == 1 ? StockDocumentStatus.PendingApproval : StockDocumentStatus.Rejected,
                Lines = i == 0 ? [] : Enumerable.Range(1, 3).Select(n => new StockDocumentLine {
                    LineNo = n, ProductVariantId = store.VariantId, Quantity = n, BaseQuantity = n,
                    Factor = 1, UnitCost = 10, LineTotal = n * 10, ProductNameSnapshot = "Hàng kiểm tra"
                }).ToList()
            }).ToList();
            db.AddRange(receipts);
            db.AddRange(new StockDocument { StoreId = store.StoreId, WarehouseId = original.Id, DocumentNo = "EXCLUDED-CONFIRMED", Status = StockDocumentStatus.Confirmed, ConfirmedLegalEntityId = original.LegalEntityId },
                new StockDocument { StoreId = store.StoreId, WarehouseId = original.Id, DocumentNo = "EXCLUDED-CANCELLED", Status = StockDocumentStatus.Cancelled });
            var deleted = new StockDocument { StoreId = store.StoreId, WarehouseId = original.Id, DocumentNo = "EXCLUDED-DELETED" };
            db.Add(deleted); await db.SaveChangesAsync();
            deleted.IsDeleted = true; await db.SaveChangesAsync();
            activeIds = receipts.Select(x => x.Id).ToArray(); otherLegalEntityId = legal.Id; otherWarehouseId = warehouse.Id;
        }
        var other = app.Stores[1];
        await using (var db = app.Database.CreateTenantContext(other.StoreId))
        {
            db.Add(new StockDocument { StoreId = other.StoreId, WarehouseId = other.WarehouseId, DocumentNo = "FOREIGN-RECEIPT" });
            await db.SaveChangesAsync();
        }
        return new(employee, denied, otherLegalEntityId, otherWarehouseId, activeIds);
    }

    internal static async Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            documents = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.Status, x.WarehouseId, x.DocumentTitle, x.RowVersion, x.IsDeleted }).ToListAsync(),
            lines = await db.StockDocumentLines.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StockDocumentId, x.Quantity, x.BaseQuantity, x.UnitCost, x.IsDeleted }).ToListAsync(),
            balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.WarehouseId, x.OnHandQty }).ToListAsync(),
            postings = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().CountAsync(),
            payables = await db.PurchasePayables.IgnoreQueryFilters().AsNoTracking().CountAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.Title, x.Url, x.SortOrder, x.IsActive }).ToListAsync()
        });
    }
}
