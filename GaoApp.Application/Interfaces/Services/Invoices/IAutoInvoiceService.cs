using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IAutoInvoiceService
{
    Task<AutoInvoiceDashboardDto> GetDashboardAsync(
        AutoInvoiceDashboardQueryDto query,
        CancellationToken ct = default);

    Task<Result<AutoInvoiceSettingsDto>> UpdateSettingsAsync(
        UpdateAutoInvoiceSettingsRequest request,
        CancellationToken ct = default);

    Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct = default);

    Task<Result<bool>> ToggleEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs one worker cycle. A forced cycle is used by the Admin button and
    /// deliberately bypasses only the persisted send-interval gate; it never
    /// bypasses the enabled/paused switch or the invoice eligibility rules.
    /// </summary>
    Task<Result> RunOnceAsync(bool force = false, CancellationToken ct = default);

    Task<Result<ViettelInvoiceIssueResultDto>> IssueManualAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
    Task<Result> RecheckIncidentAsync(
    int invoiceHeadId,
    CancellationToken ct = default);
    Task<Result<ViettelInvoiceLookupResultDto>> SyncUnknownAsync(int invoiceHeadId, CancellationToken ct = default);

    Task MarkWorkerStoppedAsync(CancellationToken ct = default);
}
