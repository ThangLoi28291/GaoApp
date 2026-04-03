namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại hành động audit.
/// </summary>
public enum AuditActionType
{
    Login = 1,
    Logout = 2,
    LoginFailed = 3,

    Create = 10,
    Update = 11,
    Delete = 12,
    Restore = 13,

    ChangeBarcode = 20,
    ChangePrice = 21,

    CreateOrder = 30,
    UpdateOrder = 31,
    CancelOrder = 32,
    FinalizeOrder = 33,
    Refund = 34,
    Return = 35,

    StockAdjustment = 40,
    StockTransfer = 41,
    StockCount = 42,

    ChangeUserRole = 50,

    OpenShift = 60,
    CloseShift = 61,

        ReturnCreate = 200,
    ReturnComplete = 201,
    ReturnCancel = 202,
    RefundPayment = 203,
    RestockFromReturn = 204
}