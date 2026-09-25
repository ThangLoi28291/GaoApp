using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Seed;

// Add both workspaces without invoking full menu recovery or changing custom menus.
public static class ProductLabelMenuSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'gao-product-label-menu-seed', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @r<0 THROW 51001, 'Label menu seed is busy.', 1;
            """, ct);
        var stores = await db.Stores.IgnoreQueryFilters().Where(x => !x.IsDeleted && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        foreach (var store in stores)
        {
            var entries = new[]
            {
                new { Title = "In tem sản phẩm", Controller = "LabelPrinting", Url = "/admin/label-printing", Icon = "bx bx-barcode", Order = 62, Permission = PermissionCodes.System.ProductLabel.Print },
                new { Title = "Cấu hình in tem", Controller = "LabelPrintingSettings", Url = "/admin/label-printing-settings", Icon = "bx bx-slider-alt", Order = 63, Permission = PermissionCodes.System.ProductLabel.Manage }
            };
            foreach (var entry in entries)
            {
                if (await db.AdminMenuItems.IgnoreQueryFilters().AnyAsync(x => x.StoreId == store && !x.IsDeleted &&
                    (x.Controller == entry.Controller || x.Url == entry.Url), ct)) continue;
                db.Add(new AdminMenuItem { StoreId = store, Title = entry.Title, Area = "Admin", Controller = entry.Controller, Action = "Index",
                    Url = entry.Url, Icon = entry.Icon, SortOrder = entry.Order, PermissionCode = entry.Permission, IsActive = true, IsSystem = true });
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
