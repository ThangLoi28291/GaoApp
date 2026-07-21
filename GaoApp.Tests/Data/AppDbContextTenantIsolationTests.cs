using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public class AppDbContextTenantIsolationTests
{
    [Fact]
    public async Task Real_context_filter_should_return_only_current_store_non_deleted_rows()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, tenant => tenant.SetHostAdmin()))
        {
            var activeStoreOne = NewCategory(1, "S1-ACTIVE", "Store 1 active");
            var deletedStoreOne = NewCategory(1, "S1-DELETED", "Store 1 deleted");
            var activeStoreTwo = NewCategory(2, "S2-ACTIVE", "Store 2 active");

            seedContext.Categories.AddRange(activeStoreOne, deletedStoreOne, activeStoreTwo);
            await seedContext.SaveChangesAsync();

            seedContext.Categories.Remove(deletedStoreOne);
            await seedContext.SaveChangesAsync();
        }

        await using var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var visibleCodes = await storeOneContext.Categories
            .Select(x => x.Code)
            .ToListAsync();

        visibleCodes.Should().Equal("S1-ACTIVE");
    }

    [Fact]
    public async Task Tenant_guard_should_reject_added_entity_for_another_store()
    {
        var options = CreateOptions();

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.Categories.Add(NewCategory(2, "WRONG-STORE", "Wrong store"));

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*không khớp TenantContext=1*");
    }

    [Fact]
    public async Task Tenant_guard_should_reject_update_loaded_from_another_store()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(options, tenant => tenant.SetHostAdmin()))
        {
            seedContext.Categories.Add(NewCategory(2, "S2-CATEGORY", "Store 2 category"));
            await seedContext.SaveChangesAsync();
        }

        await using var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var category = await storeOneContext.Categories
            .IgnoreQueryFilters()
            .SingleAsync();

        category.Name = "Cross-tenant update";

        var action = () => storeOneContext.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Tenant mismatch UPDATE");
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static AppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        Action<TenantContext> configureTenant)
    {
        var tenant = new TenantContext();
        configureTenant(tenant);
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static Category NewCategory(int storeId, string code, string name)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            IsActive = true,
            // SQL Server sinh rowversion tự động; EF InMemory thì không.
            RowVersion = new byte[8]
        };

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase3-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
