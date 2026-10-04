namespace GaoApp.Domain.Enums;

public enum OrderLegalEntityReversalType : byte
{
    Void = 1,
    ReturnRestock = 2,
    ReturnNoRestock = 3,
    ReturnPendingRestock = 4
}
