using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Rewards;

public sealed class CustomerRewardService : ICustomerRewardService
{
    private readonly ICustomerRewardLedgerRepository _ledgerRepository;
    private readonly IRewardSettingsRepository _settingsRepository;
    private readonly ICustomerRewardVoucherRepository _voucherRepository;
    public CustomerRewardService(
     ICustomerRewardLedgerRepository ledgerRepository,
     IRewardSettingsRepository settingsRepository,
     ICustomerRewardVoucherRepository voucherRepository)
    {
        _ledgerRepository = ledgerRepository;
        _settingsRepository = settingsRepository;
        _voucherRepository = voucherRepository;
    }

    public async Task<CustomerRewardBalanceDto> GetBalanceAsync(
        int customerId,
        CancellationToken ct = default)
    {
        if (customerId <= 0)
            throw new InvalidOperationException("Khách hàng không hợp lệ.");

        var settings = await _settingsRepository.GetCurrentAsync(ct)
            ?? throw new InvalidOperationException("Chưa cấu hình tích điểm cho cửa hàng.");

        if (!settings.IsEnabled)
            throw new InvalidOperationException("Chức năng tích điểm đang tắt.");

        if (settings.MoneyPerPoint <= 0)
            throw new InvalidOperationException("Cấu hình tiền quy đổi điểm không hợp lệ.");

        if (settings.PointsPerVoucher <= 0)
            throw new InvalidOperationException("Cấu hình số điểm đổi phiếu không hợp lệ.");

        var balanceAmount = await _ledgerRepository.GetBalanceAmountAsync(customerId, ct);

        return new CustomerRewardBalanceDto
        {
            CustomerId = customerId,
            BalanceAmount = balanceAmount,
            MoneyPerPoint = settings.MoneyPerPoint,
            PointsPerVoucher = settings.PointsPerVoucher,
            VoucherValue = settings.VoucherValue
        };
    }
    public async Task<CustomerRewardBalanceDto> CreateManualLedgerAsync(
    CreateManualRewardLedgerRequest request,
    CancellationToken ct = default)
    {
        if (request.CustomerId <= 0)
            throw new InvalidOperationException("Khách hàng không hợp lệ.");

        if (request.Amount == 0)
            throw new InvalidOperationException("Số tiền điều chỉnh phải khác 0.");

        var ledger = new CustomerRewardLedger
        {
            CustomerId = request.CustomerId,
            Type = CustomerRewardLedgerType.ManualAdjust,
            Amount = request.Amount,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? "Điều chỉnh tích điểm thủ công"
                : request.Description.Trim()
        };

        await _ledgerRepository.AddAsync(ledger, ct);
        await _ledgerRepository.SaveChangesAsync(ct);

        return await GetBalanceAsync(request.CustomerId, ct);
    }
    public async Task<List<CustomerRewardVoucherDto>> GetAvailableVouchersAsync(
    int customerId,
    CancellationToken ct = default)
    {
        if (customerId <= 0)
            throw new InvalidOperationException("Khách hàng không hợp lệ.");

        var vouchers = await _voucherRepository.GetByCustomerAsync(
            customerId,
            GaoApp.Domain.Enums.CustomerRewardVoucherStatus.Available,
            ct);

        return vouchers.Select(x => new CustomerRewardVoucherDto
        {
            Id = x.Id,
            VoucherCode = x.VoucherCode,
            Value = x.Value,
            RequiredAmount = x.RequiredAmount,
            Status = x.Status.ToString(),
            IssuedAtUtc = x.IssuedAtUtc,
            UsedAtUtc = x.UsedAtUtc,
            Description = x.Description
        }).ToList();
    }
    public async Task<RedeemRewardVoucherResultDto> RedeemVoucherAsync(
    RedeemRewardVoucherRequest request,
    CancellationToken ct = default)
    {
        if (request.CustomerId <= 0)
            throw new InvalidOperationException("Khách hàng không hợp lệ.");

        if (request.VoucherCount <= 0)
            throw new InvalidOperationException("Số phiếu muốn đổi phải lớn hơn 0.");

        if (request.VoucherCount > 20)
            throw new InvalidOperationException("Không nên đổi quá 20 phiếu trong một lần.");

        var settings = await _settingsRepository.GetCurrentAsync(ct)
            ?? throw new InvalidOperationException("Chưa cấu hình tích điểm cho cửa hàng.");

        if (!settings.IsEnabled)
            throw new InvalidOperationException("Chức năng tích điểm đang tắt.");

        var requiredAmountPerVoucher = settings.MoneyPerPoint * settings.PointsPerVoucher;
        var totalRequiredAmount = requiredAmountPerVoucher * request.VoucherCount;

        if (requiredAmountPerVoucher <= 0)
            throw new InvalidOperationException("Cấu hình đổi phiếu không hợp lệ.");

        var currentBalance = await GetBalanceAsync(request.CustomerId, ct);

        if (currentBalance.BalanceAmount < totalRequiredAmount)
        {
            throw new InvalidOperationException(
                $"Khách chưa đủ điểm. Cần {totalRequiredAmount:N0}đ tích lũy để đổi {request.VoucherCount} phiếu.");
        }

        var createdVouchers = new List<CustomerRewardVoucherDto>();

        var baseCode = await _voucherRepository.GenerateNextVoucherCodeAsync(ct);

        var prefix = $"RV-{DateTime.Now:yyyyMMdd}-";

        var startNumber = 1;
        var parts = baseCode.Split('-');
        if (parts.Length >= 3 && int.TryParse(parts[^1], out var parsed))
        {
            startNumber = parsed;
        }

        for (var i = 0; i < request.VoucherCount; i++)
        {
            var voucherCode = $"{prefix}{startNumber + i:0000}";

            var voucher = new CustomerRewardVoucher
            {
                CustomerId = request.CustomerId,
                VoucherCode = voucherCode,
                Value = settings.VoucherValue,
                RequiredAmount = requiredAmountPerVoucher,
                Status = GaoApp.Domain.Enums.CustomerRewardVoucherStatus.Available,
                IssuedAtUtc = DateTime.UtcNow,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"Đổi {settings.PointsPerVoucher} điểm lấy phiếu {settings.VoucherValue:N0}đ"
                    : request.Description.Trim()
            };

            await _voucherRepository.AddAsync(voucher, ct);

            createdVouchers.Add(new CustomerRewardVoucherDto
            {
                VoucherCode = voucher.VoucherCode,
                Value = voucher.Value,
                RequiredAmount = voucher.RequiredAmount,
                Status = voucher.Status.ToString(),
                IssuedAtUtc = voucher.IssuedAtUtc,
                Description = voucher.Description
            });
        }

        var ledger = new CustomerRewardLedger
        {
            CustomerId = request.CustomerId,
            Type = GaoApp.Domain.Enums.CustomerRewardLedgerType.VoucherRedeemed,
            Amount = -totalRequiredAmount,
            Description = $"Đổi {request.VoucherCount} phiếu tích điểm, trừ {totalRequiredAmount:N0}đ tích lũy"
        };

        await _ledgerRepository.AddAsync(ledger, ct);
        await _ledgerRepository.SaveChangesAsync(ct);

        var balanceAfter = await GetBalanceAsync(request.CustomerId, ct);

        return new RedeemRewardVoucherResultDto
        {
            CustomerId = request.CustomerId,
            CreatedVoucherCount = request.VoucherCount,
            DeductedAmount = totalRequiredAmount,
            VoucherValue = settings.VoucherValue,
            Balance = balanceAfter,
            CreatedVouchers = createdVouchers
        };
    }
    public async Task<CustomerRewardSummaryDto> GetSummaryAsync(
    int customerId,
    CancellationToken ct = default)
    {
        var balance = await GetBalanceAsync(customerId, ct);

        var availableVouchers = await _voucherRepository.GetByCustomerAsync(
            customerId,
            GaoApp.Domain.Enums.CustomerRewardVoucherStatus.Available,
            ct);

        var availableVoucherCount = availableVouchers.Count;
        var availableVoucherValue = availableVouchers.Sum(x => x.Value);

        return new CustomerRewardSummaryDto
        {
            CustomerId = customerId,

            BalanceAmount = balance.BalanceAmount,
            AvailablePoints = balance.AvailablePoints,

            RedeemableVoucherCount = balance.AvailableVoucherCount,
            RedeemableVoucherValue = balance.AvailableVoucherCount * balance.VoucherValue,

            AvailableVoucherCount = availableVoucherCount,
            AvailableVoucherValue = availableVoucherValue,

            MoneyPerPoint = balance.MoneyPerPoint,
            PointsPerVoucher = balance.PointsPerVoucher,
            VoucherValue = balance.VoucherValue
        };
    }
    public async Task<PagedResult<CustomerRewardVoucherListItemDto>> GetVoucherListAsync(
    CustomerRewardVoucherFilterDto filter,
    CancellationToken ct = default)
    {
        return await _voucherRepository.GetListAsync(filter, ct);
    }

