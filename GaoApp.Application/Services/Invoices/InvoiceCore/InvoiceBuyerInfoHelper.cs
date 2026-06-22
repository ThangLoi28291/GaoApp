using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceBuyerInfoHelper
{
    public static bool IsIssuedLike(InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    public static string NormalizeBuyerType(string? buyerType)
    {
        buyerType = (buyerType ?? string.Empty).Trim();

        if (buyerType.Equals(InvoiceBuyerTypes.NoInvoice, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.NoInvoice;

        if (buyerType.Equals(InvoiceBuyerTypes.Individual, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.Individual;

        if (buyerType.Equals(InvoiceBuyerTypes.Business, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.Business;

        return InvoiceBuyerTypes.NoInvoice;
    }

    public static string NormalizeBuyerTaxCodeForViettel(string? taxCode)
    {
        taxCode = (taxCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(taxCode))
            return string.Empty;

        return taxCode
            .Replace(" ", "")
            .Replace(".", "")
            .Trim()
            .ToUpperInvariant();
    }

    public static bool IsValidBuyerTaxCodeForViettel(string? taxCode)
    {
        taxCode = NormalizeBuyerTaxCodeForViettel(taxCode);

        if (string.IsNullOrWhiteSpace(taxCode))
            return true;

        if (taxCode.Length > 20)
            return false;

        return taxCode.All(x => char.IsLetterOrDigit(x) || x == '-');
    }

    public static string? NormalizeNullableText(string? value)
    {
        value = (value ?? string.Empty).Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    public static Result<bool> ValidateBuyerInfo(
        string buyerType,
        string? buyerName,
        string? buyerLegalName,
        string buyerTaxCode)
    {
        if (!IsValidBuyerTaxCodeForViettel(buyerTaxCode))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceBuyer.TaxCodeInvalid",
                    "MST/mã định danh không hợp lệ. Chỉ cho phép chữ, số, dấu gạch ngang và tối đa 20 ký tự."));
        }

        if (buyerType == InvoiceBuyerTypes.NoInvoice)
            return Result<bool>.Success(true);

        if (buyerType == InvoiceBuyerTypes.Individual)
        {
            if (string.IsNullOrWhiteSpace(buyerName))
            {
                return Result<bool>.Failure(
                    Error.Validation(
                        "InvoiceBuyer.BuyerNameRequired",
                        "Vui lòng nhập tên khách hàng cá nhân."));
            }

            return Result<bool>.Success(true);
        }

        if (buyerType == InvoiceBuyerTypes.Business)
        {
            if (string.IsNullOrWhiteSpace(buyerLegalName))
            {
                return Result<bool>.Failure(
                    Error.Validation(
                        "InvoiceBuyer.BuyerLegalNameRequired",
                        "Vui lòng nhập tên đơn vị/công ty/hộ kinh doanh."));
            }

            return Result<bool>.Success(true);
        }

        return Result<bool>.Failure(
            Error.Validation(
                "InvoiceBuyer.TypeInvalid",
                "Loại người mua không hợp lệ."));
    }

    public static void ApplyBuyerInfoToInvoiceHead(
        InvoiceHead invoiceHead,
        string buyerType,
        string? buyerName,
        string? buyerLegalName,
        string buyerTaxCode,
        string? buyerAddress,
        string? buyerEmail,
        string? buyerPhone)
    {
        invoiceHead.BuyerType = buyerType;

        if (buyerType == InvoiceBuyerTypes.NoInvoice)
        {
            invoiceHead.BuyerName = null;
            invoiceHead.BuyerLegalName = null;
            invoiceHead.BuyerTaxCode = null;
            invoiceHead.BuyerAddress = null;
            invoiceHead.BuyerEmail = null;
            invoiceHead.BuyerPhone = null;
            return;
        }

        if (buyerType == InvoiceBuyerTypes.Individual)
        {
            invoiceHead.BuyerName = buyerName;
            invoiceHead.BuyerLegalName = null;
            invoiceHead.BuyerTaxCode = string.IsNullOrWhiteSpace(buyerTaxCode)
                ? null
                : buyerTaxCode;
            invoiceHead.BuyerAddress = buyerAddress;
            invoiceHead.BuyerEmail = buyerEmail;
            invoiceHead.BuyerPhone = buyerPhone;
            return;
        }

        invoiceHead.BuyerName = buyerName;
        invoiceHead.BuyerLegalName = buyerLegalName;
        invoiceHead.BuyerTaxCode = string.IsNullOrWhiteSpace(buyerTaxCode)
            ? null
            : buyerTaxCode;
        invoiceHead.BuyerAddress = buyerAddress;
        invoiceHead.BuyerEmail = buyerEmail;
        invoiceHead.BuyerPhone = buyerPhone;
    }

    public static InvoiceBuyerLookupDto MapCustomerToBuyerLookup(
        Customer customer,
        string buyerType,
        string taxCode)
    {
        if (buyerType == InvoiceBuyerTypes.Individual)
        {
            return new InvoiceBuyerLookupDto
            {
                IsFound = true,
                BuyerType = InvoiceBuyerTypes.Individual,
                BuyerName = customer.Name,
                BuyerLegalName = null,
                BuyerTaxCode = string.IsNullOrWhiteSpace(customer.TaxCode)
                    ? taxCode
                    : customer.TaxCode,
                BuyerAddress = customer.Address,
                BuyerEmail = customer.Email,
                BuyerPhone = customer.Phone,
                Source = "customer"
            };
        }

        return new InvoiceBuyerLookupDto
        {
            IsFound = true,
            BuyerType = InvoiceBuyerTypes.Business,
            BuyerName = null,
            BuyerLegalName = customer.Name,
            BuyerTaxCode = string.IsNullOrWhiteSpace(customer.TaxCode)
                ? taxCode
                : customer.TaxCode,
            BuyerAddress = customer.Address,
            BuyerEmail = customer.Email,
            BuyerPhone = customer.Phone,
            Source = "customer"
        };
    }

    public static InvoiceBuyerLookupDto MapInvoiceHistoryToBuyerLookup(
        InvoiceHead invoice,
        string taxCode)
    {
        return new InvoiceBuyerLookupDto
        {
            IsFound = true,
            BuyerType = NormalizeBuyerType(invoice.BuyerType),
            BuyerName = invoice.BuyerName,
            BuyerLegalName = invoice.BuyerLegalName,
            BuyerTaxCode = string.IsNullOrWhiteSpace(invoice.BuyerTaxCode)
                ? taxCode
                : invoice.BuyerTaxCode,
            BuyerAddress = invoice.BuyerAddress,
            BuyerEmail = invoice.BuyerEmail,
            BuyerPhone = invoice.BuyerPhone,
            Source = "invoice-history"
        };
    }
}