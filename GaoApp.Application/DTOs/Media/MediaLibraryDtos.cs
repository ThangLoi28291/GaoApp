namespace GaoApp.Application.DTOs.Media;

public sealed class MediaLibraryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpireAtUtc { get; set; }
    public bool IsTemp { get; set; }
    public bool IsDeleted { get; set; }
    public bool Used { get; set; }
    public string Status { get; set; } = "";
    public List<MediaProductLink> Products { get; set; } = [];
}

public sealed record MediaProductLink(int Id, string Name);
public sealed class MediaLibrarySummary
{
    public int Total { get; set; }
    public int Used { get; set; }
    public int Temporary { get; set; }
    public int Waiting { get; set; }
    public int Ready { get; set; }
    public long Bytes { get; set; }
    public long ReadyBytes { get; set; }
}

public sealed record MediaLibraryPage(List<MediaLibraryItem> Items, MediaLibrarySummary Summary,
    string Search, string Status, int Page, int Pages, int FilteredCount);
public sealed record MediaCleanupResult(int Scanned, int Scheduled, int Deleted, int Failed, int Kept, int LastId);
public enum MediaCleanupOutcome { Kept, Scheduled, Deleted, Failed, Missing }
