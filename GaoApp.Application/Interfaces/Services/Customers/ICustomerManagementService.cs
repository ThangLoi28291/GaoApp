using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Customers;

namespace GaoApp.Application.Interfaces.Services.Customers;

public interface ICustomerManagementService
{
    Task<CustomerManagementPageDto> GetPageAsync(CustomerManagementQueryRequest request, CancellationToken ct = default);
    Task<CustomerQuickViewDto?> GetQuickViewAsync(int id, CancellationToken ct = default);
    Task<Result<CustomerEditDto>> GetForEditAsync(int id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(CustomerEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(CustomerEditDto dto, CancellationToken ct = default);
    Task<Result<bool>> ToggleActiveAsync(int id, CancellationToken ct = default);
}
