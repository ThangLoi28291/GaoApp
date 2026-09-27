using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("InvoiceBuyerSelfServiceRequests")]
public sealed class InvoiceBuyerSelfServiceRequest
    : BaseStoreEntity, IAuditTrackedEntity
{
    public int OrderId { get; set; }

    public Order Order { get; set; } = default!;

    /// <summary>
    /// SHA-256 của opaque token in trên QR.
    /// Không lưu plaintext token.
    /// </summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Fixed expiry = Order.CompletedAtUtc + 2 giờ.
    /// Reprint không kéo dài thời gian này.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? LastSubmittedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}