using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

public sealed class StorefrontQrProcessing : BaseStoreEntity
{
    public int PaymentIntentId { get; set; }
    public int State { get; set; }
    public string TraceNumber { get; set; } = "";
    public string ProviderTraceNumber { get; set; } = "";
    public string VirtualAccount { get; set; } = "";
    public byte[]? QrPng { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextScanBankDate { get; set; }
    public DateTime? RequestedBankDate { get; set; }
    public int Attempts { get; set; }
    public string? ReviewCode { get; set; }
}
