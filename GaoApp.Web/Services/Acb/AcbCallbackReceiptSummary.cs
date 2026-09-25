namespace GaoApp.Web.Services.Acb;

public sealed record AcbCallbackReceiptSummary(int Id, int Page, int Attempts, DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc, DateTime NextAttemptAtUtc, bool NeedsReview, string? LastErrorCode, string RequestCode, int TotalPages);
