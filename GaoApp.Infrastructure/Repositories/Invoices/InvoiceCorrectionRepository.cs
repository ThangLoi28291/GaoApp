using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceCorrectionRepository : IInvoiceCorrectionRepository
{
    private readonly AppDbContext _db;

    public InvoiceCorrectionRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceHead?> GetOriginalInvoiceWithDetailsAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return await _db.InvoiceHeads
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x =>
                x.Id == invoiceHeadId &&
                !x.IsDeleted,
                ct);
    }

    public async Task<InvoiceCorrectionCase?> GetOpenCaseByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .Include(x => x.NewInvoiceHead)
            .Where(x =>
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == originalInvoiceHeadId &&
                (
                    x.Status == InvoiceCorrectionStatus.Draft ||
                    x.Status == InvoiceCorrectionStatus.ReadyToIssue ||
                    x.Status == InvoiceCorrectionStatus.Issuing
                ))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
        CancellationToken ct = default)
    {
        await _db.InvoiceHeads.AddAsync(invoiceHead, ct);
    }

    public async Task AddCorrectionCaseAsync(
        InvoiceCorrectionCase correctionCase,
        CancellationToken ct = default)
    {
        await _db.InvoiceCorrectionCases.AddAsync(correctionCase, ct);
    }
    public async Task<InvoiceCorrectionCase?> GetByNewInvoiceHeadIdAsync(
    int newInvoiceHeadId,
    CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .Include(x => x.OriginalInvoiceHead)
            .Include(x => x.NewInvoiceHead)
            .FirstOrDefaultAsync(x =>
                !x.IsDeleted &&
                x.NewInvoiceHeadId == newInvoiceHeadId,
                ct);
    }
    public async Task<InvoiceHead?> GetInvoiceHeadForHistoryAsync(
    int invoiceHeadId,
    CancellationToken ct = default)
    {
        return await _db.InvoiceHeads
            .AsNoTracking()
            .Include(x => x.OriginalInvoiceHead)
            .FirstOrDefaultAsync(x =>
                x.Id == invoiceHeadId &&
                !x.IsDeleted,
                ct);
    }

    public async Task<List<InvoiceCorrectionCase>> GetCasesByOriginalInvoiceHeadIdAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .AsNoTracking()
            .Include(x => x.OriginalInvoiceHead)
            .Include(x => x.NewInvoiceHead)
            .Where(x =>
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == originalInvoiceHeadId)
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
    }
    public async Task<InvoiceCorrectionCase?> GetUnfinishedCaseByOriginalAsync(
    int originalInvoiceHeadId,
    CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .Include(x => x.NewInvoiceHead)
            .Where(x =>
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == originalInvoiceHeadId &&
                (
                    x.Status == InvoiceCorrectionStatus.Draft ||
                    x.Status == InvoiceCorrectionStatus.ReadyToIssue ||
                    x.Status == InvoiceCorrectionStatus.Issuing ||
                    x.Status == InvoiceCorrectionStatus.Failed
                ))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<InvoiceCorrectionCase?> GetActiveReplacementCaseByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .Include(x => x.NewInvoiceHead)
            .Where(x =>
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == originalInvoiceHeadId &&
                x.Type == InvoiceCorrectionType.Replacement &&
                x.Status != InvoiceCorrectionStatus.Cancelled &&
                (
                    x.NewInvoiceHead == null ||
                    !x.NewInvoiceHead.IsDeleted
                ))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<InvoiceCorrectionCase>> GetActiveCasesByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default)
    {
        return await _db.InvoiceCorrectionCases
            .Include(x => x.NewInvoiceHead)
            .Where(x =>
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == originalInvoiceHeadId &&
                x.Status != InvoiceCorrectionStatus.Cancelled &&
                (
                    x.NewInvoiceHead == null ||
                    !x.NewInvoiceHead.IsDeleted
                ))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}