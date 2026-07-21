namespace GaoApp.Domain.Enums;

/// <summary>
/// Module nghiệp vụ để gom nhóm log.
/// </summary>
public enum AuditModuleType
{
    Authentication = 1,
    UserManagement = 2,
    Catalog = 3,
    Pricing = 4,
    Orders = 5,
    Inventory = 6,
    Security = 7,
    POS = 8,
    System = 9
}