using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.POSPaymentQrs;

public interface IPOSPaymentQrRequestRepository
{
    Task AddAsync(PosPaymentQrRequest entity, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<PosPaymentQrRequest?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PosPaymentQrRequest?> GetLatestByContentAsync(
    string content,
    CancellationToken ct = default);
}