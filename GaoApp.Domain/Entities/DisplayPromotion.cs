using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class DisplayPromotion : BaseStoreEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// image / video / text
    /// </summary>
    public string MediaType { get; set; } = "image";

    public string? MediaUrl { get; set; }

    public string? ButtonText { get; set; }

    public string? BackgroundColor { get; set; }

    public string? TextColor { get; set; }

    public int SortOrder { get; set; }

    public int DurationSeconds { get; set; } = 6;

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;

    public bool IsFlashSale { get; set; }

    public DateTime? CountdownToUtc { get; set; }

    public bool IsFullscreen { get; set; }
}