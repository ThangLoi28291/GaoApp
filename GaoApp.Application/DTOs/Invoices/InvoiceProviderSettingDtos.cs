using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceProviderSettingListItemDto
{
    public int Id { get; set; }

    public string ProviderCode { get; set; } = "VIETTEL";

    public bool IsProduction { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string SupplierTaxCode { get; set; } = string.Empty;

    public string InvoiceType { get; set; } = string.Empty;

    public string TemplateCode { get; set; } = string.Empty;

    public string InvoiceSeries { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "VND";

    public string PaymentMethodName { get; set; } = "TM";

    public bool IsActive { get; set; }

    public string? Note { get; set; }
    public InvoiceProviderAuthMode AuthMode { get; set; } = InvoiceProviderAuthMode.BasicAuth;
}

public class UpsertInvoiceProviderSettingRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "ProviderCode không được trống.")]
    [StringLength(50)]
    public string ProviderCode { get; set; } = "VIETTEL";

    public bool IsProduction { get; set; }

    [Required(ErrorMessage = "BaseUrl không được trống.")]
    [StringLength(500)]
    public string BaseUrl { get; set; } = "https://api-vinvoice.viettel.vn/services/einvoiceapplication/api";

    [Required(ErrorMessage = "Username không được trống.")]
    [StringLength(150)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password không được trống.")]
    [StringLength(500)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "SupplierTaxCode không được trống.")]
    [StringLength(20)]
    public string SupplierTaxCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "InvoiceType không được trống.")]
    [StringLength(20)]
    public string InvoiceType { get; set; } = "1";

    [Required(ErrorMessage = "TemplateCode không được trống.")]
    [StringLength(20)]
    public string TemplateCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "InvoiceSeries không được trống.")]
    [StringLength(25)]
    public string InvoiceSeries { get; set; } = string.Empty;

    [Required(ErrorMessage = "CurrencyCode không được trống.")]
    [StringLength(3)]
    public string CurrencyCode { get; set; } = "VND";

    public decimal ExchangeRate { get; set; } = 1m;

    [StringLength(50)]
    public string PaymentMethodName { get; set; } = "TM";

    public bool CusGetInvoiceRight { get; set; } = true;

    public bool DefaultPaymentStatus { get; set; } = true;

    public bool IsActive { get; set; } = true;

    [StringLength(500)]
    public string? Note { get; set; }
    public InvoiceProviderAuthMode AuthMode { get; set; } = InvoiceProviderAuthMode.BasicAuth;
}

public class TestInvoiceProviderLoginResultDto
{
    public bool IsSuccess { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? TokenPreview { get; set; }

    public long DurationMs { get; set; }

    public int? TotalRows { get; set; }

    public string? AuthModeName { get; set; }
}