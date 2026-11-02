using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public sealed record StockDocumentWorkflowContextDto(PurchaseReceiptSource ReceiptSource, int? PurchaseOrderId);
