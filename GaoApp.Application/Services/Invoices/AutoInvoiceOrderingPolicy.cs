using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public sealed record AutoInvoiceCandidate(
    int InvoiceHeadId,
    DateTime SaleAtUtc,
    DateTime SaleDateLocal,
    decimal GrandTotal,
    bool IsConsumer,
    string GroupKey);

public sealed record AutoInvoiceSelection(
    AutoInvoiceOperationKind Kind,
    IReadOnlyList<int> InvoiceHeadIds);

/// <summary>
/// Pure scheduling policy. It deliberately knows nothing about EF or Viettel so
/// the chronological/month/grouping guarantees can be tested without a database.
/// </summary>
public static class AutoInvoiceOrderingPolicy
{
    public static AutoInvoiceSelection? Select(
        IReadOnlyList<AutoInvoiceCandidate> candidates,
        decimal separateAmountThreshold,
        decimal groupTargetAmount,
        TimeSpan closingTimeLocal,
        bool issueOldDayRemainder,
        DateTime nowLocal)
    {
        var ordered = candidates
            .OrderBy(x => x.SaleAtUtc)
            .ThenBy(x => x.InvoiceHeadId)
            .ToList();
        if (ordered.Count == 0)
            return null;

        var first = ordered[0];
        if (!first.IsConsumer || first.GrandTotal >= separateAmountThreshold)
            return new(AutoInvoiceOperationKind.Single, [first.InvoiceHeadId]);

        var group = ordered
            .Where(x => x.IsConsumer && x.GroupKey == first.GroupKey)
            .OrderBy(x => x.SaleAtUtc)
            .ThenBy(x => x.InvoiceHeadId)
            .ToList();
        var selected = new List<AutoInvoiceCandidate>();
        decimal total = 0;
        foreach (var item in group)
        {
            selected.Add(item);
            total += item.GrandTotal;
            if (total >= groupTargetAmount)
                break;
        }

        var saleDate = first.SaleDateLocal.Date;
        var readyByClosing = saleDate == nowLocal.Date && nowLocal.TimeOfDay >= closingTimeLocal;
        var readyByOldDay = saleDate < nowLocal.Date && issueOldDayRemainder;
        if (total < groupTargetAmount && !readyByClosing && !readyByOldDay)
            return null;

        var ids = total >= groupTargetAmount
            ? selected.Select(x => x.InvoiceHeadId).ToList()
            : group.Select(x => x.InvoiceHeadId).ToList();
        return new(AutoInvoiceOperationKind.Group, ids);
    }
}
