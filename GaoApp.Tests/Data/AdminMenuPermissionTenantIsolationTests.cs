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
    [Fact]
    public async Task Assigned_role_lookup_should_return_only_roles_from_requested_store()
    {
        var fixture = await CreateFixtureAsync();

        await using var context = fixture.CreateStoreContext(1);
        var repository = new AdminMenuPermissionRepository(context);

        var roleIds = await repository.GetRoleIdsByPermissionCodeAsync(
            1,
            fixture.PermissionCode);

        roleIds.Should().Equal(fixture.StoreOneRoleId);
    }

    [Fact]
    public async Task Sync_should_remove_only_current_store_role_permissions()
    {
        var fixture = await CreateFixtureAsync();

        await using (var context = fixture.CreateStoreContext(1))
        {
            var repository = new AdminMenuPermissionRepository(context);

            await repository.SyncPermissionRolesAsync(
                1,
                fixture.Permission,
                new List<int>());

            await context.SaveChangesAsync();
        }

        await using var verificationContext = fixture.CreateHostContext();
        var remainingRoleIds = await verificationContext.RolePermissions
            .Where(x => x.PermissionId == fixture.PermissionId)
            .Select(x => x.RoleId)
            .ToListAsync();

        remainingRoleIds.Should().Equal(fixture.StoreTwoRoleId);
    }

    [Fact]
    public async Task Sync_should_reject_role_from_another_store()
    {
        var fixture = await CreateFixtureAsync();

        await using var context = fixture.CreateStoreContext(1);
        var repository = new AdminMenuPermissionRepository(context);

        var action = () => repository.SyncPermissionRolesAsync(
            1,
            fixture.Permission,
            new List<int> { fixture.StoreTwoRoleId });

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*không thuộc cửa hàng hiện tại*");
    }

    [Fact]
    public async Task New_permission_and_role_assignment_should_save_atomically()
    {
        var fixture = await CreateFixtureAsync();

        await using var context = fixture.CreateStoreContext(1);
        var repository = new AdminMenuPermissionRepository(context);
        var permission = new Permission
        {
            Code = "PHASE3.NEW.PERMISSION",
            Name = "New permission",
            GroupName = "Tests"
        };

        await repository.AddPermissionAsync(permission);
        await repository.SyncPermissionRolesAsync(
            1,
            permission,
            new List<int> { fixture.StoreOneRoleId });
        await context.SaveChangesAsync();

        permission.Id.Should().BeGreaterThan(0);
        var assignmentExists = await context.RolePermissions
            .AnyAsync(x =>
                x.PermissionId == permission.Id &&
                x.RoleId == fixture.StoreOneRoleId);

        assignmentExists.Should().BeTrue();
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
            => CreateContext(tenant => tenant.SetHostAdmin());

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
