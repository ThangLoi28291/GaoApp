using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public sealed class TenantScopeRepositoryTests
{
    [Theory]
    [InlineData("purchase-request")]
    [InlineData("purchase-order")]
    [InlineData("stock-count")]
    [InlineData("stock-transfer")]
    public async Task Next_line_includes_own_deleted_lines_and_never_uses_another_stores_lines(string kind)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new TenantContext();
        await using var db = new AppDbContext(options, tenant, new Actor());
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning))
        {
            db.AddRange(Line(kind, 1, 101, 3), Line(kind, 1, 101, 7, deleted: true),
                Line(kind, 2, 101, 99), Line(kind, 2, 202, 90));
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        tenant.SetStore(1, "one");
        Assert.Equal(8, await Next(db, kind, 101));
        Assert.Equal(1, await Next(db, kind, 202));
        tenant.Clear();
        await Assert.ThrowsAsync<TenantDataScopeException>(() => Next(db, kind, 101));
    }

    [Fact]
    public async Task Store_id_on_entities_outside_BaseStoreEntity_is_filtered_and_verified_on_writes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new TenantContext();
        await using var db = new AppDbContext(options, tenant, new Actor());
        var foreign = new AuditLog { StoreId = 2, Summary = "foreign" };
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning))
        {
            db.AuditLogs.AddRange(new AuditLog { StoreId = 1, Summary = "own" }, foreign);
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        tenant.SetStore(1, "one");
        Assert.Equal("own", (await db.AuditLogs.SingleAsync()).Summary);
        db.AuditLogs.Update(new AuditLog { Id = foreign.Id, StoreId = 1, Summary = "forged" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AuditLogs.Remove(new AuditLog { Id = foreign.Id, StoreId = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AuditLogs.Add(new AuditLog { StoreId = 2 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static Task<int> Next(AppDbContext db, string kind, int id) => kind switch
    {
        "purchase-request" => new PurchaseRequestRepository(db).GetNextLineNumberAsync(id),
        "purchase-order" => new PurchaseOrderRepository(db).GetNextLineNumberAsync(id),
        "stock-count" => new StockCountRepository(db).GetNextLineNoAsync(id),
        "stock-transfer" => new StockTransferRepository(db).GetNextLineNoAsync(id),
        _ => throw new ArgumentException(kind)
    };

    private static BaseStoreEntity Line(string kind, int store, int parent, int number, bool deleted = false)
    {
        BaseStoreEntity row = kind switch
        {
            "purchase-request" => new PurchaseRequestLine { PurchaseRequestId = parent, LineNo = number },
            "purchase-order" => new PurchaseOrderLine { PurchaseOrderId = parent, LineNo = number },
            "stock-count" => new StockCountLine { StockCountDocumentId = parent, LineNo = number },
            "stock-transfer" => new StockTransferLine { StockTransferDocumentId = parent, LineNo = number,
                UnitNameSnapshot = "unit", ProductNameSnapshot = "synthetic" },
            _ => throw new ArgumentException(kind)
        };
        row.StoreId = store; row.IsDeleted = deleted; row.RowVersion = new byte[8];
        return row;
    }

    private sealed class Actor : ICurrentUser
    {
        public int? UserId => null; public string? UserName => null;
        public int? TerminalId => null; public string? TerminalCode => null; public bool IsAuthenticated => false;
    }
}
