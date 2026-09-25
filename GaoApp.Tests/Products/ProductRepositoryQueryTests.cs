using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Products;

public sealed class ProductRepositoryQueryTests
{
    [Fact]
    public async Task Filtered_GetPagedAsync_should_apply_store_category_and_lifecycle_before_paging()
    {
        var options = CreateOptions();
        int categoryId;

        await using (var context = CreateContext(options, storeId: 1))
        {
            var catalog = AddCatalog(context, 1, "S1");
            await context.SaveChangesAsync();
            categoryId = catalog.Category.Id;

            AddProductGraph(context, catalog, "POS-A", "Sữa Ánh Dương", "milk-a", true, true, "SKU-POS", "893850000001");
            AddProductGraph(context, catalog, "DRAFT-A", "Sữa chờ bán", "milk-b", true, false, "SKU-DRAFT", "893850000002");
            AddProductGraph(context, catalog, "OFF-A", "Sữa ngừng", "milk-c", false, true, "SKU-OFF", "893850000003");
            await context.SaveChangesAsync();
        }

        await using (var otherStore = CreateContext(options, storeId: 2))
        {
            var catalog = AddCatalog(otherStore, 2, "S2");
            await otherStore.SaveChangesAsync();
            AddProductGraph(otherStore, catalog, "POS-OTHER", "Sữa Store khác", "milk-other", true, true, "SKU-POS", "893850000004");
            await otherStore.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductRepository(readContext);

        var result = await InvokeFilteredPagedAsync(
            repository,
            storeId: 1,
            search: "SKU-POS",
            categoryId,
            isActive: true,
            isSellable: true,
            page: 1,
            pageSize: 20);

        result.TotalItems.Should().Be(1);
        var item = result.Items.Should().ContainSingle().Subject;
        item.Alias.Should().Be("milk-a");
        item.VariantCount.Should().Be(1);
    }

    [Fact]
    public async Task Filtered_GetPagedAsync_should_search_active_unit_barcode_only()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(options, storeId: 1))
        {
            var catalog = AddCatalog(context, 1, "S1");
            await context.SaveChangesAsync();

            var active = AddProductGraph(context, catalog, "ACTIVE-BC", "Sản phẩm barcode", "barcode-active", true, true, "SKU-A", "893850000010");
            var inactive = AddProductGraph(context, catalog, "INACTIVE-BC", "Sản phẩm barcode cũ", "barcode-inactive", true, true, "SKU-B", "893850000011");
            inactive.Variants.Single().UnitConversions.Single().Barcodes.Single().IsActive = false;
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductRepository(readContext);

        var activeResult = await InvokeFilteredPagedAsync(repository, 1, "893850000010", null, null, null, 1, 20);
        var inactiveResult = await InvokeFilteredPagedAsync(repository, 1, "893850000011", null, null, null, 1, 20);

        activeResult.Items.Should().ContainSingle(x => x.Alias == "barcode-active");
        inactiveResult.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsync_should_use_exact_pos_readiness_partition()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(options, storeId: 1))
        {
            var catalog = AddCatalog(context, 1, "S1");
            await context.SaveChangesAsync();

            AddProductGraph(context, catalog, "POS", "POS", "pos", true, true, "SKU-1", "1001");
            AddProductGraph(context, catalog, "DRAFT", "DRAFT", "draft", true, false, "SKU-2", "1002");
            AddProductGraph(context, catalog, "OFF", "OFF", "off", false, true, "SKU-3", "1003");
            var deleted = AddProductGraph(context, catalog, "DELETED", "DELETED", "deleted", true, true, "SKU-4", "1004");
            await context.SaveChangesAsync();

            deleted.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductRepository(readContext);
        var summary = await InvokeSummaryAsync(repository, storeId: 1);

        summary.Should().Be((3, 1, 1, 1));
    }

