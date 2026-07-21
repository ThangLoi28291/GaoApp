
using GaoApp.Application.Interfaces.Repositories.Products;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed class VariantUsageChecker : IVariantUsageChecker
{
    public Task<bool> HasTransactionAsync(int storeId, int variantId, CancellationToken ct)
        => Task.FromResult(false); // GĐ2 chưa có Order/Kho
}
