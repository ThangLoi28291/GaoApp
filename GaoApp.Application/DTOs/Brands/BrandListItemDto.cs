namespace GaoApp.Application.DTOs.Brands;

public sealed class BrandListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
}
