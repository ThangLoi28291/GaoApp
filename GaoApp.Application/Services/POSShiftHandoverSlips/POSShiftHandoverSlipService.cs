using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Services.Security;
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
    private readonly IStoreAdminAccess _admin;
    private readonly IPOSTerminalRepository _terminals;

    public POSShiftHandoverSlipService(
        IPOSShiftHandoverSlipRepository repo,
        IWarehouseRepository warehouseRepo,
        IUserRepository userRepo,
        ICurrentStore currentStore,
        ICurrentUser currentUser,
        IStoreAdminAccess admin,
        IPOSTerminalRepository terminals)
    {
        _repo = repo;
        _warehouseRepo = warehouseRepo;
        _userRepo = userRepo;
        _currentStore = currentStore;
        _currentUser = currentUser;
        _admin = admin;
        _terminals = terminals;
    }

    public async Task<POSShiftHandoverAssignmentsDto> GetAssignmentsAsync(CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
        var terminals = await _terminals.GetActiveByStoreAsync(_currentStore.StoreId, ct);
        return new(terminals.Select(x => new POSShiftAssignmentOption(x.Id, $"{x.Code} - {x.Name}")).ToList());
    }

    public async Task<POSShiftHandoverSlipDto> CreateAsync(
        CreatePOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
        var storeId = _currentStore.StoreId;
        var userId = RequireUserId();

        var denomItems = await ValidateAndBuildDenominationsAsync(request, ct);

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
            AssignedToUserId = null,
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

    public async Task<POSShiftHandoverSlipDto> UpdateAsync(
        int id, UpdatePOSShiftHandoverSlipRequest request, CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
        var slip = await RequireSlipAsync(id, ct);
        if (slip.Status is not (POSShiftHandoverSlipStatus.Draft or POSShiftHandoverSlipStatus.Printed))
            throw new ConflictAppException("Chỉ sửa được phiếu chưa nhận và chưa hủy.");
        if (request.RowVersion == null || request.RowVersion.Length == 0 || !request.RowVersion.SequenceEqual(slip.RowVersion))
            throw new ConflictAppException("Phiếu đã thay đổi. Vui lòng tải lại phiếu trước khi sửa.");
        var denominations = await ValidateAndBuildDenominationsAsync(request, ct);

        // Keep the old denomination rows for audit and replace their active values atomically.
        foreach (var old in slip.Denominations.Where(x => !x.IsDeleted))
            old.IsDeleted = true;
        foreach (var item in denominations) slip.Denominations.Add(item);
        slip.WarehouseId = request.WarehouseId;
        slip.TerminalId = request.TerminalId;
        slip.AssignedToUserId = null;
        slip.Note = request.Note?.Trim();
        slip.Status = POSShiftHandoverSlipStatus.Draft;
        slip.PrintedAtUtc = null;
        // Keep the replacement barcode short enough to print clearly on a receipt roll.
        slip.BarcodeValue = $"H{Guid.NewGuid().ToString("N")[..16].ToUpperInvariant()}";
        slip.RecalcTotal();
        await _repo.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct) ?? throw new NotFoundAppException("Không tìm thấy phiếu nhận ca.");
    }

    private async Task<List<POSShiftHandoverSlipDenomination>> ValidateAndBuildDenominationsAsync(
        CreatePOSShiftHandoverSlipRequest request, CancellationToken ct)
    {
        var storeId = _currentStore.StoreId;
        if (request.TerminalId is not > 0)
            throw new ValidationAppException("Vui lòng chọn quầy nhận ca.");
        var terminal = await _terminals.GetByIdAsync(request.TerminalId.Value, ct);
        if (terminal == null || terminal.StoreId != storeId || !terminal.IsActive || terminal.IsDeleted
            || terminal.Status != POSTerminalStatus.Active)
            throw new ValidationAppException("Quầy nhận ca không hợp lệ hoặc đã ngừng hoạt động.");
        if (request.Note?.Length > 500)
            throw new ValidationAppException("Ghi chú tối đa 500 ký tự.");

        if (request.WarehouseId <= 0)
            throw new ValidationAppException("Vui lòng chọn kho bán hàng.");

        var warehouse = await _warehouseRepo.GetByIdAsync(request.WarehouseId, ct);
        if (warehouse == null || warehouse.StoreId != storeId || warehouse.IsDeleted)
            throw new ValidationAppException("Kho bán hàng không tồn tại.");

        if (!warehouse.IsActive)
            throw new ValidationAppException("Kho bán hàng đã ngưng hoạt động.");

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
            throw new ValidationAppException("Vui lòng nhập ít nhất một mệnh giá tiền.");

        return denomItems;
    }

    public async Task<POSShiftHandoverSlipDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
        var slip = await _repo.GetByIdAsync(_currentStore.StoreId, id, ct);
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlipDto?> GetByBarcodeAsync(string barcodeValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
            return null;

        var slip = await _repo.GetByBarcodeAsync(_currentStore.StoreId, barcodeValue.Trim(), ct);
        if (slip != null && !await _admin.IsAdminAsync(ct))
        {
            if (slip.TerminalId.HasValue && slip.TerminalId != _currentUser.TerminalId)
                throw new ForbiddenAppException("Phiếu nhận ca không thuộc quầy hiện tại.");
        }
        return slip == null ? null : await MapAsync(slip, ct);
    }

    public async Task<PagedResult<POSShiftHandoverSlipDto>> QueryAsync(
        QueryPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
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

    public async Task<POSShiftHandoverSlipDto> MarkPrintedAsync(int id, CancellationToken ct = default, string? barcodeValue = null)
    {
        await _admin.RequireAsync(ct);
        var slip = await RequireSlipAsync(id, ct);

        if (barcodeValue != null && barcodeValue != slip.BarcodeValue)
            throw new ConflictAppException("Phiếu đã được sửa. Vui lòng mở lại bản in mới.");
        if (slip.Status is POSShiftHandoverSlipStatus.Used or POSShiftHandoverSlipStatus.Cancelled)
            return await MapAsync(slip, ct);
        slip.MarkPrinted();

        await _repo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlipDto> CancelAsync(
        int id,
        CancelPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default)
    {
        await _admin.RequireAsync(ct);
        var slip = await RequireSlipAsync(id, ct);

        if (slip.Status == POSShiftHandoverSlipStatus.Used)
            throw new ConflictAppException("Phiếu đã sử dụng, không thể hủy.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
            throw new ValidationAppException("Nhập lý do hủy từ 1 đến 300 ký tự.");
        slip.Cancel(RequireUserId(), request.Reason);

        await _repo.SaveChangesAsync(ct);

        return await MapAsync(slip, ct);
    }

    private async Task<POSShiftHandoverSlip> RequireSlipAsync(int id, CancellationToken ct)
    {
        var slip = await _repo.GetByIdAsync(_currentStore.StoreId, id, ct);

        if (slip == null)
            throw new NotFoundAppException("Không tìm thấy phiếu nhận ca.");

        return slip;
    }

    private int RequireUserId()
    {
        if (_currentUser.UserId.HasValue && _currentUser.UserId.Value > 0)
            return _currentUser.UserId.Value;

        throw new ValidationAppException("Bạn chưa đăng nhập hoặc phiên đăng nhập không hợp lệ.");
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

        throw new ValidationAppException("Không thể sinh mã phiếu nhận ca trong ngày.");
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
            RowVersion = slip.RowVersion,
            RequiresReprint = slip.Status == POSShiftHandoverSlipStatus.Draft && slip.BarcodeValue != slip.SlipCode,
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
