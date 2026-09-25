using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.ProductAttributes;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.ProductAttributes;

public sealed class ProductAttributeRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_status_and_store_before_total_and_paging()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            seedContext.ProductAttributes.AddRange(
                NewAttribute(1, "COLOR-ACTIVE", "Màu sắc", status: true),
                NewAttribute(1, "COLOR-INACTIVE", "Màu sắc cũ", status: false),
                NewAttribute(1, "SIZE-ACTIVE", "Kích cỡ", status: true));

            await seedContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.ProductAttributes.Add(
                NewAttribute(2, "COLOR-OTHER", "Màu sắc Store khác", status: true));

            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductAttributeRepository(readContext);

        var (items, total) = await repository.GetPagedAsync(
            storeId: 1,
            search: "Màu sắc",
            status: true,
            page: 1,
            pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Code.Should().Be("COLOR-ACTIVE");
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
            Name = "Product attribute search test",
            SubDomain = "product-attribute-search-test",
            SubDomainNormalized = "PRODUCT-ATTRIBUTE-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        context.ProductAttributes.Add(new ProductAttribute
        {
            StoreId = store.Id,
            Code = "ATTR-ANH-DUONG",
            Name = "Đặc tính Ánh Dương",
            Status = true,
            RowVersion = new byte[8]
        });

        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "product-attribute-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new ProductAttributeRepository(readContext);
        var (items, total) = await repository.GetPagedAsync(
            storeId: store.Id,
            search: "dac tinh anh duong",
            page: 1,
            pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Name.Should().Be("Đặc tính Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_same_store_non_deleted_rows()
    {
        var options = CreateOptions();
        int deletedId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var deleted = NewAttribute(1, "REMOVED", "Đã xóa", status: true);
            seedContext.ProductAttributes.AddRange(
                NewAttribute(1, "ACTIVE-1", "Hoạt động một", status: true),
                NewAttribute(1, "ACTIVE-2", "Hoạt động hai", status: true),
                NewAttribute(1, "INACTIVE-1", "Ngừng hoạt động", status: false),
                deleted);

            await seedContext.SaveChangesAsync();
            deletedId = deleted.Id;
        }

        await using (var deleteContext = CreateContext(options, storeId: 1))
        {
            var deleted = await deleteContext.ProductAttributes.SingleAsync(x => x.Id == deletedId);
            deleteContext.ProductAttributes.Remove(deleted);
            await deleteContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.ProductAttributes.Add(
                NewAttribute(2, "OTHER", "Store khác", status: true));
            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductAttributeRepository(readContext);
        var summary = await repository.GetSummaryAsync(storeId: 1);

        summary.TotalItems.Should().Be(3);
        summary.ActiveItems.Should().Be(2);
        summary.InactiveItems.Should().Be(1);
    }

    [Fact]
    public async Task Existing_GetPagedAsync_overload_should_remain_backward_compatible()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            seedContext.ProductAttributes.AddRange(
                NewAttribute(1, "ACTIVE", "Hoạt động", status: true),
                NewAttribute(1, "INACTIVE", "Ngừng hoạt động", status: false));
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductAttributeRepository(readContext);
        var (items, total) = await repository.GetPagedAsync(
            storeId: 1,
            search: null,
            page: 1,
            pageSize: 20);

        total.Should().Be(2);
        items.Should().HaveCount(2);
    }

    private static ProductAttribute NewAttribute(
        int storeId,
        string code,
        string name,
        bool status)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
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
        tenant.SetStore(storeId, "product-attribute-query-test");

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
        public string? UserName => "product-attribute-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
