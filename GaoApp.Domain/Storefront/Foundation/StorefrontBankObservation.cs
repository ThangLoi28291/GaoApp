using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

// Unparseable, unknown and over-size provider evidence remains durable before any allocation.
public sealed class StorefrontBankObservation : BaseStoreEntity
{
    public int PaymentIntentId { get; set; }
    public string Kind { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
    public DateTime ObservedAtUtc { get; set; }
}
