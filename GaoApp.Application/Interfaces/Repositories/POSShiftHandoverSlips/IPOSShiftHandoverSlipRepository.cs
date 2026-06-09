using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.POSShiftHandoverSlips;

public interface IPOSShiftHandoverSlipRepository
{
    Task AddAsync(POSShiftHandoverSlip slip, CancellationToken ct = default);

    Task<POSShiftHandoverSlip?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlip?> GetByBarcodeAsync(
        int storeId,
        string barcodeValue,
        CancellationToken ct = default);

    Task<bool> ExistsSlipCodeAsync(
        int storeId,
        string slipCode,
        CancellationToken ct = default);

    Task<(List<POSShiftHandoverSlip> Items, int Total)> QueryAsync(
        int storeId,
        POSShiftHandoverSlipStatus? status,
        int? terminalId,
        int? warehouseId,
        int? assignedToUserId,
        string? keyword,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    /// <summary>
    /// Tìm phiếu có thể dùng để mở ca.
    /// Chỉ trả về Draft hoặc Printed.
    /// </summary>
    Task<POSShiftHandoverSlip?> GetUsableByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    /// <summary>
    /// Tìm phiếu có thể dùng để mở ca theo barcode.
    /// Chỉ trả về Draft hoặc Printed.
    /// </summary>
    Task<POSShiftHandoverSlip?> GetUsableByBarcodeAsync(
        int storeId,
        string barcodeValue,
        CancellationToken ct = default);
}