    public async Task<CustomerRewardVoucherDetailDto> GetVoucherDetailAsync(
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            throw new InvalidOperationException("Voucher không hợp lệ.");

        var voucher = await _voucherRepository.GetDetailAsync(id, ct);

        if (voucher == null)
            throw new InvalidOperationException("Không tìm thấy voucher.");

        return voucher;
    }

    public async Task<CustomerRewardVoucherDetailDto> CancelVoucherAsync(
        int id,
        CancelCustomerRewardVoucherRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
            throw new InvalidOperationException("Voucher không hợp lệ.");

        var voucher = await _voucherRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy voucher.");

        if (voucher.Status != CustomerRewardVoucherStatus.Available)
            throw new InvalidOperationException("Chỉ được hủy voucher chưa sử dụng.");

        voucher.Status = CustomerRewardVoucherStatus.Cancelled;

        var reason = (request.Reason ?? "").Trim();

        var nowText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        voucher.Description = AppendDescription(
            voucher.Description,
            string.IsNullOrWhiteSpace(reason)
                ? $"[HỦY VOUCHER - {nowText}]"
                : $"[HỦY VOUCHER - {nowText}] Lý do: {reason}");

        await _voucherRepository.SaveChangesAsync(ct);

        return await GetVoucherDetailAsync(id, ct);
    }

