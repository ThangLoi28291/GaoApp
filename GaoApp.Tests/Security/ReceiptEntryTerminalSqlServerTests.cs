using GaoApp.Application.Interfaces.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Purchases;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptEntryTerminalSqlServerTests
{
    [Fact]
    public async Task Receipt_creation_captures_employee_and_terminal_and_later_edits_preserve_origin()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var employee = await app.LoginAsync(account);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
        var terminal = await db.POSTerminals.SingleAsync(x => x.Id == store.TerminalId);
        var terminalName = terminal.Name;
        var terminalCode = terminal.Code;
        var created = await employee.JsonAsync(HttpMethod.Post, "/admin/api/stock-documents/receipts", new
        {
            legalEntityId = warehouse.LegalEntityId, warehouseId = store.WarehouseId,
            documentTitle = "Hàng nhập sáng", directReceiptReason = "Nhà phân phối giao",
            // Unknown client properties must never override server context.
            entryTerminalId = app.Stores[1].TerminalId, entryTerminalName = "Giả mạo"
        });
        var id = created.GetProperty("id").GetInt32();
        var doc = await db.StockDocuments.SingleAsync(x => x.Id == id);
        Assert.Equal(account.UserId, doc.CreatedBy);
        Assert.Equal(store.TerminalId, doc.EntryTerminalId);
        Assert.Equal(terminalName, doc.EntryTerminalName);
        Assert.Equal(terminalCode, doc.EntryTerminalCode);
        terminal.Name = "Quầy đã đổi tên";
        doc.DocumentTitle = "Đã sửa tên phiếu";
        doc.SubmittedByUserId = account.UserId;
        await db.SaveChangesAsync();
        var list = await employee.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        var row = Assert.Single(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("E2E test user", row.GetProperty("createdByName").GetString());
        Assert.Equal(terminalName, row.GetProperty("entryTerminalName").GetString());
        Assert.Equal(terminalCode, row.GetProperty("entryTerminalCode").GetString());
        using var other = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var otherList = await other.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        Assert.DoesNotContain(otherList.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Both_creation_repositories_validate_terminal_store_and_leave_legacy_unknown()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        StockDocument New(string no) => new() { StoreId = store.StoreId,
            WarehouseId = store.WarehouseId, DocumentNo = no };
        var legacy = New("LEGACY-NO-ORIGIN");
        db.Add(legacy);
        var valid = New("PO-ENTRY");
        await new PurchaseReceivingWorkbenchRepository(db, new Pos(store.StoreId, store.TerminalId)).AddDocumentAsync(valid);
        var foreign = New("FOREIGN-TERMINAL");
        await new StockDocumentRepository(db, pos: new Pos(store.StoreId, app.Stores[1].TerminalId)).AddAsync(foreign);
        var wrongStore = New("WRONG-STORE");
        await new PurchaseReceivingWorkbenchRepository(db, new Pos(app.Stores[1].StoreId, app.Stores[1].TerminalId)).AddDocumentAsync(wrongStore);
        var noContext = New("NO-CONTEXT");
        await new StockDocumentRepository(db).AddAsync(noContext);
        await db.SaveChangesAsync();
        Assert.Equal(store.TerminalId, valid.EntryTerminalId);
        Assert.False(string.IsNullOrWhiteSpace(valid.EntryTerminalName));
        foreach (var item in new[] { legacy, foreign, wrongStore, noContext })
        {
            Assert.Null(item.EntryTerminalId);
            Assert.Null(item.EntryTerminalName);
            Assert.Null(item.EntryTerminalCode);
        }
        var rows = await new StockDocumentRepository(db).GetReceiptListAsync();
        var old = Assert.Single(rows, x => x.Id == legacy.Id);
        Assert.Null(old.EntryTerminalName);
        Assert.Null(old.CreatedByName);
    }

    private sealed record Pos(int StoreId, int TerminalId) : ICurrentPOSContext
    {
        public int? UserId => null;
        public bool IsAvailable => StoreId > 0 && TerminalId > 0;
    }
}
