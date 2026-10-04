using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IReceiptInvoiceFollowUpService
{
    Task<ReceiptInvoiceFollowUpDto> GetAsync(int id, CancellationToken ct);
    Task EndWaitingAsync(int id, EndReceiptInvoiceWaitRequest request, CancellationToken ct);
    Task ReviewAsync(int id, ReviewReceiptInvoiceRequest request, CancellationToken ct);
}
