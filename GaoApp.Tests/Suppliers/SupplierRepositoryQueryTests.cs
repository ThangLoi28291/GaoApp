using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Mappings.Suppliers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Suppliers;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Suppliers;

public sealed class SupplierRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_and_status_before_total_and_paging()
    {
        var options =
            CreateOptions();

        await using (
            var seedContext =
                CreateContext(options, storeId: 1))
        {
            seedContext
                .Set<Supplier>()
                .AddRange(
                    NewSupplier(
                        storeId: 1,
                        code: "NCC-ACTIVE",
                        name: "Gạo Miền Tây",
                        phone: "0901000001",
                        isActive: true,
                        taxCode: "031.277-0607"),

                    NewSupplier(
                        storeId: 1,
                        code: "NCC-INACTIVE",
                        name: "Gạo Miền Tây cũ",
                        phone: "0901000002",
                        isActive: false),

                    NewSupplier(
                        storeId: 1,
                        code: "NCC-OTHER",
                        name: "Bao bì",
                        phone: "0901000003",
                        isActive: true));

            await seedContext
                .SaveChangesAsync();
        }

        await using (
            var otherStoreContext =
                CreateContext(options, storeId: 2))
        {
            otherStoreContext
                .Set<Supplier>()
                .Add(
                    NewSupplier(
                        storeId: 2,
                        code: "NCC-OTHER-STORE",
                        name: "Gạo Miền Tây ngoài Store",
                        phone: "0901000002",
                        isActive: true));

            await otherStoreContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options, storeId: 1);

        var repository =
            new SupplierRepository(
                readContext);

        var (activeItems, activeTotal) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: "Gạo Miền Tây",
                status: true,
                page: 1,
                pageSize: 20);

        activeTotal
            .Should()
            .Be(1);

        activeItems
            .Should()
            .ContainSingle();

        activeItems
            .Single()
            .Code
            .Should()
            .Be("NCC-ACTIVE");

        var (inactiveItems, inactiveTotal) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: "0901000002",
                status: false,
                page: 1,
                pageSize: 20);

        inactiveTotal
            .Should()
            .Be(1);

        inactiveItems
            .Should()
            .ContainSingle();

        inactiveItems
            .Single()
            .Code
            .Should()
            .Be("NCC-INACTIVE");

        var (taxCodeItems, taxCodeTotal) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: "031.277",
                status: null,
                page: 1,
                pageSize: 20);

        taxCodeTotal
            .Should()
            .Be(1);

        taxCodeItems
            .Should()
            .ContainSingle();

        taxCodeItems
            .Single()
            .Code
            .Should()
            .Be("NCC-ACTIVE");
    }

    [Fact]
    public async Task GetPagedAsync_should_search_vietnamese_names_without_diacritics_on_sql_server()
    {
        await using var database =
            new PreflightAcceptanceDatabase();

        await database
            .CreateDatabaseAsync();

        await using var context =
            database.CreateContext();

        await context.Database
            .MigrateAsync();

        var store =
            new Store
            {
                Name = "Supplier search test",
                SubDomain = "supplier-search-test",
                SubDomainNormalized = "SUPPLIER-SEARCH-TEST",
                IsActive = true
            };

        context.Stores
            .Add(store);

        await context
            .SaveChangesAsync();

        context.Suppliers
            .Add(
                new Supplier
                {
                    StoreId = store.Id,
                    Code = "NCC-ANH-DUONG",
                    Name = "Đại lý Ánh Dương",
                    Phone = "0901000099",
                    TaxCode = "031.277-0607",
                    IsActive = true
                });

        await context
            .SaveChangesAsync();

        var tenant =
            new TenantContext();

        tenant.SetStore(
            store.Id,
            "supplier-accent-search-test");

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(
                    database.ConnectionString)
                .Options;

        await using var readContext =
            new AppDbContext(
                options,
                tenant,
                new TestCurrentUser());

        var repository =
            new SupplierRepository(
                readContext);

        var (_, unfilteredTotal) =
            await repository.GetPagedAsync(
                storeId: store.Id,
                search: null,
                status: true,
                page: 1,
                pageSize: 20);

        unfilteredTotal
            .Should()
            .Be(1);

        var (items, total) =
            await repository.GetPagedAsync(
                storeId: store.Id,
                search: "dai ly anh duong",
                status: true,
                page: 1,
                pageSize: 20);

        total
            .Should()
            .Be(1);

        items
            .Should()
            .ContainSingle();

        items
            .Single()
            .Name
            .Should()
            .Be("Đại lý Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_same_store_non_deleted_rows()
    {
        var options =
            CreateOptions();

        int deletedSupplierId;

        await using (
            var seedContext =
                CreateContext(options, storeId: 1))
        {
            var deletedSupplier =
                NewSupplier(
                    storeId: 1,
                    code: "REMOVED",
                    name: "Removed supplier",
                    phone: null,
                    isActive: true);

            seedContext
                .Set<Supplier>()
                .AddRange(
                    NewSupplier(
                        storeId: 1,
                        code: "ACTIVE-1",
                        name: "Active one",
                        phone: null,
                        isActive: true),

                    NewSupplier(
                        storeId: 1,
                        code: "ACTIVE-2",
                        name: "Active two",
                        phone: null,
                        isActive: true),

                    NewSupplier(
                        storeId: 1,
                        code: "INACTIVE-1",
                        name: "Inactive one",
                        phone: null,
                        isActive: false),

                    deletedSupplier);

            await seedContext
                .SaveChangesAsync();

            deletedSupplierId =
                deletedSupplier.Id;
        }

        await using (
            var otherStoreContext =
                CreateContext(options, storeId: 2))
        {
            otherStoreContext
                .Set<Supplier>()
                .Add(
                    NewSupplier(
                        storeId: 2,
                        code: "OTHER-STORE",
                        name: "Other store",
                        phone: null,
                        isActive: true));

            await otherStoreContext
                .SaveChangesAsync();
        }

        await using (
            var deleteContext =
                CreateContext(options, storeId: 1))
        {
            var deletedSupplier =
                await deleteContext
                    .Set<Supplier>()
                    .SingleAsync(x =>
                        x.Id ==
                        deletedSupplierId);

            deleteContext
                .Set<Supplier>()
                .Remove(deletedSupplier);

            await deleteContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options, storeId: 1);

        var repository =
            new SupplierRepository(
                readContext);

        var summary =
            await repository
                .GetSummaryAsync(
                    storeId: 1);

        summary.TotalItems
            .Should()
            .Be(3);

        summary.ActiveItems
            .Should()
            .Be(2);

        summary.InactiveItems
            .Should()
            .Be(1);
    }
    [Fact]
    public async Task GetPagedAsync_should_allow_500_page_size_for_product_dropdown()
    {
        var options =
            CreateOptions();

        await using (
            var seedContext =
                CreateContext(options, storeId: 1))
        {
            var suppliers =
                Enumerable.Range(1, 291)
                    .Select(i =>
                        NewSupplier(
                            storeId: 1,
                            code: $"SUP{i:D6}",
                            name: $"Supplier {i:D3}",
                            phone: null,
                            isActive: true))
                    .ToArray();

            seedContext
                .Set<Supplier>()
                .AddRange(suppliers);

            await seedContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options, storeId: 1);

        var repository =
            new SupplierRepository(
                readContext);

        var (items, total) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: null,
                page: 1,
                pageSize: 500);

        total
            .Should()
            .Be(291);

        items
            .Should()
            .HaveCount(291);
    }
    [Fact]
    public async Task Existing_GetPagedAsync_overload_should_remain_backward_compatible()
    {
        var options =
            CreateOptions();

        await using (
            var seedContext =
                CreateContext(options, storeId: 1))
        {
            seedContext
                .Set<Supplier>()
                .AddRange(
                    NewSupplier(
                        storeId: 1,
                        code: "ACTIVE",
                        name: "Active",
                        phone: null,
                        isActive: true),

                    NewSupplier(
                        storeId: 1,
                        code: "INACTIVE",
                        name: "Inactive",
                        phone: null,
                        isActive: false));

            await seedContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options, storeId: 1);

        var repository =
            new SupplierRepository(
                readContext);

        var (items, total) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: null,
                page: 1,
                pageSize: 20);

        total
            .Should()
            .Be(2);

        items
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public void ToListItemDto_should_preserve_contact_raw_tax_code_and_created_timestamp()
    {
        var createdAtUtc =
            new DateTime(
                2026,
                9,
                1,
                3,
                15,
                0,
                DateTimeKind.Utc);

        var supplier =
            NewSupplier(
                storeId: 1,
                code: "NCC-01",
                name: "Nhà cung cấp",
                phone: "0901000001",
                isActive: true);

        supplier.Id = 12;
        supplier.ContactName = "Nguyễn Văn A";
        supplier.Email = "supplier@example.test";
        supplier.TaxCode = " 031.277-0607 ";
        supplier.CreatedAtUtc = createdAtUtc;

        var dto =
            supplier.ToListItemDto();

        dto.Id
            .Should()
            .Be(12);

        dto.ContactName
            .Should()
            .Be("Nguyễn Văn A");

        dto.Phone
            .Should()
            .Be("0901000001");

        dto.Email
            .Should()
            .Be("supplier@example.test");

        dto.TaxCode
            .Should()
            .Be(" 031.277-0607 ");

        dto.CreatedAtUtc
            .Should()
            .Be(createdAtUtc);
    }

    private static Supplier NewSupplier(
        int storeId,
        string code,
        string name,
        string? phone,
        bool isActive,
        string? taxCode = null)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            Phone = phone,
            TaxCode = taxCode,
            IsActive = isActive,
            SortOrder = 0,
            RowVersion = new byte[8]
        };

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid()
                    .ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        int storeId)
    {
        var tenant =
            new TenantContext();

        tenant.SetStore(
            storeId,
            "supplier-query-test");

        var context =
            new InMemoryAppDbContext(
                options,
                tenant,
                new TestCurrentUser());

        context.VerifyRowVersionConfiguration();

        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;

        public string? UserName =>
            "supplier-query-test";

        public int? TerminalId =>
            null;

        public string? TerminalCode =>
            null;

        public bool IsAuthenticated =>
            true;
    }
}
