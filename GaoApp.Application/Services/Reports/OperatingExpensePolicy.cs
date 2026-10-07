using GaoApp.Application.DTOs.Reports;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Reports;

public static class OperatingExpensePolicy
{
    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string> {
        ["rent"] = "Mặt bằng", ["salary"] = "Lương & nhân sự", ["utilities"] = "Điện, nước & dịch vụ",
        ["transport"] = "Vận chuyển", ["marketing"] = "Marketing", ["other"] = "Vật tư & chi phí khác" };

    // Cumulative cents keep every partial range additive, including the final allocation remainder.
    public static decimal Allocate(OperatingExpense expense, DateTime from, DateTime to)
    {
        var start = expense.RecognitionFrom.Date; var end = expense.RecognitionTo.Date;
        if (end < start) throw new ArgumentException("Kỳ ghi nhận chi phí không hợp lệ.");
        var a = from.Date > start ? from.Date : start; var b = to.Date < end ? to.Date : end;
        if (a > b) return 0;
        var days = (end - start).Days + 1;
        decimal Cumulative(int n) => Math.Round(expense.Amount * n / days, 2, MidpointRounding.AwayFromZero);
        return Cumulative((b - start).Days + 1) - Cumulative((a - start).Days);
    }

    public static void Validate(ExpenseWriteDto x)
    {
        if (string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 200 || string.IsNullOrEmpty(x.Category) || !Categories.ContainsKey(x.Category) ||
            x.Amount <= 0 || x.Amount > 1_000_000_000_000m || decimal.Round(x.Amount, 2) != x.Amount ||
            x.RecognitionFrom.Year < 2000 || x.RecognitionTo.Year > 2100 || x.RecognitionFrom.Date > x.RecognitionTo.Date ||
            (x.RecognitionTo.Date - x.RecognitionFrom.Date).Days >= 366 ||
            x.PaymentMethod is not ("cash" or "bank" or "other") ||
            x.ReceiptReference?.Length > 200 || x.Note?.Length > 1000)
            throw new ArgumentException("Kiểm tra tên khoản chi, số tiền, nhóm chi và kỳ ghi nhận (tối đa 366 ngày).");
    }
    public static ExpenseRowDto Row(OperatingExpense x, DateTime? from = null, DateTime? to = null) => new(
        x.Id, x.ClientRequestId, x.Name, x.Category, x.Amount, x.RecognitionFrom, x.RecognitionTo, x.Status,
        x.IsPaid, x.PaymentMethod, x.ReceiptReference, x.Note, Convert.ToBase64String(x.RowVersion ?? []),
        Allocate(x, from ?? x.RecognitionFrom, to ?? x.RecognitionTo), x.CreatedAtUtc, x.VoidReason);
}
