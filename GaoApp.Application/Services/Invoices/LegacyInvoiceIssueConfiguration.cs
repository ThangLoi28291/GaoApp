using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class LegacyInvoiceIssueConfiguration
{
    // Once submission has started, retries and UUID lookups must retain the
    // submitted identity. The original GaoStore JSON remains untouched.
    public static bool UseCurrent(InvoiceHead invoice) => Eligible(
        invoice.LegacySourceId, invoice.LegacyReadOnly, invoice.ProviderStatus,
        invoice.InvoiceNumber, invoice.ProviderInvoiceNo, invoice.IssuedAtUtc,
        invoice.ProviderTransactionId);

    public static bool UseCurrent(InvoiceHeadDto invoice) => Eligible(
        invoice.LegacySourceId, invoice.LegacyReadOnly, invoice.ProviderStatus,
        invoice.InvoiceNumber, invoice.ProviderInvoiceNo, invoice.IssuedAtUtc,
        invoice.ProviderTransactionId);

    private static bool Eligible(long? legacyId, bool readOnly, InvoiceProviderStatus status,
        string? number, string? providerNumber, DateTime? issuedAt, string? providerTransaction)
        => legacyId.HasValue && !readOnly
           && status is InvoiceProviderStatus.LocalDraft or InvoiceProviderStatus.ReadyToIssue or InvoiceProviderStatus.Previewed
           && string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(providerNumber)
           && issuedAt == null && string.IsNullOrWhiteSpace(providerTransaction);

    public static void Apply(InvoiceHeadDto invoice, InvoiceProviderSetting setting)
    {
        invoice.InvoiceProviderSettingId = setting.Id;
        invoice.ProviderCode = setting.ProviderCode;
        invoice.SupplierTaxCode = setting.SupplierTaxCode;
        invoice.InvoiceType = setting.InvoiceType;
        invoice.TemplateCode = setting.TemplateCode;
        invoice.InvoiceSeries = setting.InvoiceSeries;
    }

    public static void Apply(InvoiceHead invoice, InvoiceProviderSetting setting)
    {
        invoice.InvoiceProviderSettingId = setting.Id;
        invoice.ProviderCode = setting.ProviderCode;
        invoice.SupplierTaxCode = setting.SupplierTaxCode;
        invoice.InvoiceType = setting.InvoiceType;
        invoice.TemplateCode = setting.TemplateCode;
        invoice.InvoiceSeries = setting.InvoiceSeries;
    }
}
