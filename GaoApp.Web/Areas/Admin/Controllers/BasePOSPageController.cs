using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Areas.Admin.ViewModels.POS;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public abstract class BasePOSPageController : BaseAdminController
{
    private readonly IPOSRuntimeContextAccessor _runtimeContext;
    private readonly IPOSShiftService _posShiftService;

    protected BasePOSPageController(
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
    {
        _runtimeContext = runtimeContext;
        _posShiftService = posShiftService;
    }

    protected async Task BindPOSHeaderContextAsync(CancellationToken ct = default)
    {
        var vm = new POSHeaderContextViewModel
        {
            StoreId = _runtimeContext.StoreId,
            StoreName = string.IsNullOrWhiteSpace(_runtimeContext.StoreName)
                ? $"Store #{_runtimeContext.StoreId ?? 0}"
                : _runtimeContext.StoreName!,
            TerminalId = _runtimeContext.TerminalId,
            TerminalName = string.IsNullOrWhiteSpace(_runtimeContext.TerminalName)
                ? "Chưa xác định terminal"
                : _runtimeContext.TerminalName!,
            TerminalCode = _runtimeContext.TerminalCode,
            UserId = _runtimeContext.UserId,
            UserName = string.IsNullOrWhiteSpace(_runtimeContext.UserName)
                ? "Chưa xác định nhân viên"
                : _runtimeContext.UserName!
        };

        try
        {
            var shift = await _posShiftService.GetCurrentOpenAsync(ct);

            if (shift != null)
            {
                vm.ShiftId = shift.Id;
                vm.ShiftCode = !string.IsNullOrWhiteSpace(shift.ShiftCode)
                    ? shift.ShiftCode
                    : $"SHIFT-{shift.Id}";
                vm.ShiftStatusText = shift.Status.ToString();
                vm.WarehouseName = shift.WarehouseName;
                vm.OpenedAtUtc = shift.OpenedAtUtc;
            }
        }
        catch
        {
            // Không chặn render page chỉ vì không load được ca hiện tại
            vm.ShiftStatusText = "NoShift";
            vm.ShiftCode = "Chưa mở ca";
        }

        ViewBag.POSHeaderContext = vm;
    }
}