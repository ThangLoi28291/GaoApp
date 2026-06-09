using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Sinh mã phiếu điều chỉnh kho.
/// </summary>
public class InventoryAdjustmentDocumentNumberService
    : IInventoryAdjustmentDocumentNumberService
{
    private readonly IInventoryAdjustmentDocumentNumberRepository _repository;

    public InventoryAdjustmentDocumentNumberService(
        IInventoryAdjustmentDocumentNumberRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> GenerateAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow;

        var prefix = $"ADJ-{today:yyyyMMdd}-";

        var countToday = await _repository.CountTodayAsync(prefix, ct);

        return $"{prefix}{countToday + 1:0000}";
    }
}