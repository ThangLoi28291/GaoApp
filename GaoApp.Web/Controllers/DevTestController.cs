using GaoApp.Infrastructure.Data;
using GaoApp.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Controllers;

public class DevTestController : Controller
{
    private readonly AppDbContext _db;

    public DevTestController(AppDbContext db)
    {
        _db = db;
    }

    // 1) Tạo 1 category test
    [HttpGet("/dev/create-category")]
    public async Task<IActionResult> CreateCategory()
    {
        var code = "GAO";
        var exists = await _db.Categories.AnyAsync(x => x.Code == code);

        if (!exists)
        {
            _db.Categories.Add(new Category
            {
                Code = code,
                Name = "Gạo test"
                // ❌ KHÔNG set StoreId ở đây
            });

            await _db.SaveChangesAsync();
        }

        var items = await _db.Categories
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.StoreId, x.Code, x.Name, x.CreatedAtUtc, x.IsDeleted })
            .ToListAsync();

        return Json(items);
    }

    // 2) Xóa mềm category mới nhất
    [HttpGet("/dev/delete-latest-category")]
    public async Task<IActionResult> DeleteLatestCategory()
    {
        var item = await _db.Categories
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (item == null) return Content("No category");

        _db.Categories.Remove(item); // gọi Remove => SaveChanges sẽ soft delete
        await _db.SaveChangesAsync();

        return Json(new { item.Id, item.StoreId, item.Code, item.IsDeleted, item.DeletedAtUtc });
    }

    // 3) Xem list category (để test filter theo store)
    [HttpGet("/dev/categories")]
    public async Task<IActionResult> List()
    {
        var items = await _db.Categories
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StoreId, x.Code, x.Name, x.IsDeleted })
            .ToListAsync();

        return Json(items);
    }
}