    public async Task<Dictionary<string, int>> GetVoucherStatusCountsAsync(
        CancellationToken ct = default)
    {
        var raw = await _voucherRepository.CountByStatusAsync(ct);

        return Enum.GetValues<CustomerRewardVoucherStatus>()
            .ToDictionary(
                x => x.ToString(),
                x => raw.TryGetValue(x, out var count) ? count : 0);
    }
    public async Task<CustomerRewardVoucherDetailDto> LockVoucherAsync(
    int id,
    LockCustomerRewardVoucherRequest request,
    CancellationToken ct = default)
    {
        if (id <= 0)
            throw new InvalidOperationException("Voucher không hợp lệ.");

        var voucher = await _voucherRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy voucher.");

        if (voucher.Status != CustomerRewardVoucherStatus.Available)
            throw new InvalidOperationException("Chỉ được khóa voucher đang khả dụng.");

        var reason = (request?.Reason ?? string.Empty).Trim();
        var nowText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        voucher.Status = CustomerRewardVoucherStatus.Locked;

        voucher.Description = AppendDescription(
            voucher.Description,
            string.IsNullOrWhiteSpace(reason)
                ? $"[KHÓA VOUCHER - {nowText}]"
                : $"[KHÓA VOUCHER - {nowText}] Lý do: {reason}");

        await _voucherRepository.SaveChangesAsync(ct);

        return await GetVoucherDetailAsync(id, ct);
    }

