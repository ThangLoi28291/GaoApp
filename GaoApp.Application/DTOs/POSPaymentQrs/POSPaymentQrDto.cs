using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSPaymentQrs;

public class POSPaymentQrDto
{
    public bool AutomaticConfirmation { get; set; }
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int BankAccountId { get; set; }

    public string BankCode { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Content { get; set; } = string.Empty;

    public string RequestCode { get; set; } = string.Empty;

    public PosPaymentQrStatus Status { get; set; }

    public string QrDataUrl { get; set; } = string.Empty;

    public string QrRawText { get; set; } = string.Empty;

    public DateTime ExpireAtUtc { get; set; }
}
