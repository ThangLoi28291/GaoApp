namespace GaoApp.Application.DTOs.POS;

public sealed class POSCustomerSearchItemDto
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public string DisplayText =>
        string.IsNullOrWhiteSpace(Phone)
            ? Name
            : $"{Name} - {Phone}";
}