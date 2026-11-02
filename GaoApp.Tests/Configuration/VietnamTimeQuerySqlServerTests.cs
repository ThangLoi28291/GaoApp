using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Rewards;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class VietnamTimeQuerySqlServerTests
{
    [Fact]
    public async Task Local_day_filters_include_Vietnam_midnight_and_exclude_next_midnight_in_SQL()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var customer = new Customer { StoreId = seed.StoreId, Name = "Vietnam clock customer", Code = "VN-CLOCK" };
        var invoice = new InvoiceHead { StoreId = seed.StoreId, IsAutoInvoiceGroup = true };
        db.AddRange(customer, invoice);
        await db.SaveChangesAsync();

        var timestamps = new[]
        {
            new DateTime(2026, 10, 10, 16, 59, 59, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 11, 16, 59, 59, DateTimeKind.Utc),
            new DateTime(2026, 10, 11, 17, 0, 0, DateTimeKind.Utc)
        };
        var transactions = new List<InventoryTransaction>();
        var counts = new List<StockCountDocument>();
        var vouchers = new List<CustomerRewardVoucher>();
        var logs = new List<InvoiceIntegrationLog>();
        for (var i = 0; i < timestamps.Length; i++)
        {
            transactions.Add(new InventoryTransaction
            {
                StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.ProductVariantId,
                ReferenceType = InventoryReferenceType.StockDocument, ReferenceId = "VN-CLOCK-" + i,
                TransactionType = InventoryTransactionType.PurchaseReceipt, QuantityChange = 1,
                BeforeQty = i, AfterQty = i + 1, OccurredAtUtc = timestamps[i]
            });
            counts.Add(new StockCountDocument { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId,
                DocumentNo = "VN-CLOCK-" + i, DocumentDate = timestamps[i] });
            vouchers.Add(new CustomerRewardVoucher { StoreId = seed.StoreId, CustomerId = customer.Id,
                VoucherCode = "VN-CLOCK-" + i, Value = 10000, RequiredAmount = 100000, IssuedAtUtc = timestamps[i] });
            logs.Add(new InvoiceIntegrationLog { StoreId = seed.StoreId, InvoiceHeadId = invoice.Id,
                ActionType = InvoiceIntegrationActionType.IssueInvoice, StartedAtUtc = timestamps[i] });
        }
        db.AddRange(transactions); db.AddRange(counts); db.AddRange(vouchers); db.AddRange(logs);
        await db.SaveChangesAsync();
        var day = new DateTime(2026, 10, 11);

        var ledger = await new InventoryLedgerIndexReadRepository(db).QueryAsync(seed.StoreId,
            new InventoryLedgerIndexQueryRequest { FromDate = day, ToDate = day }, null);
        Assert.Equal(2, ledger.TotalItems);
        Assert.Equal(transactions.Skip(1).Take(2).Select(x => x.Id).Order(), ledger.Items.Select(x => x.TransactionId).Order());

        var stockCounts = await new StockCountIndexReadRepository(db).QueryAsync(seed.StoreId,
            new StockCountIndexQueryRequest { FromDate = day, ToDate = day });
        Assert.Equal(2, stockCounts.TotalItems);
        Assert.Equal(counts.Skip(1).Take(2).Select(x => x.Id).Order(), stockCounts.Items.Select(x => x.DocumentId).Order());

        var rewardVouchers = await new RewardVoucherIndexReadRepository(db).QueryAsync(seed.StoreId,
            new RewardVoucherIndexQueryRequest { FromDate = day, ToDate = day });
        Assert.Equal(2, rewardVouchers.TotalItems);
        Assert.Equal(vouchers.Skip(1).Take(2).Select(x => x.Id).Order(), rewardVouchers.Items.Select(x => x.VoucherId).Order());

        var repository = new InvoiceIntegrationLogRepository(db);
        var invoiceLogs = await repository.QueryAsync(new InvoiceIntegrationLogQueryDto { FromDate = day, ToDate = day });
        Assert.Equal(2, invoiceLogs.Total);
        Assert.Equal(logs.Skip(1).Take(2).Select(x => x.Id).Order(), invoiceLogs.Items.Select(x => x.Id).Order());
        Assert.Equal(2, (await repository.GetLogsForDashboardAsync(day, day)).Count);
    }
}
