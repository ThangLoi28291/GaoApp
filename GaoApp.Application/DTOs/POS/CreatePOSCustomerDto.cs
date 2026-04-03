namespace GaoApp.Application.DTOs.POS;

public sealed class CreatePOSCustomerDto
{
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
}