    public async Task<CustomerRewardVoucherDetailDto> UnlockVoucherAsync(
        int id,
        UnlockCustomerRewardVoucherRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
            throw new InvalidOperationException("Voucher không hợp lệ.");

        var voucher = await _voucherRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy voucher.");

        if (voucher.Status != CustomerRewardVoucherStatus.Locked)
            throw new InvalidOperationException("Chỉ được mở khóa voucher đang bị khóa.");

        var reason = (request?.Reason ?? string.Empty).Trim();
        var nowText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        voucher.Status = CustomerRewardVoucherStatus.Available;

        voucher.Description = AppendDescription(
            voucher.Description,
            string.IsNullOrWhiteSpace(reason)
                ? $"[MỞ KHÓA VOUCHER - {nowText}]"
                : $"[MỞ KHÓA VOUCHER - {nowText}] Lý do: {reason}");

        await _voucherRepository.SaveChangesAsync(ct);

        return await GetVoucherDetailAsync(id, ct);
    }

    public async Task<List<CustomerRewardVoucherLogDto>> GetVoucherLogsAsync(
        int id,
        CancellationToken ct = default)
    {
        var voucher = await _voucherRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy voucher.");

        var logs = new List<CustomerRewardVoucherLogDto>();

        logs.Add(new CustomerRewardVoucherLogDto
        {
            CreatedAtUtc = voucher.IssuedAtUtc,
            Action = "Issued",
            OrderId = null,
            Reason = null,
            Note = "Phát hành voucher"
        });

        if (voucher.UsedAtUtc.HasValue)
        {
            logs.Add(new CustomerRewardVoucherLogDto
            {
                CreatedAtUtc = voucher.UsedAtUtc.Value,
                Action = "Used",
                OrderId = voucher.UsedOrderId,
                Reason = null,
                Note = "Voucher đã được sử dụng"
            });
        }

        if (!string.IsNullOrWhiteSpace(voucher.Description))
        {
            logs.Add(new CustomerRewardVoucherLogDto
            {
                CreatedAtUtc = voucher.UpdatedAtUtc ?? voucher.CreatedAtUtc,
                Action = voucher.Status.ToString(),
                OrderId = voucher.UsedOrderId,
                Reason = null,
                Note = voucher.Description
            });
        }

        return logs
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<CustomerRewardVoucherPrintDto> GetVoucherPrintAsync(
        int id,
        CancellationToken ct = default)
    {
        var detail = await GetVoucherDetailAsync(id, ct);

        return new CustomerRewardVoucherPrintDto
        {
            Id = detail.Id,
            VoucherCode = detail.VoucherCode,
            CustomerName = detail.CustomerName,
            CustomerPhone = detail.CustomerPhone,
            Value = detail.Value,
            IssuedAtUtc = detail.IssuedAtUtc,
            ExpiredAtUtc = null,
            Status = detail.Status,
            Description = detail.Description
        };
    }
    public async Task<CustomerRewardVoucherDetailDto> LookupVoucherByCodeAsync(
    string code,
    CancellationToken ct = default)
    {
        code = (code ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Mã voucher không được để trống.");

        var voucher = await _voucherRepository.GetByCodeAsync(code, ct)
            ?? throw new InvalidOperationException("Không tìm thấy voucher.");

        return new CustomerRewardVoucherDetailDto
        {
            Id = voucher.Id,
            VoucherCode = voucher.VoucherCode,
            CustomerId = voucher.CustomerId,
            CustomerName = voucher.Customer?.Name ?? "",
            CustomerPhone = voucher.Customer?.Phone,
            Value = voucher.Value,
            RequiredAmount = voucher.RequiredAmount,
            Status = voucher.Status.ToString(),
            IssuedAtUtc = voucher.IssuedAtUtc,
            UsedAtUtc = voucher.UsedAtUtc,
            UsedOrderId = voucher.UsedOrderId,
            Description = voucher.Description,
            ReferenceCode = voucher.ReferenceCode
        };
    }

    private const int VoucherDescriptionMaxLength = 500;

    private static string AppendDescription(string? current, string newText)
    {
        var value = string.IsNullOrWhiteSpace(current)
            ? newText.Trim()
            : $"{current.Trim()}{Environment.NewLine}{newText.Trim()}";

        if (value.Length <= VoucherDescriptionMaxLength)
            return value;

        return "..." + value[^Math.Min(VoucherDescriptionMaxLength - 3, value.Length)..];
    }

}