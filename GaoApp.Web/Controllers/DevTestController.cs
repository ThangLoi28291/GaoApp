using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Controllers;

/// <summary>
/// Controller test nội bộ cho developer.
/// CHỈ chạy trong môi trường Development.
/// Production/Staging gọi vào sẽ nhận 404.
/// </summary>
public class DevTestController : DevOnlyControllerBase
{
    private readonly AppDbContext _db;

    public DevTestController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// 1) Tạo 1 category test
    /// Dùng để test:
    /// - auto gán StoreId
    /// - audit fields
    /// - save pipeline
    /// </summary>
    [HttpGet("/dev/create-category")]
    public async Task<IActionResult> CreateCategory()
    {
        var blockResult = EnsureDevelopmentOnly();
        if (blockResult != null)
            return blockResult;

        var code = "GAO";
        var exists = await _db.Categories.AnyAsync(x => x.Code == code);

        if (!exists)
        {
            _db.Categories.Add(new Category
            {
                Code = code,
                Name = "Gạo test"
                // KHÔNG set StoreId ở đây để test auto gán StoreId
            });

            await _db.SaveChangesAsync();
        }

        var items = await _db.Categories
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.StoreId,
                x.Code,
                x.Name,
                x.CreatedAtUtc,
                x.IsDeleted
            })
            .ToListAsync();

        return Json(items);
    }

    /// <summary>
    /// 2) Xóa mềm category mới nhất
    /// Dùng để test:
    /// - soft delete
    /// - DeletedAtUtc
    /// - global filter
    /// </summary>
    [HttpGet("/dev/delete-latest-category")]
    public async Task<IActionResult> DeleteLatestCategory()
    {
        var blockResult = EnsureDevelopmentOnly();
        if (blockResult != null)
            return blockResult;

        var item = await _db.Categories
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (item == null)
            return Content("No category");

        _db.Categories.Remove(item); // Remove => SaveChanges sẽ chuyển thành soft delete
        await _db.SaveChangesAsync();

        return Json(new
        {
            item.Id,
            item.StoreId,
            item.Code,
            item.IsDeleted,
            item.DeletedAtUtc
        });
    }

    /// <summary>
    /// 3) Xem list category
    /// Dùng để test:
    /// - query filter theo store
    /// - soft delete filter
    /// </summary>
    [HttpGet("/dev/categories")]
    public async Task<IActionResult> List()
    {
        var blockResult = EnsureDevelopmentOnly();
        if (blockResult != null)
            return blockResult;

        var items = await _db.Categories
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.StoreId,
                x.Code,
                x.Name,
                x.IsDeleted
            })
            .ToListAsync();

        return Json(items);
    }
}