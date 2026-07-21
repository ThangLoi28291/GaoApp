using GaoApp.Application.DTOs.Auth;

namespace GaoApp.Application.Interfaces.Services.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}