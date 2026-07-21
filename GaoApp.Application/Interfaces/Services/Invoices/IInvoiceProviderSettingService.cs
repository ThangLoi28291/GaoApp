using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceProviderSettingService
{
    Task<Result<List<InvoiceProviderSettingListItemDto>>> GetAllAsync(CancellationToken ct = default);

    Task<Result<UpsertInvoiceProviderSettingRequest>> GetForEditAsync(int id, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(UpsertInvoiceProviderSettingRequest request, CancellationToken ct = default);

    Task<Result<int>> UpdateAsync(UpsertInvoiceProviderSettingRequest request, CancellationToken ct = default);

    Task<Result<bool>> ToggleActiveAsync(int id, CancellationToken ct = default);

    Task<Result<TestInvoiceProviderLoginResultDto>> TestLoginAsync(int id, CancellationToken ct = default);
}