using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IBarcodeGovernanceService
{
    Task ChangeBarcodeAsync(ChangeBarcodeRequest request, CancellationToken ct = default);
}