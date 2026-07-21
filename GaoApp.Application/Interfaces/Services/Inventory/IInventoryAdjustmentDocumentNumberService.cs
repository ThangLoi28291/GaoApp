namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Sinh mã phiếu điều chỉnh kho.
/// Ví dụ: ADJ-20260516-0001.
/// </summary>
public interface IInventoryAdjustmentDocumentNumberService
{
    Task<string> GenerateAsync(CancellationToken ct = default);
}