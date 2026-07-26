using GaoApp.Application.DTOs.Auth;

using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Interfaces.Services.Auth;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken ct = default);
}
