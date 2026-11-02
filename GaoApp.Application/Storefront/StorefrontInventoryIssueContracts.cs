using GaoApp.Application.DTOs.Orders;

namespace GaoApp.Application.Storefront;

public interface IStorefrontInventoryIssueAdapter
{
    Task<bool> TryRefreshAsync(int issueId, CancellationToken ct = default);
    Task<InventoryIssueApprovalCheckResultDto?> TryValidateAsync(int issueId, CancellationToken ct = default);
}
