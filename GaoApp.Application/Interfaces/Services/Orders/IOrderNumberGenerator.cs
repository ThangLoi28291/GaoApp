namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}