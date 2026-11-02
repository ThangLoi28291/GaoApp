using System.Diagnostics;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Services.Invoices;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceInputStockPagingTests
{
    [Fact]
    public async Task Half_million_movements_share_prepared_balances_and_ledger_without_full_page_allocations()
    {
        const int count = 500_000;
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, count).Select(i => new InvoiceInputStockMovement
        {
            Key = i.ToString("D8"), WarehouseId = 1, WarehouseName = "Kho chính",
            ProductVariantId = i % 10000 + 1, ProductName = $"Sữa tươi {i % 10000:D5}",
            Code = $"SKU-{i % 10000}", DateUtc = start.AddMinutes(i),
            Kind = i % 3 == 0 ? "increase" : "decrease", Change = i % 3 == 0 ? 2 : -1,
            Before = 999, After = 999
        }).ToArray();
        var service = new InvoiceInputStockReadService(new Repository(rows), new Store());
        var balance = await service.GetPageAsync(new());
        Assert.Equal(10000, balance.TotalItems);
        Assert.Equal(count / 3 + 1, balance.IncreaseCount);
        Assert.Equal(count - balance.IncreaseCount, balance.DecreaseCount);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var ledger = await service.GetPageAsync(new() { View = "ledger", Page = 2 });
        Assert.Equal(count, ledger.TotalItems);
        Assert.Equal((count - 21).ToString("D8"), ledger.Movements[0].Key);
        var original = rows[count - 21];
        var before = rows.Where(x => x.ProductVariantId == original.ProductVariantId && x.DateUtc < original.DateUtc).Sum(x => x.Change);
        Assert.Equal(before, ledger.Movements[0].Before);
        Assert.Equal(before + original.Change, ledger.Movements[0].After);
        for (var i = 1; i <= 10; i++)
        {
            await service.GetPageAsync(new() { Page = i });
            await service.GetPageAsync(new() { View = "ledger", Page = i });
        }
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), $"Warm paging took {timer.Elapsed}.");
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 5_000_000, "Paging copied the full movement history.");
        Assert.All(rows, row => { Assert.Equal(999, row.Before); Assert.Equal(999, row.After); });
    }

    [Fact]
    public async Task Concurrent_filters_and_response_edits_do_not_change_cached_stock()
    {
        var day = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        InvoiceInputStockMovement[] rows =
        [
            new() { Key = "opening", WarehouseId = 1, ProductVariantId = 1, ProductName = "Sữa tươi", Change = 10, IsOpening = true, Kind = "increase", DateUtc = day.AddDays(-1) },
            new() { Key = "xml", WarehouseId = 1, ProductVariantId = 1, ProductName = "Sữa tươi", Change = 20, Kind = "increase", DateUtc = day, XmlSeries = "1C26TAA", XmlNumber = "0000123" },
            new() { Key = "sale", WarehouseId = 1, ProductVariantId = 1, ProductName = "Sữa tươi", Change = -4, Kind = "decrease", DateUtc = day, SourceCode = "HD-1" },
            new() { Key = "hold", WarehouseId = 1, ProductVariantId = 1, ProductName = "Sữa tươi", Held = 3, Kind = "hold", DateUtc = day.AddHours(1) }
        ];
        var service = new InvoiceInputStockReadService(new Repository(rows), new Store());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var ledger = await service.GetPageAsync(new() { View = "ledger", FromDate = day, ToDate = day, Kind = "decrease" });
            var row = Assert.Single(ledger.Movements);
            Assert.Equal(30, row.Before); Assert.Equal(26, row.After);
            row.Before = -100;
            var balance = Assert.Single((await service.GetPageAsync(new() { Keyword = "HD-1" })).Balances);
            Assert.Equal(23, balance.Available); balance.Opening = -100;
        })));
        Assert.Equal(23, Assert.Single((await service.GetPageAsync(new() { Keyword = "sua tuoi" })).Balances).Available);
        Assert.Equal("xml", Assert.Single((await service.GetPageAsync(new() { View = "ledger", Keyword = "1C26TAA/0000123" })).Movements).Key);
        Assert.All(rows, x => { Assert.Equal(0, x.Before); Assert.Equal(0, x.After); });
    }

    [Fact]
    public void Reference_search_preserves_selected_display_names_and_complete_balances()
    {
        InvoiceInputStockMovement[] rows =
        [
            new() { Key = "1", WarehouseId = 1, ProductVariantId = 1, ProductName = "Z cũ", Kind = "increase", Change = 10 },
            new() { Key = "2", WarehouseId = 1, ProductVariantId = 2, ProductName = "A cũ", Kind = "increase", Change = 20 },
            new() { Key = "3", WarehouseId = 1, ProductVariantId = 1, ProductName = "A mới", Kind = "decrease", Change = -1, SourceCode = "HD-1" },
            new() { Key = "4", WarehouseId = 1, ProductVariantId = 2, ProductName = "Z mới", Kind = "decrease", Change = -2, SourceCode = "HD-1" }
        ];
        var page = InvoiceInputStockReadService.BuildPage(rows, new() { Keyword = "HD-1" });
        Assert.Equal(new[] { "A mới", "Z mới" }, page.Balances.Select(x => x.ProductName));
        Assert.Equal(new[] { 9m, 18m }, page.Balances.Select(x => x.Available));
    }

    [Fact]
    public async Task New_repository_snapshot_replaces_prepared_balances()
    {
        var repository = new Repository([new() { Key = "1", Kind = "increase", Change = 10, WarehouseId = 1, ProductVariantId = 1 }]);
        var service = new InvoiceInputStockReadService(repository, new Store());
        Assert.Equal(10, Assert.Single((await service.GetPageAsync(new())).Balances).Available);
        repository.Rows = [new() { Key = "2", Kind = "decrease", Change = -5, WarehouseId = 1, ProductVariantId = 1 }];
        var balance = Assert.Single((await service.GetPageAsync(new() { Kind = "negative" })).Balances);
        Assert.Equal(-5, balance.Available);
    }

    [Theory]
    [InlineData("sua tuoi")]
    [InlineData("ST-220 HD 8120")]
    [InlineData("hd 8120 1C26TVK")]
    [InlineData("1C26TVK/0000123")]
    [InlineData("legacy-reference")]
    public void Prepared_search_keeps_accents_cross_field_phrases_and_compact_XML_references(string keyword)
    {
        var row = new InvoiceInputStockMovement { Key = "1", ProductVariantId = 1, WarehouseId = 1,
            ProductName = "Sữa tươi", Code = "ST-220", SourceCode = "HĐ 8120", XmlSeries = "1C26TVK",
            XmlNumber = "0000123", LegacySourceKey = "legacy-reference", Change = 10, Kind = "increase" };
        var page = InvoiceInputStockReadService.BuildPage([row], new() { Keyword = keyword });
        Assert.Equal(10m, Assert.Single(page.Balances).Available);
    }

    private sealed class Store : ICurrentStore { public int StoreId => 1; }
    private sealed class Repository(IReadOnlyList<InvoiceInputStockMovement> rows) : IInvoiceInputStockReadRepository
    {
        public IReadOnlyList<InvoiceInputStockMovement> Rows { get; set; } = rows;
        public Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(int storeId, CancellationToken ct = default) => Task.FromResult(Rows);
        public Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(int storeId, IReadOnlyCollection<int>? productVariantIds,
            IReadOnlyCollection<int>? warehouseIds, CancellationToken ct = default) => Task.FromResult(Rows);
    }
}
