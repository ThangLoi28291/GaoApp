using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;

namespace GaoApp.Domain.Storefront;

// A contradictory receiving account is evidence, not verified cash in for the requested account.
public sealed class StorefrontUnattributedBankReceipt : BaseStoreEntity
{
    public int PaymentIntentId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ExternalTransactionId { get; set; } = "";
    public decimal Amount { get; set; }
    public string RequestedAccountIdentity { get; set; } = "";
    public string ReportedAccountIdentity { get; set; } = "";
    public string RawBankTime { get; set; } = "";
    public BankTimePrecision TimePrecision { get; set; }
    public DateTime? BankBusinessDate { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public string ProtectedEvidence { get; set; } = "";
}
