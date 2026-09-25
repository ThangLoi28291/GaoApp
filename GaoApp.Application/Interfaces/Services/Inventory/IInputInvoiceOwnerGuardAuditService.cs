using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceOwnerGuardAuditService
{
    Task RecordBlockedLinkAsync(
        int storeId,
        int stockDocumentId,
        InputInvoiceOwnerGuardException exception,
        CancellationToken ct = default);

    Task RecordBlockedConfirmAsync(
        int storeId,
        int stockDocumentId,
        InputInvoiceOwnerGuardException exception,
        CancellationToken ct = default);
}
