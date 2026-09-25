using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceBuyerOwnerResolutionService
{
    Task<InputInvoiceBuyerOwnerResolution> ResolveWithinTransactionAsync(
        int storeId,
        InputInvoiceHead invoice,
        CancellationToken ct = default);
}

public sealed record InputInvoiceBuyerOwnerResolution(
    InputInvoiceBuyerOwnerResolutionStatus Status,
    string? NormalizedBuyerTaxCode,
    int? LegalEntityId,
    string? LegalEntityCode,
    string? LegalEntityName,
    int CandidateCount);
