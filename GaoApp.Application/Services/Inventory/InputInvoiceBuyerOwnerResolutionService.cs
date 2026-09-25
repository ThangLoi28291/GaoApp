using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceBuyerOwnerResolutionService(
    ILegalEntityRepository legalEntities)
    : IInputInvoiceBuyerOwnerResolutionService
{
    public async Task<InputInvoiceBuyerOwnerResolution> ResolveWithinTransactionAsync(
        int storeId,
        InputInvoiceHead invoice,
        CancellationToken ct = default)
    {
        var normalized = TaxCodeIdentityNormalizer.Normalize(invoice.BuyerTaxCode);
        if (normalized is null)
            return Apply(invoice, InputInvoiceBuyerOwnerResolutionStatus.MissingBuyerTaxCode, null, 0, null);

        var candidates = await legalEntities.LockActiveByNormalizedTaxCodeAsync(
            storeId, normalized, ct);
        if (candidates.Count == 0)
            return Apply(invoice, InputInvoiceBuyerOwnerResolutionStatus.NotFound, normalized, 0, null);
        if (candidates.Count != 1)
            return Apply(invoice, InputInvoiceBuyerOwnerResolutionStatus.Ambiguous, normalized, candidates.Count, null);

        return Apply(invoice, InputInvoiceBuyerOwnerResolutionStatus.Resolved,
            normalized, 1, candidates[0]);
    }

    private static InputInvoiceBuyerOwnerResolution Apply(
        InputInvoiceHead invoice,
        InputInvoiceBuyerOwnerResolutionStatus status,
        string? normalized,
        int candidateCount,
        LegalEntity? owner)
    {
        invoice.BuyerOwnerResolutionStatus = status;
        invoice.ResolvedBuyerLegalEntityId = owner?.Id;
        invoice.BuyerOwnerResolutionUpdatedAtUtc = DateTime.UtcNow;
        return new(status, normalized, owner?.Id, owner?.Code, owner?.Name, candidateCount);
    }
}
