using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Services.Rewards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/customers")]
[ApiController]
public sealed class CustomerRewardsController : ControllerBase
{
    private readonly ICustomerRewardService _customerRewardService;
    private readonly IOrderRewardCalculator _orderRewardCalculator;

    public CustomerRewardsController(
       ICustomerRewardService customerRewardService,
       IOrderRewardCalculator orderRewardCalculator)
    {
        _customerRewardService = customerRewardService;
        _orderRewardCalculator = orderRewardCalculator;
    }

    /// <summary>
    /// Test đọc số dư tích điểm của khách hàng.
    /// 
    /// Lưu ý:
    /// - BalanceAmount là tiền tích lũy hợp lệ.
    /// - AvailablePoints = BalanceAmount / MoneyPerPoint.
    /// - AvailableVoucherCount = AvailablePoints / PointsPerVoucher.
    /// </summary>
    [HttpGet("{customerId:int}/reward-balance")]
    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    public async Task<IActionResult> GetRewardBalance(
        int customerId,
        CancellationToken ct)
    {
        var result = await _customerRewardService.GetBalanceAsync(customerId, ct);
        return Ok(result);
    }
    [HttpPost("manual-ledger")]
   
    public async Task<IActionResult> CreateManualLedger(
    [FromBody] CreateManualRewardLedgerRequest request,
    CancellationToken ct)
    {
        var result = await _customerRewardService.CreateManualLedgerAsync(request, ct);
        return Ok(result);
    }
    [HttpGet("{customerId:int}/reward-vouchers/available")]

    public async Task<IActionResult> GetAvailableVouchers(
    int customerId,
    CancellationToken ct)
    {
        var result = await _customerRewardService.GetAvailableVouchersAsync(customerId, ct);
        return Ok(result);
    }
    [HttpPost("redeem-voucher")]

    public async Task<IActionResult> RedeemVoucher(
    [FromBody] RedeemRewardVoucherRequest request,
    CancellationToken ct)
    {
        var result = await _customerRewardService.RedeemVoucherAsync(request, ct);
        return Ok(result);
    }
    [HttpGet("/admin/api/orders/{orderId:int}/reward-calculation")]

    public async Task<IActionResult> CalculateOrderReward(
    int orderId,
    CancellationToken ct)
    {
        var result = await _orderRewardCalculator.CalculateAsync(orderId, ct);
        return Ok(result);
    }
    [HttpGet("{customerId:int}/reward-summary")]
    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    public async Task<IActionResult> GetRewardSummary(
    int customerId,
    CancellationToken ct)
    {
        var result = await _customerRewardService.GetSummaryAsync(customerId, ct);
        return Ok(result);
    }
    [HttpGet("reward-vouchers")]
    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    public async Task<IActionResult> GetVoucherList(
    [FromQuery] CustomerRewardVoucherFilterDto filter,
    CancellationToken ct)
    {
        var result = await _customerRewardService.GetVoucherListAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("reward-vouchers/status-counts")]
   
    public async Task<IActionResult> GetVoucherStatusCounts(CancellationToken ct)
    {
        var result = await _customerRewardService.GetVoucherStatusCountsAsync(ct);
        return Ok(result);
    }

    [HttpGet("reward-vouchers/{id:int}")]
    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    public async Task<IActionResult> GetVoucherDetail(
        int id,
        CancellationToken ct)
    {
        var result = await _customerRewardService.GetVoucherDetailAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("reward-vouchers/{id:int}/cancel")]
   
    public async Task<IActionResult> CancelVoucher(
        int id,
        [FromBody] CancelCustomerRewardVoucherRequest request,
        CancellationToken ct)
    {
        var result = await _customerRewardService.CancelVoucherAsync(id, request, ct);
        return Ok(result);
    }
    [HttpPost("reward-vouchers/{id:int}/lock")]
    public async Task<IActionResult> LockVoucher(
    int id,
    [FromBody] LockCustomerRewardVoucherRequest request,
    CancellationToken ct)
    {
        var result = await _customerRewardService.LockVoucherAsync(id, request, ct);
        return Ok(result);
    }

    [HttpPost("reward-vouchers/{id:int}/unlock")]
    public async Task<IActionResult> UnlockVoucher(
        int id,
        [FromBody] UnlockCustomerRewardVoucherRequest request,
        CancellationToken ct)
    {
        var result = await _customerRewardService.UnlockVoucherAsync(id, request, ct);
        return Ok(result);
    }

    [HttpGet("reward-vouchers/{id:int}/logs")]
    public async Task<IActionResult> GetVoucherLogs(
        int id,
        CancellationToken ct)
    {
        var result = await _customerRewardService.GetVoucherLogsAsync(id, ct);
        return Ok(result);
    }

    [HttpGet("reward-vouchers/{id:int}/print")]
    public async Task<IActionResult> GetVoucherPrint(
        int id,
        CancellationToken ct)
    {
        var result = await _customerRewardService.GetVoucherPrintAsync(id, ct);
        return Ok(result);
    }
    [HttpGet("reward-vouchers/lookup")]
    public async Task<IActionResult> LookupVoucher(
    [FromQuery] string code,
    CancellationToken ct)
    {
        var result = await _customerRewardService.LookupVoucherByCodeAsync(code, ct);
        return Ok(result);
    }
}