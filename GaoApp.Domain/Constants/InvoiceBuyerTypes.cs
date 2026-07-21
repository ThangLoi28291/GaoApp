namespace GaoApp.Domain.Constants;

/// <summary>
/// Phân loại người mua dùng cho hóa đơn điện tử.
/// </summary>
public static class InvoiceBuyerTypes
{
    /// <summary>
    /// Khách không lấy hóa đơn.
    /// Khi build Viettel: buyerNotGetInvoice = 1.
    /// </summary>
    public const string NoInvoice = "NoInvoice";

    /// <summary>
    /// Cá nhân.
    /// Khi build Viettel: dùng buyerName.
    /// </summary>
    public const string Individual = "Individual";

    /// <summary>
    /// Doanh nghiệp / tổ chức / hộ kinh doanh.
    /// Khi build Viettel: dùng buyerLegalName + buyerTaxCode.
    /// </summary>
    public const string Business = "Business";
}