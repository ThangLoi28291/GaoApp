using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class ProductLabelTemplate : BaseStoreEntity, IAuditTrackedEntity
{
    public string Name { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
}

public sealed class ProductLabelPrinter : BaseStoreEntity, IAuditTrackedEntity
{
    public string Name { get; set; } = "";
    public string WindowsPrinterName { get; set; } = "";
    public int Dpi { get; set; } = 203;
    public decimal PrintableWidthMm { get; set; } = 108;
    public decimal OffsetXmm { get; set; }
    public decimal OffsetYmm { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastSeenAtUtc { get; set; }
}

// Printing progress is independent of stock posting. JSON lines preserve the receipt snapshot.
public sealed class ProductLabelTask : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = default!;
    public string DocumentNo { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public int? TemplateId { get; set; }
    public bool Completed { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CompletedByUserId { get; set; }
}

public enum ProductLabelJobStatus { Queued, Sending, AwaitingConfirmation, NeedsAttention, Confirmed, Cancelled }

public sealed class ProductLabelJob : BaseStoreEntity, IAuditTrackedEntity
{
    public int? TaskId { get; set; }
    public ProductLabelTask? Task { get; set; }
    public int PrinterId { get; set; }
    public ProductLabelPrinter Printer { get; set; } = default!;
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public ProductLabelJobStatus Status { get; set; }
    public string PayloadJson { get; set; } = "";
    public string ResultJson { get; set; } = "[]";
    public int Quantity { get; set; }
    public bool IsReprint { get; set; }
    public string Reason { get; set; } = "";
    public string RequestedByName { get; set; } = "";
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public string? ConfirmedByName { get; set; }
    public int? SpoolJobId { get; set; }
    public string? Error { get; set; }
}
