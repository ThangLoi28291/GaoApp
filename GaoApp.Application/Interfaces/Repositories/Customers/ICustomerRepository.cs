using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetActiveByIdAsync(int customerId, CancellationToken ct = default);

    Task<Customer?> GetByPhoneAsync(string phone, CancellationToken ct = default);

    Task<List<Customer>> SearchActiveAsync(string keyword, int take = 20, CancellationToken ct = default);

    Task AddAsync(Customer customer, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<Customer?> GetActiveByTaxCodeAsync(
       int storeId,
       string taxCode,
       CancellationToken ct = default);

}