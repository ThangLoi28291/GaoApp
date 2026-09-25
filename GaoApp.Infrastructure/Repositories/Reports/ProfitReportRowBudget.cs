using GaoApp.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Reports;

internal sealed class ProfitReportRowBudget(int maximum)
{
    public int Remaining { get; private set; } = maximum;
    public void Consume(int count)
    {
        if (count > Remaining)
            throw new ValidationAppException("Phạm vi báo cáo có quá nhiều dữ liệu. Vui lòng chọn khoảng ngày ngắn hơn.");
        Remaining -= count;
    }
}

internal static class ProfitReportBoundedRead
{
    public static async Task<List<T>> ReadWithinBudgetAsync<T>(this IQueryable<T> query, ProfitReportRowBudget budget, CancellationToken ct)
    {
        // Limit in SQL before materialization; never return truncated financial totals.
        var rows = await query.Take(budget.Remaining + 1).ToListAsync(ct);
        budget.Consume(rows.Count);
        return rows;
    }
}
