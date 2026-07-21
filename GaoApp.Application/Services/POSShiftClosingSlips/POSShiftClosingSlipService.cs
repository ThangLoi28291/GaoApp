using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShiftClosingSlips;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.POSShiftClosingSlips;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.POSShiftClosingSlips;

public class POSShiftClosingSlipService : IPOSShiftClosingSlipService
{
    private readonly ICurrentStore _currentStore;
    private readonly IPOSShiftRepository _shiftRepo;
    private readonly IPOSShiftClosingSlipRepository _closingSlipRepo;
    private readonly IUserRepository _userRepo;

    public POSShiftClosingSlipService(
        ICurrentStore currentStore,
        IPOSShiftRepository shiftRepo,
        IPOSShiftClosingSlipRepository closingSlipRepo,
        IUserRepository userRepo)
    {
        _currentStore = currentStore;
        _shiftRepo = shiftRepo;
        _closingSlipRepo = closingSlipRepo;
        _userRepo = userRepo;
    }

    public async Task<POSShiftClosingSlipDto> CreateFromClosedShiftAsync(
        int shiftId,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var existed = await _closingSlipRepo.GetByShiftIdAsync(storeId, shiftId, ct);
        if (existed != null)
            return await MapAsync(existed, ct);

        var shift = await _shiftRepo.GetByIdAsync(shiftId, ct);
        if (shift == null || shift.StoreId != storeId)
            throw new InvalidOperationException("Không tìm thấy ca POS cần tạo phiếu bàn giao.");

        if (shift.Status != POSShiftStatus.Closed)
            throw new InvalidOperationException("Chỉ tạo phiếu bàn giao cho ca đã đóng.");

        if (!shift.ClosingCashActual.HasValue)
            throw new InvalidOperationException("Ca chưa có tiền thực đếm cuối ca.");

        var slipCode = await GenerateSlipCodeAsync(storeId, ct);

        var slip = new POSShiftClosingSlip
        {
            StoreId = storeId,
            POSShiftId = shift.Id,
            SlipCode = slipCode,
            BarcodeValue = slipCode,

            OpenedByUserId = shift.OpenedByUserId,
            ClosedByUserId = shift.ClosedByUserId ?? shift.OpenedByUserId,

            OpenedAtUtc = shift.OpenedAtUtc,
            ClosedAtUtc = shift.ClosedAtUtc ?? DateTime.UtcNow,

            OpeningCash = shift.OpeningCash,
            CashSalesTotal = shift.CashSalesTotal,
            NonCashSalesTotal = shift.NonCashSalesTotal,
            CashRefundTotal = shift.CashRefundTotal,
            NonCashRefundTotal = shift.NonCashRefundTotal,
            RefundCount = shift.RefundCount,
            VoidCount = shift.VoidCount,
            CashInTotal = shift.CashInTotal,
            CashOutTotal = shift.CashOutTotal,
            ClosingCashExpected = shift.ClosingCashExpected,
            ClosingCashActual = shift.ClosingCashActual.Value,
            CashDifference = shift.ClosingCashActual.Value - shift.ClosingCashExpected,
            CloseNote = shift.CloseNote
        };

        var closingDenoms = shift.CashDenominations
            .Where(x =>
                !x.IsDeleted &&
                x.EntryType == POSShiftCashDenominationEntryType.Closing &&
                x.Quantity > 0)
            .OrderByDescending(x => x.DenominationValue)
            .ToList();

        foreach (var d in closingDenoms)
        {
            var item = new POSShiftClosingSlipDenomination
            {
                StoreId = storeId,
                POSShiftClosingSlip = slip,
                DenominationValue = d.DenominationValue,
                Quantity = d.Quantity
            };

            item.Recalc();
            slip.Denominations.Add(item);
        }

        await _closingSlipRepo.AddAsync(slip, ct);
        await _closingSlipRepo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    public async Task<POSShiftClosingSlipDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var slip = await _closingSlipRepo.GetByIdAsync(_currentStore.StoreId, id, ct);
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<POSShiftClosingSlipDto?> GetByShiftIdAsync(int shiftId, CancellationToken ct = default)
    {
        var slip = await _closingSlipRepo.GetByShiftIdAsync(_currentStore.StoreId, shiftId, ct);
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<POSShiftClosingSlipDto> MarkPrintedAsync(int id, CancellationToken ct = default)
    {
        var slip = await _closingSlipRepo.GetByIdAsync(_currentStore.StoreId, id, ct);

        if (slip == null)
            throw new InvalidOperationException("Không tìm thấy phiếu bàn giao cuối ca.");

        slip.MarkPrinted();

        await _closingSlipRepo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    private async Task<string> GenerateSlipCodeAsync(int storeId, CancellationToken ct)
    {
        var today = DateTime.Now.ToString("yyyyMMdd");

        for (var i = 1; i <= 9999; i++)
        {
            var code = $"CLS-{today}-{i:0000}";

            if (!await _closingSlipRepo.ExistsSlipCodeAsync(storeId, code, ct))
                return code;
        }

        throw new InvalidOperationException("Không thể sinh mã phiếu bàn giao cuối ca.");
    }

    private async Task<string?> ResolveUserNameAsync(int userId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        return user?.UserName ?? $"User #{userId}";
    }

    private async Task<POSShiftClosingSlipDto> MapAsync(
        POSShiftClosingSlip slip,
        CancellationToken ct)
    {
        return new POSShiftClosingSlipDto
        {
            Id = slip.Id,
            POSShiftId = slip.POSShiftId,
            SlipCode = slip.SlipCode,
            BarcodeValue = slip.BarcodeValue,
            Status = slip.Status,
            ShiftCode = slip.POSShift?.ShiftCode,

            OpenedByUserId = slip.OpenedByUserId,
            OpenedByUserName = await ResolveUserNameAsync(slip.OpenedByUserId, ct),

            ClosedByUserId = slip.ClosedByUserId,
            ClosedByUserName = await ResolveUserNameAsync(slip.ClosedByUserId, ct),

            OpenedAtUtc = slip.OpenedAtUtc,
            ClosedAtUtc = slip.ClosedAtUtc,

            OpeningCash = slip.OpeningCash,
            CashSalesTotal = slip.CashSalesTotal,
            NonCashSalesTotal = slip.NonCashSalesTotal,
            CashRefundTotal = slip.CashRefundTotal,
            NonCashRefundTotal = slip.NonCashRefundTotal,
            RefundCount = slip.RefundCount,
            VoidCount = slip.VoidCount,
            CashInTotal = slip.CashInTotal,
            CashOutTotal = slip.CashOutTotal,
            ClosingCashExpected = slip.ClosingCashExpected,
            ClosingCashActual = slip.ClosingCashActual,
            CashDifference = slip.CashDifference,
            CloseNote = slip.CloseNote,
            PrintedAtUtc = slip.PrintedAtUtc,

            Denominations = slip.Denominations
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.DenominationValue)
                .Select(x => new POSShiftClosingSlipDenominationDto
                {
                    DenominationValue = x.DenominationValue,
                    Quantity = x.Quantity,
                    Amount = x.Amount
                })
                .ToList()
        };
    }
}