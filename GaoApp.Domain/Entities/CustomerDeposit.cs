using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
namespace GaoApp.Domain.Entities;

public sealed class CustomerDeposit : BaseStoreEntity, IAuditTrackedEntity
{
    public int CustomerId { get; set; }
    [MaxLength(500)] public string Purpose { get; set; } = "";
    public DateTime? ExpectedDeliveryDate { get; set; }
    public decimal Balance { get; set; }
}

public sealed class CustomerDepositEntry : BaseStoreEntity, IAuditTrackedEntity
{
    public int CustomerDepositId { get; set; }
    public int? OrderId { get; set; }
    public int? SalesReturnId { get; set; }
    public int POSShiftId { get; set; }
    [MaxLength(20)] public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod? Method { get; set; }
    public int? StoreBankAccountId { get; set; }
    [MaxLength(100)] public string? Reference { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public Guid? ClientRequestId { get; set; }
    public string? RequestJson { get; set; }
}
