namespace GaoApp.Application.DTOs.Display;

public class DisplayPromotionIndexDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string MediaType { get; set; } = "image";

    public string? MediaUrl { get; set; }

    public int SortOrder { get; set; }

    public int DurationSeconds { get; set; }

    public bool IsActive { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}