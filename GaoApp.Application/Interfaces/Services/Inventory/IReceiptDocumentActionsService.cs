using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IReceiptDocumentActionsService
{
    Task<ReceiptDocumentActionsDto> GetAsync(int id, CancellationToken ct);
    Task RenameAsync(int id, ReceiptTitleRequest request, bool manager, CancellationToken ct);
    Task RequestTitleAsync(int id, ReceiptTitleRequest request, CancellationToken ct);
    Task ReviewTitleAsync(int id, ReviewReceiptTitleRequest request, CancellationToken ct);
    Task DeleteDraftAsync(int id, ReceiptVersionRequest request, CancellationToken ct);
}
