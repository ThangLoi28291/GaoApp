using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.AdminMenus;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public class AdminMenuPermissionTenantIsolationTests
{
    [Theory]
    [InlineData("report.profit.view", "ProfitReport")]
    [InlineData("report.sales.view", "SalesReport")]
    [InlineData("", "")]
    public async Task Profit_report_navigation_retains_leaf_permissions_and_store_boundary(string permission, string expected)
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.CreateStoreContext(1);
        var parent = new AdminMenuItem { StoreId = 1, Title = "Báo cáo", IsSystem = true };
        context.AdminMenuItems.Add(parent); await context.SaveChangesAsync();
        context.AdminMenuItems.AddRange(
            new AdminMenuItem { StoreId = 1, ParentId = parent.Id, Title = "Profit", Controller = "ProfitReport", PermissionCode = "report.profit.view", IsSystem = true },
            new AdminMenuItem { StoreId = 1, ParentId = parent.Id, Title = "Sales", Controller = "SalesReport", PermissionCode = "report.sales.view", IsSystem = true });
        await context.SaveChangesAsync();
        await using (var foreign = fixture.CreateStoreContext(2))
        {
            foreign.AdminMenuItems.Add(new AdminMenuItem { StoreId = 2, Title = "Foreign", Controller = "ForeignReport" });
            await foreign.SaveChangesAsync();
        }
        var service = new GaoApp.Application.Services.AdminMenus.AdminMenuService(
            new AdminMenuRepository(context), null!, new ProfitPermissions(permission));
        var menu = await service.GetForRenderAsync(1, 99);
        if (expected == "") Assert.Empty(menu);
        else Assert.Equal(expected, Assert.Single(Assert.Single(menu).Children).Controller);
    }

    private sealed class ProfitPermissions(string permission) : GaoApp.Application.Interfaces.Services.Security.ICurrentStorePermissionService
    {
        public Task<List<string>> GetPermissionsAsync(int storeId, int userId, CancellationToken ct = default)
            => Task.FromResult(permission == "" ? new List<string>() : new List<string> { permission });
        public Task<bool> HasPermissionAsync(int storeId, int userId, string code, CancellationToken ct = default)
            => Task.FromResult(code == permission);
    }

    [Fact]
    public async Task Menu_permission_catalog_is_read_only_and_leaves_all_store_grants_unchanged()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.CreateStoreContext(1);
        var repository = new AdminMenuPermissionRepository(context);
        var permissions = await repository.GetPermissionsAsync();
        var permission = await repository.GetPermissionByCodeAsync(fixture.PermissionCode);
        permissions.Should().ContainSingle().Which.Id.Should().Be(fixture.PermissionId);
        permission!.Id.Should().Be(fixture.PermissionId);
        (await repository.GetPermissionByCodeAsync("missing.permission")).Should().BeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();
        await using var check = fixture.CreateHostContext();
        var roleIds = await check.RolePermissions.Where(x => x.PermissionId == fixture.PermissionId)
            .OrderBy(x => x.RoleId).Select(x => x.RoleId).ToListAsync();
        roleIds.Should().Equal(fixture.StoreOneRoleId, fixture.StoreTwoRoleId);
    }
    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var fixture = new Fixture(options);

        await using var context = fixture.CreateHostContext();

        var permission = new Permission
        {
            Code = "PHASE3.MENU.TEST",
            Name = "Phase 3 menu test",
            GroupName = "Tests"
        };

        var storeOneRole = NewRole(1, "STORE1-MANAGER", "Store 1 manager");
        var storeTwoRole = NewRole(2, "STORE2-MANAGER", "Store 2 manager");

        context.AddRange(permission, storeOneRole, storeTwoRole);
        await context.SaveChangesAsync();

        context.RolePermissions.AddRange(
            new RolePermission { RoleId = storeOneRole.Id, PermissionId = permission.Id },
            new RolePermission { RoleId = storeTwoRole.Id, PermissionId = permission.Id });
        await context.SaveChangesAsync();

        fixture.PermissionId = permission.Id;
        fixture.PermissionCode = permission.Code;
        fixture.Permission = permission;
        fixture.StoreOneRoleId = storeOneRole.Id;
        fixture.StoreTwoRoleId = storeTwoRole.Id;

        return fixture;
    }

    private static Role NewRole(int storeId, string code, string name)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            IsSystemRole = false,
            // SQL Server sinh rowversion tự động; EF InMemory thì không.
            RowVersion = new byte[8]
        };

    private sealed class Fixture
    {
        private readonly DbContextOptions<InMemoryAppDbContext> _options;

        public Fixture(DbContextOptions<InMemoryAppDbContext> options)
        {
            _options = options;
        }

        public int PermissionId { get; set; }
        public string PermissionCode { get; set; } = string.Empty;
        public Permission Permission { get; set; } = null!;
        public int StoreOneRoleId { get; set; }
        public int StoreTwoRoleId { get; set; }

        public AppDbContext CreateHostContext()
            => CreateContext(tenant => tenant.SetHostAdminForTest());

        public AppDbContext CreateStoreContext(int storeId)
            => CreateContext(tenant => tenant.SetStore(storeId, $"store-{storeId}"));

        private AppDbContext CreateContext(Action<TenantContext> configureTenant)
        {
            var tenant = new TenantContext();
            configureTenant(tenant);
            var context = new InMemoryAppDbContext(_options, tenant, new TestCurrentUser());
            context.VerifyRowVersionConfiguration();
            return context;
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase3-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
