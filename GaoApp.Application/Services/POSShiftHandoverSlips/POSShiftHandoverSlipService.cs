using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.POSShiftHandoverSlips;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.POSShiftHandoverSlips;

public class POSShiftHandoverSlipService : IPOSShiftHandoverSlipService
{
    private readonly IPOSShiftHandoverSlipRepository _repo;
    private readonly IWarehouseRepository _warehouseRepo;
    private readonly IUserRepository _userRepo;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public POSShiftHandoverSlipService(
        IPOSShiftHandoverSlipRepository repo,
        IWarehouseRepository warehouseRepo,
        IUserRepository userRepo,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _repo = repo;
        _warehouseRepo = warehouseRepo;
        _userRepo = userRepo;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public async Task<POSShiftHandoverSlipDto> CreateAsync(
        CreatePOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var userId = RequireUserId();

        if (request.WarehouseId <= 0)
            throw new InvalidOperationException("Vui lòng chọn kho bán hàng.");

        var warehouse = await _warehouseRepo.GetByIdAsync(request.WarehouseId, ct);
        if (warehouse == null || warehouse.StoreId != storeId || warehouse.IsDeleted)
            throw new InvalidOperationException("Kho bán hàng không tồn tại.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho bán hàng đã ngưng hoạt động.");

        var denomItems = (request.Denominations ?? new())
            .Where(x => x.DenominationValue > 0 && x.Quantity > 0)
            .GroupBy(x => x.DenominationValue)
            .Select(g =>
            {
                var item = new POSShiftHandoverSlipDenomination
                {
                    StoreId = storeId,
                    DenominationValue = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                };

                item.Recalc();
                return item;
            })
            .OrderByDescending(x => x.DenominationValue)
            .ToList();

        if (!denomItems.Any())
            throw new InvalidOperationException("Vui lòng nhập ít nhất một mệnh giá tiền.");

        var slipCode = await GenerateSlipCodeAsync(storeId, ct);

        var slip = new POSShiftHandoverSlip
        {
            StoreId = storeId,
            SlipCode = slipCode,
            BarcodeValue = slipCode,
            Status = POSShiftHandoverSlipStatus.Draft,
            TerminalId = request.TerminalId,
            WarehouseId = request.WarehouseId,
            CreatedByUserId = userId,
            AssignedToUserId = request.AssignedToUserId,
            Note = request.Note?.Trim()
        };

        foreach (var item in denomItems)
        {
            slip.Denominations.Add(item);
        }

        slip.RecalcTotal();

        await _repo.AddAsync(slip, ct);
        await _repo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlipDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var slip = await _repo.GetByIdAsync(_currentStore.StoreId, id, ct);
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlipDto?> GetByBarcodeAsync(string barcodeValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
            return null;

        var slip = await _repo.GetByBarcodeAsync(_currentStore.StoreId, barcodeValue.Trim(), ct);
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<PagedResult<POSShiftHandoverSlipDto>> QueryAsync(
        QueryPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        var result = await _repo.QueryAsync(
            _currentStore.StoreId,
            request.Status,
            request.TerminalId,
            request.WarehouseId,
            request.AssignedToUserId,
            request.Keyword,
            request.FromUtc,
            request.ToUtcExclusive,
            request.Page,
            request.PageSize,
            ct);

        var items = new List<POSShiftHandoverSlipDto>();

        foreach (var slip in result.Items)
        {
            items.Add(await MapAsync(slip, ct));
        }

        return new PagedResult<POSShiftHandoverSlipDto>
        {
            Items = items,
            TotalItems = result.Total,
            Page = request.Page <= 0 ? 1 : request.Page,
            PageSize = request.PageSize <= 0 ? 20 : request.PageSize
        };
    }

    public async Task<POSShiftHandoverSlipDto> MarkPrintedAsync(int id, CancellationToken ct = default)
    {
        var slip = await RequireSlipAsync(id, ct);

        slip.MarkPrinted();

        await _repo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlipDto> CancelAsync(
        int id,
        CancelPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        var slip = await RequireSlipAsync(id, ct);

        slip.Cancel(RequireUserId(), request.Reason);

        await _repo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    private async Task<POSShiftHandoverSlip> RequireSlipAsync(int id, CancellationToken ct)
    {
        var slip = await _repo.GetByIdAsync(_currentStore.StoreId, id, ct);

        if (slip == null)
            throw new InvalidOperationException("Không tìm thấy phiếu nhận ca.");

        return slip;
    }

    private int RequireUserId()
    {
        if (_currentUser.UserId.HasValue && _currentUser.UserId.Value > 0)
            return _currentUser.UserId.Value;

        throw new InvalidOperationException("Bạn chưa đăng nhập hoặc phiên đăng nhập không hợp lệ.");
    }

    private async Task<string> GenerateSlipCodeAsync(int storeId, CancellationToken ct)
    {
        var today = DateTime.Now.ToString("yyyyMMdd");

        for (var i = 1; i <= 9999; i++)
        {
            var code = $"HOS-{today}-{i:0000}";

            if (!await _repo.ExistsSlipCodeAsync(storeId, code, ct))
                return code;
        }

        throw new InvalidOperationException("Không thể sinh mã phiếu nhận ca trong ngày.");
    }

    private async Task<string?> ResolveUserNameAsync(int? userId, CancellationToken ct)
    {
        if (!userId.HasValue || userId.Value <= 0)
            return null;

        var user = await _userRepo.GetByIdAsync(userId.Value, ct);
        return user?.FullName ?? user?.UserName ?? $"User #{userId.Value}";
    }

    private async Task<POSShiftHandoverSlipDto> MapAsync(
        POSShiftHandoverSlip slip,
        CancellationToken ct)
    {
        return new POSShiftHandoverSlipDto
        {
            Id = slip.Id,
            SlipCode = slip.SlipCode,
            BarcodeValue = slip.BarcodeValue,
            Status = slip.Status,

            TerminalId = slip.TerminalId,
            TerminalCode = slip.Terminal?.Code,
            TerminalName = slip.Terminal?.Name,

            WarehouseId = slip.WarehouseId,
            WarehouseCode = slip.Warehouse?.Code,
            WarehouseName = slip.Warehouse?.Name,

            CreatedByUserId = slip.CreatedByUserId,
            CreatedByUserName = await ResolveUserNameAsync(slip.CreatedByUserId, ct),

            AssignedToUserId = slip.AssignedToUserId,
            AssignedToUserName = await ResolveUserNameAsync(slip.AssignedToUserId, ct),

            OpeningCashTotal = slip.OpeningCashTotal,

            UsedPOSShiftId = slip.UsedPOSShiftId,
            UsedPOSShiftCode = slip.UsedPOSShift?.ShiftCode,

            PrintedAtUtc = slip.PrintedAtUtc,
            UsedAtUtc = slip.UsedAtUtc,
            UsedByUserId = slip.UsedByUserId,
            UsedByUserName = await ResolveUserNameAsync(slip.UsedByUserId, ct),

            CancelledAtUtc = slip.CancelledAtUtc,
            CancelledByUserId = slip.CancelledByUserId,
            CancelReason = slip.CancelReason,

            Note = slip.Note,
            CreatedAtUtc = slip.CreatedAtUtc,

            Denominations = slip.Denominations
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.DenominationValue)
                .Select(x => new POSShiftHandoverSlipDenominationDto
                {
                    DenominationValue = x.DenominationValue,
                    Quantity = x.Quantity,
                    Amount = x.Amount
                })
                .ToList()
        };
    }
}