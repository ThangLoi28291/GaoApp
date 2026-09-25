namespace GaoApp.Domain.Enums;

public enum StockDocumentProvisionalItemStatus
{
    Unresolved = 0,
    Resolved = 1,
    Removed = 2
}

public enum ProvisionalItemResolutionMethod
{
    LinkExisting = 1,
    QuickCreate = 2
}
