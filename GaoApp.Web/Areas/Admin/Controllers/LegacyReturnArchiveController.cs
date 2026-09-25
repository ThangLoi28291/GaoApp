using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/legacy-returns")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
public sealed class LegacyReturnArchiveController(AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default)
    {
        if (db.CurrentStoreId is not int storeId) return Forbid();
        page = Math.Clamp(page, 1, 1000000);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 200) return BadRequest("Nội dung tìm kiếm tối đa 200 ký tự.");
        // All filters are SQL parameters, including free text. CHARINDEX treats %/_ literally.
        var total = await db.Database.SqlQuery<long>($"""
            SELECT COUNT_BIG(*) AS Value FROM dbo.LegacyReturnArchives
            WHERE StoreId={storeId} AND ({search} IS NULL
                OR CONVERT(nvarchar(30),LegacyOrderId)={search}
                OR CHARINDEX({search},CustomerName)>0 OR CHARINDEX({search},EmployeeName)>0)
            """).SingleAsync(ct);
        var offset = (page - 1) * LegacyReturnArchivePage.PageSize;
        var rows = await db.Database.SqlQuery<LegacyReturnArchiveRow>($"""
            SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName,LegacyUserId,EmployeeName,SourceTotal,SourcePaymentFlag
            FROM dbo.LegacyReturnArchives
            WHERE StoreId={storeId} AND ({search} IS NULL
                OR CONVERT(nvarchar(30),LegacyOrderId)={search}
                OR CHARINDEX({search},CustomerName)>0 OR CHARINDEX({search},EmployeeName)>0)
            ORDER BY OccurredAtUtc DESC,LegacyOrderId DESC OFFSET {offset} ROWS FETCH NEXT {LegacyReturnArchivePage.PageSize} ROWS ONLY
            """).ToListAsync(ct);
        return View(new LegacyReturnArchivePage(rows, total, page, search));
    }

    [HttpGet("{legacyOrderId:long}")]
    public async Task<IActionResult> Details(long legacyOrderId, CancellationToken ct)
    {
        if (db.CurrentStoreId is not int storeId) return Forbid();
        var item = await db.Database.SqlQuery<LegacyReturnArchiveDetail>($"""
            SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName,LegacyUserId,EmployeeName,SourceTotal,SourcePaymentFlag,HeaderJson,DetailsJson
            FROM dbo.LegacyReturnArchives WHERE StoreId={storeId} AND LegacyOrderId={legacyOrderId}
            """).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : View(item);
    }
}
