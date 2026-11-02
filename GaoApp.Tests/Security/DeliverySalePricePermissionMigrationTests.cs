using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class DeliverySalePricePermissionMigrationTests
{
    [Fact]
    public async Task Migration_adds_only_selectable_permission_and_keeps_existing_roles_grants_and_menus()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20261101000000_AddStorefrontFulfillmentSettlement");
        await using var db = database.CreateHostContext();
        var store = new Store { Name = "Delivery price permission fixture", SubDomain = "delivery-price-" + Guid.NewGuid().ToString("N"), IsActive = true };
        store.SubDomainNormalized = store.SubDomain.ToUpperInvariant();
        await LegacyStoreSeed.InsertAsync(db, store);
        var role = new Role { StoreId = store.Id, Code = "PRICE_PERMISSION_TEST", Name = "Existing role" };
        var permission = new Permission { Code = PermissionCodes.Delivery.ApproveChanges, Name = "Existing approval", GroupName = "Delivery" };
        var menu = new AdminMenuItem { StoreId = store.Id, Title = "Existing delivery menu", Area = "Admin", Controller = "DeliveryPage", Action = "Index", IsActive = true };
        db.AddRange(role, permission, menu);
        await db.SaveChangesAsync();
        db.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        await db.SaveChangesAsync();
        var count = await db.Permissions.CountAsync();
        var grants = await db.RolePermissions.AsNoTracking().Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync();
        var menus = await db.AdminMenuItems.AsNoTracking().Select(x => new { x.Id, x.Title, x.Controller, x.Action, x.PermissionCode }).ToArrayAsync();

        await database.MigrateAsync();
        var added = await db.Permissions.AsNoTracking().SingleAsync(x => x.Code == PermissionCodes.Delivery.EditSalePrice);
        Assert.Equal("Sửa giá bán đơn giao chưa chốt tiền", added.Name);
        Assert.Equal("Delivery", added.GroupName);
        Assert.Equal(count + 1, await db.Permissions.CountAsync());
        Assert.False(await db.RolePermissions.AnyAsync(x => x.PermissionId == added.Id));
        var sql = Assert.Single(new AddDeliverySalePricePermission().UpOperations.OfType<SqlOperation>()).Sql;
        await database.ExecuteAsync(sql);
        Assert.Equal(added.Id, await db.Permissions.Where(x => x.Code == PermissionCodes.Delivery.EditSalePrice).Select(x => x.Id).SingleAsync());
        Assert.Equal(count + 1, await db.Permissions.CountAsync());
        Assert.Equal(grants, await db.RolePermissions.AsNoTracking().Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync());
        Assert.Equal(menus, await db.AdminMenuItems.AsNoTracking().Select(x => new { x.Id, x.Title, x.Controller, x.Action, x.PermissionCode }).ToArrayAsync());
        Assert.Equal("Existing role", (await db.Roles.SingleAsync(x => x.Id == role.Id)).Name);
    }
}
