using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Mappings.Categories;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Categories;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Categories;

public sealed class CategoryRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_status_and_store_before_total_and_paging()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            seedContext.Set<Category>().AddRange(
                NewCategory(1, "GAO-ACTIVE", "Gạo miền Tây", true, sortOrder: 2),
                NewCategory(1, "GAO-INACTIVE", "Gạo miền Tây cũ", false, sortOrder: 3),
                NewCategory(1, "BAO-BI", "Bao bì", true, sortOrder: 1));

            await seedContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.Set<Category>().Add(
                NewCategory(2, "GAO-OTHER", "Gạo miền Tây ngoài Store", true));

            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new CategoryRepository(readContext);

        var activePage = await repository.GetPagedAsync(
            storeId: 1,
            search: "Gạo miền Tây",
            status: true,
            page: 1,
            pageSize: 20);

        activePage.TotalItems.Should().Be(1);
        activePage.Items.Should().ContainSingle();
        activePage.Items.Single().Code.Should().Be("GAO-ACTIVE");

        var inactivePage = await repository.GetPagedAsync(
            storeId: 1,
            search: "GAO",
            status: false,
            page: 1,
            pageSize: 20);

        inactivePage.TotalItems.Should().Be(1);
        inactivePage.Items.Should().ContainSingle();
        inactivePage.Items.Single().Code.Should().Be("GAO-INACTIVE");
    }

    [Fact]
    public async Task GetPagedAsync_should_materialize_parent_and_keep_sort_order_then_name()
    {
        var options = CreateOptions();
        int parentId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var parent = NewCategory(1, "ROOT", "Danh mục gốc", true, sortOrder: 0);
            seedContext.Set<Category>().Add(parent);
            await seedContext.SaveChangesAsync();
            parentId = parent.Id;

            seedContext.Set<Category>().AddRange(
                NewCategory(1, "CHILD-B", "B child", true, sortOrder: 5, parentId: parentId),
                NewCategory(1, "CHILD-A", "A child", true, sortOrder: 5, parentId: parentId),
                NewCategory(1, "FIRST", "First", true, sortOrder: 1));

            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new CategoryRepository(readContext);

        var page = await repository.GetPagedAsync(
            storeId: 1,
            search: "child",
            status: null,
            page: 1,
            pageSize: 20);

        page.Items.Select(x => x.Name).Should().Equal("A child", "B child");
        page.Items.Should().OnlyContain(x => x.Parent != null);
        page.Items.Should().OnlyContain(x => x.Parent!.Id == parentId);
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
            Name = "Category search test",
            SubDomain = "category-search-test",
            SubDomainNormalized = "CATEGORY-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        context.Categories.Add(
            NewCategory(store.Id, "CAT-ANH-DUONG", "Đặc sản Ánh Dương", true));

        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "category-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new CategoryRepository(readContext);
        var page = await repository.GetPagedAsync(
            storeId: store.Id,
            search: "dac san anh duong",
            status: true,
            page: 1,
            pageSize: 20);

        page.TotalItems.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items.Single().Name.Should().Be("Đặc sản Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_same_store_non_deleted_rows()
    {
        var options = CreateOptions();
        int deletedCategoryId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var deletedCategory = NewCategory(1, "REMOVED", "Removed", true);

            seedContext.Set<Category>().AddRange(
                NewCategory(1, "ACTIVE-1", "Active one", true),
                NewCategory(1, "ACTIVE-2", "Active two", true),
                NewCategory(1, "INACTIVE-1", "Inactive one", false),
                deletedCategory);

            await seedContext.SaveChangesAsync();
            deletedCategoryId = deletedCategory.Id;
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            otherStoreContext.Set<Category>().Add(
                NewCategory(2, "OTHER-STORE", "Other store", true));

            await otherStoreContext.SaveChangesAsync();
        }

        await using (var deleteContext = CreateContext(options, storeId: 1))
        {
            var deletedCategory = await deleteContext.Set<Category>()
                .SingleAsync(x => x.Id == deletedCategoryId);

            deleteContext.Set<Category>().Remove(deletedCategory);
            await deleteContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new CategoryRepository(readContext);
        var summary = await repository.GetSummaryAsync(storeId: 1);

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
            var categories = Enumerable.Range(1, 225)
                .Select(index => NewCategory(
                    1,
                    $"CAT-{index:000}",
                    $"Category {index:000}",
                    isActive: index % 2 == 0,
                    sortOrder: index))
                .ToArray();

            seedContext.Set<Category>().AddRange(categories);
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new CategoryRepository(readContext);

        var page = await repository.GetPagedAsync(
            storeId: 1,
            search: null,
            page: 1,
            pageSize: 500);

        page.TotalItems.Should().Be(225);
        page.Items.Should().HaveCount(225);
        page.Items.Should().Contain(x => x.IsActive);
        page.Items.Should().Contain(x => !x.IsActive);
    }

    [Fact]
    public void ToListItemDto_should_preserve_parent_sort_status_and_created_timestamp()
    {
        var createdAtUtc = new DateTime(2026, 9, 1, 3, 15, 0, DateTimeKind.Utc);
        var category = NewCategory(1, "CAT-01", "Danh mục", true, sortOrder: 7);

        category.Id = 12;
        category.Parent = NewCategory(1, "ROOT", "Danh mục cha", true);
        category.CreatedAtUtc = createdAtUtc;

        var dto = category.ToListItemDto();

        dto.Id.Should().Be(12);
        dto.ParentName.Should().Be("Danh mục cha");
        dto.SortOrder.Should().Be(7);
        dto.IsActive.Should().BeTrue();
        dto.CreatedAtUtc.Should().Be(createdAtUtc);
    }

    private static Category NewCategory(
        int storeId,
        string code,
        string name,
        bool isActive,
        int sortOrder = 0,
        int? parentId = null)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            IsActive = isActive,
            SortOrder = sortOrder,
            ParentId = parentId,
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
        tenant.SetStore(storeId, "category-query-test");

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
        public string? UserName => "category-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
