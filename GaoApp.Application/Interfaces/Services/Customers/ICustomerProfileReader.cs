using GaoApp.Application.DTOs.Customers;
namespace GaoApp.Application.Interfaces.Services.Customers;
public interface ICustomerProfileReader
{
    Task<CustomerProfileSummaryDto?> SummaryAsync(int customerId, bool canViewOrders, CancellationToken ct);
    Task<CustomerProfilePageDto?> PageAsync(int customerId, CustomerProfileQuery query, bool canViewOrders, CancellationToken ct);
}
