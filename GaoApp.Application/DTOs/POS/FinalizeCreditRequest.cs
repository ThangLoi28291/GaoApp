namespace GaoApp.Application.DTOs.POS;

public sealed class FinalizeCreditRequest
{
    public Guid ClientRequestId { get; set; }
    public int ExpectedCustomerId { get; set; }
    public decimal ExpectedBalance { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Note { get; set; }
}
