namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository hỗ trợ sinh số phiếu điều chỉnh kho.
/// </summary>
public interface IInventoryAdjustmentDocumentNumberRepository
{
    Task<int> CountTodayAsync(
        string prefix,
        CancellationToken ct = default);
}