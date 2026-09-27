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
/// Pure scheduling policy.
///
/// Single và Group là hai lane độc lập:
/// - một group chưa đạt target không được block SingleReady phía sau;
/// - chỉ các operation READY mới cạnh tranh thứ tự;
/// - trong các operation READY, operation có sale time cũ nhất chạy trước.
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

        var readyOperations =
            new List<ReadyOperation>();

        // =========================
        // SINGLE LANE
        // =========================
        //
        // Defensive behavior:
        // một candidate không phải consumer vẫn luôn phát hành riêng.
        //
        // IsConsumer denotes grouping eligibility: the service requires an Auto
        // order fully paid in cash. Bank transfer, mixed and other non-cash
        // payments always use the single lane, irrespective of the threshold.
        var firstSingle = ordered
            .FirstOrDefault(x =>
                !x.IsConsumer ||
                x.GrandTotal >= separateAmountThreshold);

        if (firstSingle != null)
        {
            readyOperations.Add(
                new ReadyOperation(
                    new AutoInvoiceSelection(
                        AutoInvoiceOperationKind.Single,
                        [firstSingle.InvoiceHeadId]),
                    firstSingle.SaleAtUtc,
                    firstSingle.InvoiceHeadId));
        }

        // =========================
        // GROUP LANE
        // =========================
        var groupCandidates = ordered
            .Where(x =>
                x.IsConsumer &&
                x.GrandTotal < separateAmountThreshold)
            .GroupBy(x => x.GroupKey);

        foreach (var grouping in groupCandidates)
        {
            var group = grouping
                .OrderBy(x => x.SaleAtUtc)
                .ThenBy(x => x.InvoiceHeadId)
                .ToList();

            if (group.Count == 0)
                continue;

            var readyGroup =
                BuildReadyGroup(
                    group,
                    groupTargetAmount,
                    closingTimeLocal,
                    issueOldDayRemainder,
                    nowLocal);

            if (readyGroup == null)
                continue;

            readyOperations.Add(
                new ReadyOperation(
                    readyGroup,
                    group[0].SaleAtUtc,
                    group[0].InvoiceHeadId));
        }

        if (readyOperations.Count == 0)
            return null;

        // Single và Group chỉ cạnh tranh sau khi đã READY.
        //
        // Group đang chờ target không xuất hiện trong readyOperations,
        // do đó không thể block một SingleReady nằm phía sau.
        return readyOperations
            .OrderBy(x => x.OldestSaleAtUtc)
            .ThenBy(x => x.OldestInvoiceHeadId)
            .Select(x => x.Selection)
            .First();
    }

    private static AutoInvoiceSelection? BuildReadyGroup(
        IReadOnlyList<AutoInvoiceCandidate> group,
        decimal groupTargetAmount,
        TimeSpan closingTimeLocal,
        bool issueOldDayRemainder,
        DateTime nowLocal)
    {
        if (group.Count == 0)
            return null;

        var selected =
            new List<AutoInvoiceCandidate>();

        decimal selectedTotal = 0m;

        foreach (var item in group)
        {
            selected.Add(item);
            selectedTotal += item.GrandTotal;

            if (selectedTotal >= groupTargetAmount)
                break;
        }

        if (selectedTotal >= groupTargetAmount)
        {
            return new AutoInvoiceSelection(
                AutoInvoiceOperationKind.Group,
                selected
                    .Select(x => x.InvoiceHeadId)
                    .ToList());
        }

        var saleDate =
            group[0].SaleDateLocal.Date;

        var readyByClosing =
            saleDate == nowLocal.Date &&
            nowLocal.TimeOfDay >= closingTimeLocal;

        var readyByOldDay =
            saleDate < nowLocal.Date &&
            issueOldDayRemainder;

        if (!readyByClosing &&
            !readyByOldDay)
        {
            return null;
        }

        // Cutoff hoặc old-day remainder:
        // phát hành toàn bộ phần còn lại của group,
        // dù chưa đạt GroupTargetAmount.
        return new AutoInvoiceSelection(
            AutoInvoiceOperationKind.Group,
            group
                .Select(x => x.InvoiceHeadId)
                .ToList());
    }

    private sealed record ReadyOperation(
        AutoInvoiceSelection Selection,
        DateTime OldestSaleAtUtc,
        int OldestInvoiceHeadId);
}