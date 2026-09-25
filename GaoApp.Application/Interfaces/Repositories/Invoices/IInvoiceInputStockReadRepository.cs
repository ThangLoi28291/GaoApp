using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceInputStockReadRepository
{
    Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(int storeId, CancellationToken ct = default);
}
