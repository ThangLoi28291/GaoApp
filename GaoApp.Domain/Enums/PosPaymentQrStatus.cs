namespace GaoApp.Domain.Enums;

public enum PosPaymentQrStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Expired = 3,
    Cancelled = 4,
    ManualConfirmed = 5
}