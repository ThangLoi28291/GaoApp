namespace GaoApp.Application.Interfaces.Services.Inventory;

public sealed record ReturnRestockReadiness(bool CanRestock, string? BlockCode = null, string? BlockReason = null, string? ActionHint = null);

public interface IReturnableValuationReadiness
{
    Task<ReturnRestockReadiness> GetRestockReadinessAsync(int orderId, int orderLineId, CancellationToken ct = default);
}
