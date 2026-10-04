using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common;
using GaoApp.Domain.Constants;

namespace GaoApp.Application.DTOs.Customers;

public static class CustomerManagementPriceTiers
{
    public const string All = "ALL";
    public const string Retail = CustomerPriceTiers.Retail;
    public const string Wholesale = CustomerPriceTiers.Wholesale;
}

public sealed class CustomerManagementQueryRequest
{
    public string? SearchString { get; set; }
    public string? PriceTier { get; set; }
    public bool? Status { get; set; }
    public bool? HaveDebt { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class CustomerManagementSummaryDto
{
    public int TotalCustomers { get; set; }
    public int ActiveCustomers { get; set; }
    public int InactiveCustomers { get; set; }
    public int DebtEnabledCustomers { get; set; }
}

public sealed class CustomerManagementPageDto
{
    public CustomerManagementSummaryDto Summary { get; set; } = new();
    public PagedResult<CustomerListItemDto> Paged { get; set; } = new();
}

public sealed class CustomerListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string PriceTier { get; set; } = CustomerPriceTiers.Retail;
    public bool HaveDebt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CustomerQuickViewDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public string PriceTier { get; set; } = CustomerPriceTiers.Retail;
    public string CustomerGroup { get; set; } = string.Empty;
    public bool HaveDebt { get; set; }
    public bool IsActive { get; set; }
    public bool IsImportedFromOldSystem { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CustomerEditDto
{
    public bool AskBeforePrintingReceipt { get; set; }
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(200, ErrorMessage = "Tên khách hàng không được vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã khách hàng không được vượt quá 50 ký tự.")]
    public string? Code { get; set; }

    [StringLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
    public string? Email { get; set; }

    [StringLength(50, ErrorMessage = "Mã số thuế không được vượt quá 50 ký tự.")]
    public string? TaxCode { get; set; }

    [StringLength(300, ErrorMessage = "Địa chỉ không được vượt quá 300 ký tự.")]
    public string? Address { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Note { get; set; }

    [Required]
    public string PriceTier { get; set; } = CustomerPriceTiers.Retail;

    public bool HaveDebt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
