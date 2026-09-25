using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Mappings.Brands;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Brands;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Brands;

public sealed class BrandRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_and_status_before_total_and_paging()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options))
        {
            seedContext.Brands.AddRange(
                NewBrand("ALPHA-ACTIVE", "Alpha Active", isActive: true),
                NewBrand("ALPHA-INACTIVE", "Alpha Inactive", isActive: false),
                NewBrand("BETA-ACTIVE", "Beta Active", isActive: true));

            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options);
        var repository = new BrandRepository(readContext);

        var (activeItems, activeTotal) = await repository.GetPagedAsync(
            storeId: 1,
            search: "Alpha",
            status: true,
            page: 1,
            pageSize: 20);

        activeTotal.Should().Be(1);
        activeItems.Should().ContainSingle();
        activeItems.Single().Code.Should().Be("ALPHA-ACTIVE");

        var (inactiveItems, inactiveTotal) = await repository.GetPagedAsync(
            storeId: 1,
            search: "Alpha",
            status: false,
            page: 1,
            pageSize: 20);

        inactiveTotal.Should().Be(1);
        inactiveItems.Should().ContainSingle();
        inactiveItems.Single().Code.Should().Be("ALPHA-INACTIVE");
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
            Name = "Brand search test",
            SubDomain = "brand-search-test",
            SubDomainNormalized = "BRAND-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        var brand = NewBrand("BRD-ANH-DUONG", "Đặc sản Ánh Dương", isActive: true);
        brand.StoreId = store.Id;
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "brand-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new BrandRepository(readContext);
        var (items, total) = await repository.GetPagedAsync(
            storeId: store.Id,
            search: "dac san anh duong",
            status: true,
            page: 1,
            pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Name.Should().Be("Đặc sản Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_current_non_deleted_store_rows()
    {
        var options = CreateOptions();
        int deletedBrandId;

        await using (var seedContext = CreateContext(options))
        {
            var deletedBrand = NewBrand("REMOVED", "Removed brand", isActive: true);

            seedContext.Brands.AddRange(
                NewBrand("ACTIVE-1", "Active one", isActive: true),
                NewBrand("ACTIVE-2", "Active two", isActive: true),
                NewBrand("INACTIVE-1", "Inactive one", isActive: false),
                deletedBrand);

            await seedContext.SaveChangesAsync();
            deletedBrandId = deletedBrand.Id;
        }

        await using (var deleteContext = CreateContext(options))
        {
            var deletedBrand = await deleteContext.Brands.SingleAsync(x => x.Id == deletedBrandId);
            deleteContext.Brands.Remove(deletedBrand);
            await deleteContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options);
        var repository = new BrandRepository(readContext);

        var summary = await repository.GetSummaryAsync(storeId: 1);

        summary.TotalItems.Should().Be(3);
        summary.ActiveItems.Should().Be(2);
        summary.InactiveItems.Should().Be(1);
    }

    [Fact]
    public async Task Existing_GetPagedAsync_overload_should_remain_backward_compatible()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options))
        {
            seedContext.Brands.AddRange(
                NewBrand("ACTIVE", "Active", isActive: true),
                NewBrand("INACTIVE", "Inactive", isActive: false));

            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options);
        var repository = new BrandRepository(readContext);

        var (items, total) = await repository.GetPagedAsync(
            storeId: 1,
            search: null,
            page: 1,
            pageSize: 20);

        total.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public void ToListItemDto_should_preserve_created_timestamp()
    {
        var createdAtUtc = new DateTime(2026, 9, 1, 3, 15, 0, DateTimeKind.Utc);
        var brand = NewBrand("DATE", "Date brand", isActive: true);
        brand.Id = 12;
        brand.CreatedAtUtc = createdAtUtc;

        var dto = brand.ToListItemDto();

        dto.Id.Should().Be(12);
        dto.CreatedAtUtc.Should().Be(createdAtUtc);
    }

    private static Brand NewBrand(string code, string name, bool isActive)
        => new()
        {
            StoreId = 1,
            Code = code,
            Name = name,
            IsActive = isActive,
            RowVersion = new byte[8]
        };

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options)
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "brand-query-test");

        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "brand-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
