namespace GaoApp.Application.Interfaces.Repositories.Products;

public interface IVariantUsageChecker
{
    Task<bool> HasTransactionAsync(int storeId, int variantId, CancellationToken ct);
}
