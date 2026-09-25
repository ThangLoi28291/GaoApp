using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.AttributeValues;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.AttributeValues;

public sealed class AttributeValueRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_parent_status_search_and_store_before_total_and_paging()
    {
        var options = CreateOptions();
        int colorId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var color = NewParent(1, "COLOR", "Màu sắc");
            var size = NewParent(1, "SIZE", "Kích cỡ");
            seedContext.ProductAttributes.AddRange(color, size);
            await seedContext.SaveChangesAsync();
            colorId = color.Id;

            seedContext.AttributeValues.AddRange(
                NewValue(1, color.Id, "RED", "Màu đỏ", status: true),
                NewValue(1, color.Id, "RED-OLD", "Màu đỏ cũ", status: false),
                NewValue(1, size.Id, "LARGE", "Cỡ lớn", status: true));
            await seedContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            var color = NewParent(2, "COLOR-OTHER", "Màu sắc Store khác");
            otherStoreContext.ProductAttributes.Add(color);
            await otherStoreContext.SaveChangesAsync();
            otherStoreContext.AttributeValues.Add(
                NewValue(2, color.Id, "RED-OTHER", "Màu đỏ Store khác", status: true));
            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new AttributeValueRepository(readContext);
        var page = await repository.GetPagedAsync(
            storeId: 1,
            attributeId: colorId,
            status: true,
            search: "Màu",
            page: 1,
            pageSize: 20);

        page.TotalItems.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items.Single().Code.Should().Be("RED");
    }

    [Fact]
    public async Task GetPagedAsync_should_search_value_and_parent_names_without_diacritics_on_sql_server()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var store = new Store
        {
            Name = "Attribute value search test",
            SubDomain = "attribute-value-search-test",
            SubDomainNormalized = "ATTRIBUTE-VALUE-SEARCH-TEST",
            IsActive = true
        };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        var color = new ProductAttribute
        {
            StoreId = store.Id,
            Code = "COLOR",
            Name = "Màu sắc",
            Status = true,
            RowVersion = new byte[8]
        };
        var flavor = new ProductAttribute
        {
            StoreId = store.Id,
            Code = "FLAVOR",
            Name = "Đặc tính miền Tây",
            Status = true,
            RowVersion = new byte[8]
        };

        context.ProductAttributes.AddRange(color, flavor);
        await context.SaveChangesAsync();

        context.AttributeValues.AddRange(
            NewValue(store.Id, color.Id, "RED", "Đỏ Ánh Dương"),
            NewValue(store.Id, flavor.Id, "TYPE-1", "Loại một"));

        await context.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetStore(store.Id, "attribute-value-accent-search-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using var readContext = new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        var repository = new AttributeValueRepository(readContext);

        var byValue = await repository.GetPagedAsync(
            store.Id,
            attributeId: null,
            status: true,
            search: "do anh duong",
            page: 1,
            pageSize: 20);

        byValue.TotalItems.Should().Be(1);
        byValue.Items.Single().Name.Should().Be("Đỏ Ánh Dương");

        var byParent = await repository.GetPagedAsync(
            store.Id,
            attributeId: null,
            status: true,
            search: "dac tinh mien tay",
            page: 1,
            pageSize: 20);

        byParent.TotalItems.Should().Be(1);
        byParent.Items.Single().Attribute.Name.Should().Be("Đặc tính miền Tây");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_same_store_non_deleted_rows()
    {
        var options = CreateOptions();
        int deletedValueId;

        await using (var seedContext = CreateContext(options, storeId: 1))
        {
            var parent = NewParent(1, "COLOR", "Màu sắc");
            seedContext.ProductAttributes.Add(parent);
            await seedContext.SaveChangesAsync();

            var deleted = NewValue(1, parent.Id, "REMOVED", "Đã xóa", status: true);
            seedContext.AttributeValues.AddRange(
                NewValue(1, parent.Id, "ACTIVE-1", "Hoạt động một", status: true),
                NewValue(1, parent.Id, "ACTIVE-2", "Hoạt động hai", status: true),
                NewValue(1, parent.Id, "INACTIVE-1", "Ngừng hoạt động", status: false),
                deleted);
            await seedContext.SaveChangesAsync();
            deletedValueId = deleted.Id;
        }

        await using (var deleteContext = CreateContext(options, storeId: 1))
        {
            var deleted = await deleteContext.AttributeValues.SingleAsync(x => x.Id == deletedValueId);
            deleteContext.AttributeValues.Remove(deleted);
            await deleteContext.SaveChangesAsync();
        }

        await using (var otherStoreContext = CreateContext(options, storeId: 2))
        {
            var parent = NewParent(2, "OTHER", "Store khác");
            otherStoreContext.ProductAttributes.Add(parent);
            await otherStoreContext.SaveChangesAsync();
            otherStoreContext.AttributeValues.Add(
                NewValue(2, parent.Id, "OTHER-VALUE", "Store khác", status: true));
            await otherStoreContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(options, storeId: 1);
        var repository = new AttributeValueRepository(readContext);
        var summary = await repository.GetSummaryAsync(storeId: 1);

        summary.TotalItems.Should().Be(3);
        summary.ActiveItems.Should().Be(2);
        summary.InactiveItems.Should().Be(1);
    }

    private static ProductAttribute NewParent(
        int storeId,
        string code,
        string name)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            Status = true,
            RowVersion = new byte[8]
        };

    private static AttributeValue NewValue(
        int storeId,
        int attributeId,
        string code,
        string name,
        bool status = true)
        => new()
        {
            StoreId = storeId,
            AttributeId = attributeId,
            Code = code,
            Name = name,
            Status = status,
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
        tenant.SetStore(storeId, "attribute-value-query-test");

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
        public string? UserName => "attribute-value-query-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
