using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class AcbPaymentTransaction : BaseStoreEntity
{
    public int SessionId { get; set; }
    [MaxLength(100)] public string TransactionNumber { get; set; } = "";
    [MaxLength(50)] public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string Content { get; set; } = "";
    [MaxLength(100)] public string PostedAt { get; set; } = "";
}
