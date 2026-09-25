namespace GaoApp.Application.DTOs.Units;

public sealed class UnitListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
    public bool IsBase { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}