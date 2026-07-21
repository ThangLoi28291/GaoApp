using GaoApp.Application.Common;

namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityReconciliationQueryDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? LegalEntityId { get; set; }
    public string? Keyword { get; set; }
    public bool OnlyDiscrepancies { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class LegalEntityReconciliationReportDto
{
    public LegalEntityReconciliationQueryDto Query { get; set; } = new();
    public bool IsMultiLegalEntityEnabled { get; set; }
    public DateTime? MultiLegalEntityActivatedAtUtc { get; set; }
    public List<LegalEntityReconciliationOptionDto> LegalEntities { get; set; } = [];
    public int OrderCount { get; set; }
    public int DiscrepantOrderCount { get; set; }
    public decimal UnifiedOrderTotal { get; set; }
    public decimal AllocationGrossTotal { get; set; }
    public decimal ReversalTotal { get; set; }
    public decimal AllocationNetTotal { get; set; }
    public decimal ExpectedInvoiceTotal { get; set; }
    public decimal ActualInvoiceTotal { get; set; }
    public List<LegalEntityReconciliationSummaryDto> EntitySummaries { get; set; } = [];
    public PagedResult<LegalEntityOrderReconciliationRowDto> Orders { get; set; } = new();
}

public sealed class LegalEntityReconciliationOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SalePriority { get; set; }
}

public sealed class LegalEntityReconciliationSummaryDto
{
    public int LegalEntityId { get; set; }
    public string LegalEntityCode { get; set; } = string.Empty;
    public string LegalEntityName { get; set; } = string.Empty;
    public int SalePriority { get; set; }
    public int OrderCount { get; set; }
    public decimal AllocationGrossTotal { get; set; }
    public decimal ReversalTotal { get; set; }
    public decimal AllocationNetTotal { get; set; }
    public decimal ExpectedInvoiceTotal { get; set; }
    public decimal ActualInvoiceTotal { get; set; }
    public int InvoiceCount { get; set; }
    public int IssuedInvoiceCount { get; set; }
    public decimal InvoiceDifference => ActualInvoiceTotal - ExpectedInvoiceTotal;
    public bool InvoiceMatches => Math.Abs(InvoiceDifference) <= 0.01m;
}

public sealed class LegalEntityOrderReconciliationRowDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; set; }
    public string LegalEntityCodes { get; set; } = string.Empty;
    public decimal UnifiedOrderTotal { get; set; }
    public decimal AllocationGrossTotal { get; set; }
    public decimal ReversalTotal { get; set; }
    public decimal AllocationNetTotal { get; set; }
    public decimal ExpectedInvoiceTotal { get; set; }
    public decimal ActualInvoiceTotal { get; set; }
    public int InvoiceCount { get; set; }
    public int IssuedInvoiceCount { get; set; }
    public decimal OrderDifference => AllocationGrossTotal - UnifiedOrderTotal;
    public decimal InvoiceDifference => ActualInvoiceTotal - ExpectedInvoiceTotal;
    public bool OrderMatches => Math.Abs(OrderDifference) <= 0.01m;
    public bool InvoiceMatches => Math.Abs(InvoiceDifference) <= 0.01m;
    public bool IsBalanced => OrderMatches && InvoiceMatches;
}

public sealed class LegalEntityOrderBreakdownDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; set; }
    public decimal UnifiedOrderTotal { get; set; }
    public decimal AllocationGrossTotal { get; set; }
    public decimal ReversalTotal { get; set; }
    public decimal AllocationNetTotal { get; set; }
    public decimal ExpectedInvoiceTotal { get; set; }
    public decimal ActualInvoiceTotal { get; set; }
    public decimal OrderDifference => AllocationGrossTotal - UnifiedOrderTotal;
    public decimal InvoiceDifference => ActualInvoiceTotal - ExpectedInvoiceTotal;
    public bool OrderMatches => Math.Abs(OrderDifference) <= 0.01m;
    public bool InvoiceMatches => Math.Abs(InvoiceDifference) <= 0.01m;
    public bool IsBalanced => OrderMatches && InvoiceMatches;
    public List<LegalEntityOrderEntityBreakdownDto> EntitySummaries { get; set; } = [];
    public List<LegalEntityAllocationLineDto> Allocations { get; set; } = [];
    public List<LegalEntityChildInvoiceDto> Invoices { get; set; } = [];
}

public sealed class LegalEntityOrderEntityBreakdownDto
{
    public int LegalEntityId { get; set; }
    public string LegalEntityCode { get; set; } = string.Empty;
    public string LegalEntityName { get; set; } = string.Empty;
    public int SalePriority { get; set; }
    public decimal AllocationGrossTotal { get; set; }
    public decimal ReversalTotal { get; set; }
    public decimal AllocationNetTotal { get; set; }
    public decimal ExpectedInvoiceTotal { get; set; }
    public decimal ActualInvoiceTotal { get; set; }
    public int InvoiceCount { get; set; }
}

public sealed class LegalEntityAllocationLineDto
{
    public int AllocationId { get; set; }
    public int OrderLineId { get; set; }
    public int LegalEntityId { get; set; }
    public string LegalEntityCode { get; set; } = string.Empty;
    public string LegalEntityName { get; set; } = string.Empty;
    public int SalePriority { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public bool HasInputInvoice { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal NetAmount { get; set; }
    public decimal ReversedBaseQuantity { get; set; }
    public decimal ReversedAmount { get; set; }
    public decimal RemainingAmount => NetAmount - ReversedAmount;
    public string AllocationSource { get; set; } = string.Empty;
}

public sealed class LegalEntityChildInvoiceDto
{
    public int InvoiceHeadId { get; set; }
    public int? LegalEntityId { get; set; }
    public string LegalEntityCode { get; set; } = string.Empty;
    public string LegalEntityName { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public decimal GrandTotal { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderInvoiceNo { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public bool IsIssued => IssuedAtUtc.HasValue || !string.IsNullOrWhiteSpace(ProviderInvoiceNo);
}
