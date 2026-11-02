namespace GaoApp.Application.DTOs.Inventory;

public sealed class ReceiptRevisionReviewDto
{
    public DateTime ReturnedAtUtc { get; set; }
    public DateTime? ResubmittedAtUtc { get; set; }
    public int AddedLineCount { get; set; }
    public int ChangedLineCount { get; set; }
    public int RemovedLineCount { get; set; }
}
