namespace GaoApp.Domain.Enums;

/// <summary>
/// Mức độ cảnh báo của case tồn âm theo tuổi case.
/// </summary>
public enum InventoryIssueSeverity
{
    Normal = 0,
    Warning = 1,
    Overdue = 2,
    Critical = 3
}