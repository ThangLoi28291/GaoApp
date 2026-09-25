using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Mappings.Units;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Units;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Units;

public sealed class UnitRepositoryQueryTests
{
    [Fact]
    public async Task GetPagedAsync_should_apply_search_and_status_before_total_and_paging()
    {
        var options =
            CreateOptions();

        await using (
            var seedContext =
                CreateContext(options))
        {
            seedContext
                .Set<Unit>()
                .AddRange(
                    NewUnit(
                        "KG-ACTIVE",
                        "Kilogram Active",
                        isActive: true),

                    NewUnit(
                        "KG-INACTIVE",
                        "Kilogram Inactive",
                        isActive: false),

                    NewUnit(
                        "BOX-ACTIVE",
                        "Box Active",
                        isActive: true));

            await seedContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options);

        var repository =
            new UnitRepository(
                readContext);

        var (activeItems, activeTotal) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: "Kilogram",
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
            .Be("KG-ACTIVE");

        var (inactiveItems, inactiveTotal) =
            await repository.GetPagedAsync(
                storeId: 1,
                search: "Kilogram",
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
            .Be("KG-INACTIVE");
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
                Name = "Unit search test",
                SubDomain = "unit-search-test",
                SubDomainNormalized = "UNIT-SEARCH-TEST",
                IsActive = true
            };

        context.Stores.Add(store);
        await context.SaveChangesAsync();

        var unit =
            NewUnit(
                "DV-ANH-DUONG",
                "Đơn vị Ánh Dương",
                isActive: true);

        unit.StoreId = store.Id;
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var tenant =
            new TenantContext();

        tenant.SetStore(
            store.Id,
            "unit-accent-search-test");

        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options;

        await using var readContext =
            new AppDbContext(
                options,
                tenant,
                new TestCurrentUser());

        var repository =
            new UnitRepository(readContext);

        var (items, total) =
            await repository.GetPagedAsync(
                storeId: store.Id,
                search: "don vi anh duong",
                status: true,
                page: 1,
                pageSize: 20);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items.Single().Name.Should().Be("Đơn vị Ánh Dương");
    }

    [Fact]
    public async Task GetSummaryAsync_should_count_only_current_non_deleted_rows()
    {
        var options =
            CreateOptions();

        int deletedUnitId;

        await using (
            var seedContext =
                CreateContext(options))
        {
            var deletedUnit =
                NewUnit(
                    "REMOVED",
                    "Removed unit",
                    isActive: true);

            seedContext
                .Set<Unit>()
                .AddRange(
                    NewUnit(
                        "ACTIVE-1",
                        "Active one",
                        isActive: true),

                    NewUnit(
                        "ACTIVE-2",
                        "Active two",
                        isActive: true),

                    NewUnit(
                        "INACTIVE-1",
                        "Inactive one",
                        isActive: false),

                    deletedUnit);

            await seedContext
                .SaveChangesAsync();

            deletedUnitId =
                deletedUnit.Id;
        }

        await using (
            var deleteContext =
                CreateContext(options))
        {
            var deletedUnit =
                await deleteContext
                    .Set<Unit>()
                    .SingleAsync(x =>
                        x.Id ==
                        deletedUnitId);

            deleteContext
                .Set<Unit>()
                .Remove(deletedUnit);

            await deleteContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options);

        var repository =
            new UnitRepository(
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
    public async Task Existing_GetPagedAsync_overload_should_remain_backward_compatible()
    {
        var options =
            CreateOptions();

        await using (
            var seedContext =
                CreateContext(options))
        {
            seedContext
                .Set<Unit>()
                .AddRange(
                    NewUnit(
                        "ACTIVE",
                        "Active",
                        isActive: true),

                    NewUnit(
                        "INACTIVE",
                        "Inactive",
                        isActive: false));

            await seedContext
                .SaveChangesAsync();
        }

        await using var readContext =
            CreateContext(options);

        var repository =
            new UnitRepository(
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
    public void ToListItemDto_should_preserve_created_timestamp_and_unit_metadata()
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

        var unit =
            NewUnit(
                "KG",
                "Kilogram",
                isActive: true);

        unit.Id = 12;
        unit.IsBase = true;
        unit.SortOrder = 7;
        unit.CreatedAtUtc = createdAtUtc;

        var dto =
            unit.ToListItemDto();

        dto.Id
            .Should()
            .Be(12);

        dto.IsBase
            .Should()
            .BeTrue();

        dto.SortOrder
            .Should()
            .Be(7);

        dto.CreatedAtUtc
            .Should()
            .Be(createdAtUtc);
    }

    private static Unit NewUnit(
        string code,
        string name,
        bool isActive)
        => new()
        {
            StoreId = 1,
            Code = code,
            Name = name,
            IsActive = isActive,
            IsBase = false,
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
        DbContextOptions<InMemoryAppDbContext> options)
    {
        var tenant =
            new TenantContext();

        tenant.SetStore(
            1,
            "unit-query-test");

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
            "unit-query-test";

        public int? TerminalId =>
            null;

        public string? TerminalCode =>
            null;

        public bool IsAuthenticated =>
            true;
    }
}
