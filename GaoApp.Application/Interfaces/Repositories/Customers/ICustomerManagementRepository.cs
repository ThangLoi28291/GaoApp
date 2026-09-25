using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Customers;

public interface ICustomerManagementRepository
{
    Task<PagedResult<CustomerListItemDto>> GetPageAsync(int storeId, CustomerManagementQueryRequest request, CancellationToken ct = default);
    Task<CustomerManagementSummaryDto> GetSummaryAsync(int storeId, CustomerManagementQueryRequest request, CancellationToken ct = default);
    Task<CustomerQuickViewDto?> GetQuickViewAsync(int storeId, int id, CancellationToken ct = default);
    Task<Customer?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsPhoneAsync(int storeId, string phone, int? excludeId, CancellationToken ct = default);
    Task AddAsync(Customer customer, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
