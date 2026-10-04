using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Pos.Shift.View), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/pos-shift/payment-adjustments")]
public sealed class POSPaymentAdjustmentsController(IPOSPaymentAdjustmentService service, IPOSCashAdjustmentService cash) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => LocalRedirect(POSRequestNavigation.FromLegacy(Request.Query, "payment"));
    [HttpGet("payments")]
    public async Task<IActionResult> Payments([Range(1,100000000)] int page=1, [StringLength(200)] string? keyword=null,
        int? paymentId=null, int? orderId=null, int? shiftId=null, CancellationToken ct=default)
        => !ModelState.IsValid?BadRequest(new {message="Bộ lọc không hợp lệ."}):Ok(await service.PaymentsAsync(page,keyword,paymentId,orderId,shiftId,ct));
    [HttpGet("data")]
    public async Task<IActionResult> List([Range(1,100000000)] int page=1, string? status=null, int? shiftId=null, CancellationToken ct=default)
        => !ModelState.IsValid?BadRequest(new {message="Bộ lọc không hợp lệ."}):Ok(await service.ListAsync(page,status,shiftId,ct));
    [HttpGet("deposits")]
    public async Task<IActionResult> Deposits([Range(1,100000000)] int page=1, [StringLength(200)] string? keyword=null,
        int? entryId=null, int? shiftId=null, CancellationToken ct=default)
        => !ModelState.IsValid?BadRequest(new {message="Bộ lọc không hợp lệ."}):Ok(await service.DepositsAsync(page,keyword,entryId,shiftId,ct));
    [HttpGet("deposits/{id:int}/preview")]
    public async Task<IActionResult> PreviewDeposit(int id,GaoApp.Domain.Enums.PaymentMethod method,CancellationToken ct)
        => !ModelState.IsValid?BadRequest(new {message="Phương thức không hợp lệ."}):Ok(await service.PreviewDepositAsync(id,method,ct));
    [HttpPost("deposits/{id:int}/requests"), Authorize(Policy=PermissionCodes.Pos.Shift.Reconcile)]
    public async Task<IActionResult> CreateDeposit(int id,[FromBody] CreatePaymentAdjustmentRequest request,CancellationToken ct)
        => !ModelState.IsValid?BadRequest(new {message="Kiểm tra phương thức, mã giao dịch và lý do đề nghị."}):Ok(new {id=await service.CreateDepositAsync(id,request,ct)});
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id,CancellationToken ct)=>Ok(await service.DetailAsync(id,ct));
    [HttpGet("payments/{id:int}/preview")]
    public async Task<IActionResult> Preview(int id,GaoApp.Domain.Enums.PaymentMethod method,CancellationToken ct)
        => !ModelState.IsValid?BadRequest(new {message="Phương thức không hợp lệ."}):Ok(await service.PreviewAsync(id,method,ct));
    [HttpPost("payments/{id:int}/requests"), Authorize(Policy=PermissionCodes.Pos.Shift.Reconcile)]
    public async Task<IActionResult> Create(int id,[FromBody] CreatePaymentAdjustmentRequest request,CancellationToken ct)
        => !ModelState.IsValid?BadRequest(new {message="Kiểm tra phương thức, mã giao dịch và lý do đề nghị."}):Ok(new {id=await service.CreateAsync(id,request,ct)});
    [HttpPost("{id:int}/{actionName}")]
    public async Task<IActionResult> Decide(int id,string actionName,[FromBody] PaymentAdjustmentDecision request,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest(new {message="Kiểm tra phiên bản dữ liệu và ghi chú xử lý."});
        await service.DecideAsync(id,actionName,request,ct);return Ok(new {success=true});
    }
    [HttpPost("shifts/{id:int}/reconcile")]
    public async Task<IActionResult> Reconcile(int id,[FromBody] CashAdjustmentDecision request,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest(new {message="Kiểm tra phiên bản ca và ghi chú đối soát."});
        await cash.ReconcileAsync(id,request,ct);return Ok(new {success=true});
    }
}
