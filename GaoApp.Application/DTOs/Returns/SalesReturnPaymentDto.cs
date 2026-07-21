namespace GaoApp.Application.DTOs.Returns;

public sealed class SalesReturnPaymentDto
{
    public int Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Provider { get; set; }
    public DateTime PaidAtUtc { get; set; }
}