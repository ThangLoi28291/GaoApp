using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POS;

public sealed class UpsertPaymentRequest
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }

    public string? ReferenceCode { get; set; } // mã GD, last4...
    public string? Provider { get; set; }      // ACB/...
}