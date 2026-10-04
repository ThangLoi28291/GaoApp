using GaoApp.Application.DTOs.Rewards;
namespace GaoApp.Application.DTOs.Customers;
public sealed class CustomerProfileSummaryDto
{
    public CustomerQuickViewDto Customer { get; set; } = new();
    public CustomerRewardSummaryDto? Rewards { get; set; }
    public string? RewardNotice { get; set; }
    public decimal? NetSales { get; set; }
    public int? CompletedOrders { get; set; }
    public DateTime? LastPurchaseAtUtc { get; set; }
    public bool CanViewOrders { get; set; }
}
public sealed class CustomerProfileQuery
{
    public string Tab { get; set; } = "overview";
    public string? Search { get; set; }
    public int? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
public sealed class CustomerProfilePageDto
{
    public List<CustomerProfileRowDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public decimal? MoneyPerPoint { get; set; }
}
public sealed class CustomerProfileRowDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime AtUtc { get; set; }
    public decimal Amount { get; set; }
    public decimal? BalanceAmount { get; set; }
    public decimal? BalancePoints { get; set; }
    public decimal? DeltaPoints { get; set; }
    public int Status { get; set; }
    public string? Employee { get; set; }
    public string? Terminal { get; set; }
    public int? OrderId { get; set; }
    public int? VoucherId { get; set; }
    public string? Reference { get; set; }
    public int? PaymentStatus { get; set; }
    public bool Credit { get; set; }
    public List<int> PaymentMethods { get; set; } = new();
}
