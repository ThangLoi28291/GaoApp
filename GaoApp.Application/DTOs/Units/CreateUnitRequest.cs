namespace GaoApp.Application.DTOs.Units;

public sealed class CreateUnitRequest
{
    public string? Code { get; set; }
    public string Name { get; set; } = default!;
    public bool Status { get; set; } = true;
    public bool IsBase { get; set; }
    public int SortOrder { get; set; } = 0;
}
