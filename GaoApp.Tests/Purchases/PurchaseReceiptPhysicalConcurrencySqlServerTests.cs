using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Purchases;

[Collection("SqlServerConcurrency")]
public sealed class PurchaseReceiptPhysicalConcurrencySqlServerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_price_line_edit_or_delete_cannot_commit_after_concurrent_submission(bool delete)
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "physical-race");
        AppDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options, tenant, new Actor());
        }
        static StockDocumentService Service(AppDbContext db, TenantContext tenant)
        {
            var repository = new StockDocumentRepository(db);
            return new(repository, null!, null!, null!, new InventoryUnitResolver(repository),
                null!, null!, null!, null!, tenant, null!, new Actor());
        }

        int documentId, lineId, unitId;
        await using (var setup = Context())
        {
            unitId = await setup.Units.Select(x => x.Id).SingleAsync();
            var conversion = new ProductUnitConversion { StoreId = seed.StoreId,
                ProductVariantId = seed.ProductVariantId, UnitId = unitId, Factor = 1m,
                IsBaseUnit = true, IsDefaultForSale = true, IsActive = true };
            var document = new StockDocument { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId,
                DocumentNo = "ZERO-PRICE-RACE", ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Khác", Status = StockDocumentStatus.Draft };
            document.Lines.Add(new() { ProductVariantId = seed.ProductVariantId, ProductUnitConversion = conversion,
                UnitId = unitId, UnitNameSnapshot = "Cái", ProductNameSnapshot = "Hàng thực nhận",
                LineNo = 1, Factor = 1m, Quantity = 2m, BaseQuantity = 2m, UnitCost = 0m, LineTotal = 0m });
            setup.Add(document); await setup.SaveChangesAsync();
            documentId = document.Id; lineId = document.Lines.Single().Id;
        }
        var submitted = false;
        var interceptor = new BeforeSave(async () =>
        {
            await using var other = Context();
            var header = await other.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
            await Service(other, tenant).SubmitForApprovalAsync(documentId, "Đã nhận đủ", Convert.ToBase64String(header.RowVersion));
            submitted = true;
        });
        await using var editor = Context(interceptor);
        var shown = await editor.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
        var shownLine = await editor.StockDocumentLines.AsNoTracking().SingleAsync(x => x.Id == lineId);
        var documentVersion = Convert.ToBase64String(shown.RowVersion);
        var lineVersion = Convert.ToBase64String(shownLine.RowVersion);

        if (delete)
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Service(editor, tenant).DeleteLineAsync(
                documentId, lineId, new DeleteStockDocumentLineRequest { RowVersion = documentVersion, LineRowVersion = lineVersion }));
        else
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Service(editor, tenant).UpdateLineAsync(
                documentId, lineId, new UpdateStockDocumentLineRequest { RowVersion = documentVersion,
                    LineRowVersion = lineVersion, UnitId = unitId, Quantity = 9m }));

        Assert.True(submitted);
        await using var check = Context();
        var final = await check.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == documentId);
        Assert.Equal(StockDocumentStatus.PendingApproval, final.Status);
        Assert.Equal(2m, Assert.Single(final.Lines).Quantity);
        Assert.Equal(0m, final.TotalAmount);
        Assert.Equal(1, await check.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == documentId &&
            x.EventType == PurchaseReceiptAuditEventType.SubmittedForApproval));
        Assert.False(await check.PurchaseReceiptAuditEvents.AnyAsync(x => x.StockDocumentId == documentId &&
            (x.EventType == PurchaseReceiptAuditEventType.PhysicalLineChanged || x.EventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted)));
    }

    private sealed class BeforeSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool invoked;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!invoked) { invoked = true; await action(); }
            return result;
        }
    }
    private sealed class Actor : ICurrentUser
    {
        public int? UserId => 101;
        public string? UserName => "physical-concurrency-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
