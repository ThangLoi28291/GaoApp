using System.Security.Cryptography;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.ViewModels.POSTerminals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("admin/pos-terminals")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class POSTerminalsController(AppDbContext db) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var terminals = await db.POSTerminals.AsNoTracking()
            .Where(x => x.StoreId == CurrentStoreId && !x.IsDeleted).OrderBy(x => x.Code)
            .Select(x => new POSTerminalRow(x.Id, x.Code, x.Name,
                x.IsActive && x.Status == POSTerminalStatus.Active, x.Description,
                x.Devices.Where(d => d.StoreId == CurrentStoreId && !d.IsDeleted)
                    .OrderByDescending(d => d.Id)
                    .Select(d => new POSDeviceRow(d.Id, d.DeviceName, d.IsActive, d.CreatedAtUtc)).ToList()))
            .ToListAsync(ct);
        return View(new POSTerminalIndexVm(terminals));
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new POSTerminalVm());

    [HttpPost("create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(POSTerminalVm vm, CancellationToken ct)
    {
        vm.Code = (vm.Code ?? "").Trim().ToUpperInvariant();
        vm.Name = (vm.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(vm.Name)) ModelState.AddModelError(nameof(vm.Name), "Vui lòng nhập tên máy POS.");
        if (!ModelState.IsValid) return View(vm);

        // Include deleted records because the unique index reserves their codes too.
        if (await db.POSTerminals.IgnoreQueryFilters().AnyAsync(x => x.StoreId == CurrentStoreId && x.Code == vm.Code, ct))
        {
            ModelState.AddModelError(nameof(vm.Code), "Mã máy đã tồn tại trong cửa hàng. Vui lòng dùng mã khác.");
            return View(vm);
        }
        var terminal = new POSTerminal
        {
            StoreId = CurrentStoreId, Code = vm.Code, Name = vm.Name,
            Description = vm.Description?.Trim(), IsActive = true, Status = POSTerminalStatus.Active
        };
        db.POSTerminals.Add(terminal);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(terminal).State = EntityState.Detached;
            ModelState.AddModelError(nameof(vm.Code), "Mã máy vừa được tạo. Vui lòng dùng mã khác.");
            return View(vm);
        }
        ToastSuccess($"Đã tạo máy {terminal.Code}. Bạn có thể cấp khóa thiết bị cho máy này.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{terminalId:int}/keys/create")]
    public async Task<IActionResult> CreateKey(int terminalId, CancellationToken ct)
    {
        var terminal = await FindActiveTerminalAsync(terminalId, ct);
        if (terminal is null) return NotFound();
        ViewBag.TerminalLabel = $"{terminal.Code} · {terminal.Name}";
        return View(new CreatePOSKeyVm { TerminalId = terminalId });
    }

    [HttpPost("{terminalId:int}/keys/create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateKey(int terminalId, CreatePOSKeyVm vm, CancellationToken ct)
    {
        var terminal = await FindActiveTerminalAsync(terminalId, ct);
        if (terminal is null) return NotFound();
        vm.TerminalId = terminalId;
        ViewBag.TerminalLabel = $"{terminal.Code} · {terminal.Name}";
        vm.DeviceName = (vm.DeviceName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(vm.DeviceName)) ModelState.AddModelError(nameof(vm.DeviceName), "Vui lòng nhập tên thiết bị.");
        if (!ModelState.IsValid) return View(vm);

        var device = new POSTerminalDevice
        {
            StoreId = CurrentStoreId, TerminalId = terminal.Id, DeviceName = vm.DeviceName,
            DeviceKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), IsActive = true
        };
        db.POSTerminalDevices.Add(device);
        await db.SaveChangesAsync(ct);
        // The key is shown only on the creation response, never in URLs or the device list.
        return View("KeyCreated", device);
    }

    [HttpPost("keys/{id:int}/revoke"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeKey(int id, CancellationToken ct)
    {
        var device = await db.POSTerminalDevices.SingleOrDefaultAsync(x =>
            x.Id == id && x.StoreId == CurrentStoreId && !x.IsDeleted, ct);
        if (device is null) return NotFound();
        device.IsActive = false;
        await db.SaveChangesAsync(ct);
        ToastSuccess("Đã thu hồi khóa máy. Khóa này không thể dùng để đăng nhập lại.");
        return RedirectToAction(nameof(Index));
    }

    private Task<POSTerminal?> FindActiveTerminalAsync(int id, CancellationToken ct) =>
        db.POSTerminals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.StoreId == CurrentStoreId &&
            !x.IsDeleted && x.IsActive && x.Status == POSTerminalStatus.Active, ct);
}
