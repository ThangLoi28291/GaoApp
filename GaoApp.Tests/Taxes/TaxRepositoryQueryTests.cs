using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Taxes;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Taxes;

public sealed class TaxRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_status_and_store_before_total_and_paging()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            seedContext.Set<Tax>().AddRange(
                NewTax(1, "VAT-ACTIVE", "Thuế bán hàng", 10m, isActive: true),
                NewTax(1, "VAT-INACTIVE", "Thuế bán hàng cũ", 8m, isActive: false),
                NewTax(1, "ZERO", "Không chịu thuế", 0m, isActive: true));

            await seedContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.Set<Tax>().Add(
                NewTax(2, "VAT-OTHER", "Thuế bán hàng Store khác", 10m, isActive: true));

            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new TaxRepository(readContext);

        var (items, total) = await InvokeStatusPagedAsync(
            repository,
            storeId: 1,
            search: "Thuế bán hàng",
            status: true,
            page: 1,
            pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Code.Should().Be("VAT-ACTIVE");
    }

    [Fact]
    public async Task GetPagedAsync_should_search_vietnamese_names_without_diacritics_on_sql_server()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var store = new Store
        {
            Name = "Tax accent search test",
            SubDomain = "tax-accent-search-test",
            SubDomainNormalized = "TAX-ACCENT-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        context.Set<Tax>().Add(
            NewTax(store.Id, "VAT-ANH-DUONG", "Đặc thuế Ánh Dương", 10m, isActive: true));

        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "tax-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new TaxRepository(readContext);
        var (items, total) = await InvokeStatusPagedAsync(
            repository,
            storeId: store.Id,
            search: "dac thue anh duong",
            status: true,
            page: 1,
            pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Name.Should().Be("Đặc thuế Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_same_store_non_deleted_rows()
    {
        var options = CreateOptions();
        int deletedId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var deleted = NewTax(1, "REMOVED", "Đã xóa", 5m, isActive: true);
            seedContext.Set<Tax>().AddRange(
                NewTax(1, "ACTIVE-1", "Hoạt động một", 10m, isActive: true),
                NewTax(1, "ACTIVE-2", "Hoạt động hai", 8m, isActive: true),
                NewTax(1, "INACTIVE-1", "Ngừng hoạt động", 5m, isActive: false),
                deleted);

            await seedContext.SaveChangesAsync();
            deletedId = deleted.Id;
        }

        await using (var deleteContext = CreateContext(options, storeId: 1))
        {
            var deleted = await deleteContext.Set<Tax>().SingleAsync(x => x.Id == deletedId);
            deleteContext.Set<Tax>().Remove(deleted);
            await deleteContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.Set<Tax>().Add(
                NewTax(2, "OTHER", "Store khác", 10m, isActive: true));
            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new TaxRepository(readContext);
        var summary = await InvokeSummaryAsync(repository, storeId: 1);

        summary.TotalItems.Should().Be(3);
        summary.ActiveItems.Should().Be(2);
        summary.InactiveItems.Should().Be(1);
    }

    [Fact]
    public async Task Existing_GetPagedAsync_overload_should_preserve_product_dropdown_contract()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            seedContext.Set<Tax>().AddRange(
                NewTax(1, "ACTIVE", "Hoạt động", 10m, isActive: true),
                NewTax(1, "INACTIVE", "Ngừng hoạt động", 8m, isActive: false));
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new TaxRepository(readContext);
        var (items, total) = await repository.GetPagedAsync(
            storeId: 1,
            search: null,
            page: 1,
            pageSize: 500);

        total.Should().Be(2);
        items.Should().HaveCount(2);
        items.Should().Contain(x => x.IsActive);
        items.Should().Contain(x => !x.IsActive);
    }

    private static async Task<(IReadOnlyList<Tax> Items, int TotalItems)> InvokeStatusPagedAsync(
        TaxRepository repository,
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize)
    {
        var method = typeof(TaxRepository).GetMethod(
            nameof(TaxRepository.GetPagedAsync),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types:
            [
                typeof(int),
                typeof(string),
                typeof(bool?),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(
            repository,
            [storeId, search, status, page, pageSize, CancellationToken.None]);

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        return Assert.IsType<(IReadOnlyList<Tax> Items, int TotalItems)>(result);
    }

    private static async Task<(int TotalItems, int ActiveItems, int InactiveItems)> InvokeSummaryAsync(
        TaxRepository repository,
        int storeId)
    {
        var method = typeof(TaxRepository).GetMethod(
            "GetSummaryAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(repository, [storeId, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        return Assert.IsType<(int TotalItems, int ActiveItems, int InactiveItems)>(result);
    }

    private static Tax NewTax(
        int storeId,
        string code,
        string name,
        decimal rate,
        bool isActive)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            Rate = rate,
            IsActive = isActive,
            RowVersion = new byte[8]
        };

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        int storeId)
    {
        var tenant = new TenantContext();
        tenant.SetStore(storeId, "tax-query-test");

        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "tax-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