    [Fact]
    public async Task Existing_GetPagedAsync_overload_should_remain_compatible()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(options, storeId: 1))
        {
            var catalog = AddCatalog(context, 1, "S1");
            await context.SaveChangesAsync();
            AddProductGraph(context, catalog, "ACTIVE", "Đang hoạt động", "active", true, true, "SKU-A", "2001");
            AddProductGraph(context, catalog, "INACTIVE", "Ngừng hoạt động", "inactive", false, true, "SKU-B", "2002");
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new ProductRepository(readContext);
        var result = await repository.GetPagedAsync(1, null, 1, 500);

        result.TotalItems.Should().Be(2);
        result.PageSize.Should().Be(200);
        result.Items.Should().Contain(x => x.IsActive);
        result.Items.Should().Contain(x => !x.IsActive);
    }

    [Fact]
    public void Repository_source_should_use_local_accent_insensitive_product_text_search()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "GaoApp.Infrastructure",
            "Repositories",
            "Products",
            "ProductRepository.cs");

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Latin1_General_100_CI_AI", source, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.Collate", source, StringComparison.Ordinal);
        Assert.Contains("Replace(\"Đ\", \"D\")", source, StringComparison.Ordinal);
        Assert.Contains("Replace(\"đ\", \"d\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filtered_GetPagedAsync_should_search_product_text_without_diacritics_on_sql_server()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var store = new Store
        {
            Name = "Product accent search test",
            SubDomain = "product-accent-search-test",
            SubDomainNormalized = "PRODUCT-ACCENT-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        var catalog = AddCatalog(context, store.Id, "SQL");
        await context.SaveChangesAsync();

        AddProductGraph(
            context,
            catalog,
            "ACCENT",
            "Đặc sản Ánh Dương",
            "product-accent-001",
            true,
            true,
            "SKU-ACCENT-001",
            "893850009999");

        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "product-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new ProductRepository(readContext);
        var result = await InvokeFilteredPagedAsync(
            repository,
            store.Id,
            "dac san anh duong",
            categoryId: null,
            isActive: true,
            isSellable: true,
            page: 1,
            pageSize: 20);

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Name == "Đặc sản Ánh Dương");
    }

    private static async Task<PagedResult<ProductListItemDto>> InvokeFilteredPagedAsync(
        ProductRepository repository,
        int storeId,
        string? search,
        int? categoryId,
        bool? isActive,
        bool? isSellable,
        int page,
        int pageSize)
    {
        var method = typeof(ProductRepository).GetMethod(
            nameof(ProductRepository.GetPagedAsync),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types:
            [
                typeof(int),
                typeof(string),
                typeof(int?),
                typeof(bool?),
                typeof(bool?),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(
            repository,
            [storeId, search, categoryId, isActive, isSellable, page, pageSize, CancellationToken.None]);

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        return Assert.IsType<PagedResult<ProductListItemDto>>(
            task.GetType().GetProperty("Result")?.GetValue(task));
    }

    private static async Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> InvokeSummaryAsync(
        ProductRepository repository,
        int storeId)
    {
        var method = typeof(ProductRepository).GetMethod(
            "GetSummaryAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(repository, [storeId, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        return Assert.IsType<(
            int TotalItems,
            int PosAllowedItems,
            int NotForPosItems,
            int InactiveItems)>(task.GetType().GetProperty("Result")?.GetValue(task));
    }

    private static Catalog AddCatalog(AppDbContext context, int storeId, string suffix)
    {
        var category = new Category { StoreId = storeId, Code = $"CAT-{suffix}", Name = $"Danh mục {suffix}", IsActive = true };
        var supplier = new Supplier { StoreId = storeId, Code = $"SUP-{suffix}", Name = $"Nhà cung cấp {suffix}", IsActive = true };
        var brand = new Brand { StoreId = storeId, Code = $"BR-{suffix}", Name = $"Thương hiệu {suffix}", IsActive = true };
        var unit = new Unit { StoreId = storeId, Code = $"U-{suffix}", Name = "Cái", IsActive = true, IsBase = true };

        context.AddRange(category, supplier, brand, unit);
        return new Catalog(category, supplier, brand, unit);
    }

    private static Product AddProductGraph(
        AppDbContext context,
        Catalog catalog,
        string code,
        string name,
        string alias,
        bool isActive,
        bool isSellable,
        string sku,
        string barcode)
    {
        var product = new Product
        {
            StoreId = catalog.Category.StoreId,
            Name = name,
            Alias = alias,
            CategoryId = catalog.Category.Id,
            Category = catalog.Category,
            SupplierId = catalog.Supplier.Id,
            Supplier = catalog.Supplier,
            BrandId = catalog.Brand.Id,
            Brand = catalog.Brand,
            BaseUnitId = catalog.Unit.Id,
            BaseUnit = catalog.Unit,
            BasePrice = 25_000m,
            IsActive = isActive,
            IsSellable = isSellable
        };

        var variant = new ProductVariant
        {
            StoreId = product.StoreId,
            Product = product,
            Sku = sku,
            ProductVariantName = $"{name} {code}",
            IsActive = true
        };

        var conversion = new ProductUnitConversion
        {
            StoreId = product.StoreId,
            ProductVariant = variant,
            UnitId = catalog.Unit.Id,
            Unit = catalog.Unit,
            Factor = 1m,
            IsBaseUnit = true,
            IsDefaultForSale = true,
            IsActive = true
        };

        conversion.Barcodes.Add(new ProductVariantUnitBarcode
        {
            StoreId = product.StoreId,
            ProductUnitConversion = conversion,
            Barcode = barcode,
            IsActive = true,
            IsPrimary = true
        });

        variant.UnitConversions.Add(conversion);
        product.Variants.Add(variant);
        context.Products.Add(product);
        return product;
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        int storeId)
    {
        var tenant = new TenantContext();
        tenant.SetStore(storeId, "product-query-test");

        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }

    private sealed record Catalog(Category Category, Supplier Supplier, Brand Brand, Unit Unit);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "product-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
