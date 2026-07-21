namespace GaoApp.Application.DTOs.Orders;

public class InventoryIssueBatchRefreshResultDto
{
    public int TotalRequested { get; set; }
    public int RefreshedCount { get; set; }
    public int FailedCount { get; set; }

    public List<InventoryIssueBatchRefreshItemDto> Items { get; set; } = new();
}

public class InventoryIssueBatchRefreshItemDto
{
    public int IssueId { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}