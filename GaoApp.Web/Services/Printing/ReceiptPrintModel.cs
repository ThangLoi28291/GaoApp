using GaoApp.Application.DTOs.POS;

namespace GaoApp.Web.Services.Printing;

public sealed record ReceiptPrintModel(OrderReceiptDto Receipt, IReadOnlyList<ReceiptTemplateOption> Templates,
    int StoreId, int? TerminalId, bool AutoPrint, string? Size = null);
