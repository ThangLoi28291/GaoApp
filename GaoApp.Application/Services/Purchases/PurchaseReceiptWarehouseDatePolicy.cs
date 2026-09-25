namespace GaoApp.Application.Services.Purchases;

using GaoApp.Application.Common.Exceptions;

public static class PurchaseReceiptWarehouseDatePolicy
{
    public static DateTime Resolve(
        DateTime documentDate,
        IReadOnlyCollection<DateTime?> activeInvoiceDates)
    {
        if (activeInvoiceDates.Count > 1)
        {
            throw new InvalidOperationException(
                "Phiếu nhập đang liên kết nhiều hóa đơn đầu vào. Vui lòng xử lý dữ liệu trước khi duyệt.");
        }

        var date = activeInvoiceDates.SingleOrDefault() ?? documentDate;
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
    }

    public static DateTime ResolveForConfirmation(
        DateTime documentDate,
        IReadOnlyCollection<DateTime?> activeInvoiceDates)
    {
        try
        {
            return Resolve(documentDate, activeInvoiceDates);
        }
        catch (InvalidOperationException exception)
        {
            throw new BusinessRuleException(exception.Message);
        }
    }
}
