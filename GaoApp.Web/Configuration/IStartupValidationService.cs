namespace GaoApp.Web.Configuration;

/// <summary>
/// Service kiểm tra startup theo kiểu fail-fast.
/// </summary>
public interface IStartupValidationService
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}