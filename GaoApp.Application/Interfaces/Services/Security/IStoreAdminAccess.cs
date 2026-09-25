namespace GaoApp.Application.Interfaces.Services.Security;

public interface IStoreAdminAccess
{
    Task<bool> IsAdminAsync(CancellationToken ct = default);
    Task RequireAsync(CancellationToken ct = default);
}
