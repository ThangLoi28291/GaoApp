using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Returns;

public sealed class CreateSalesReturnPaymentRequest
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Provider { get; set; }
    public string? Note { get; set; }
}