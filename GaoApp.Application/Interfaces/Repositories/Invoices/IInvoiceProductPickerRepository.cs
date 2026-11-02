using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceProductPickerRepository
{
    Task<Result<InvoiceProductPickerPageDto>> SearchProductsAsync(int invoiceHeadId, string keyword, int take = 20, CancellationToken ct = default);
    Task<Result<InvoiceProductPickerItemDto>> GetProductStockAsync(int invoiceHeadId, int productVariantId, CancellationToken ct = default);
}
