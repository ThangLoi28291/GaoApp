using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.StoreBankAccounts;

public static class StoreBankAccountIndexQrModes
{
    public const string All = "all";
    public const string LocalEmvQr = "local-emv-qr";
    public const string VietQrQuickLink = "vietqr-quick-link";
    public const string ProviderApi = "provider-api";
}

public static class StoreBankAccountIndexConfirmModes
{
    public const string All = "all";
    public const string Manual = "manual";
    public const string Callback = "callback";
    public const string Polling = "polling";
}

public static class StoreBankAccountIndexLifecycles
{
    public const string All = "all";
    public const string Active = "active";
    public const string Inactive = "inactive";
}

public static class StoreBankAccountIndexDefaults
{
    public const string All = "all";
    public const string Default = "default";
    public const string NotDefault = "not-default";
}

public sealed class StoreBankAccountIndexQueryRequest
{
    public string? Keyword { get; set; }
    public string? QrMode { get; set; }
    public string? ConfirmMode { get; set; }
    public string? Lifecycle { get; set; }
    public string? DefaultRole { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class StoreBankAccountIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize)));
    public StoreBankAccountIndexSummaryDto Summary { get; set; } = new();
    public List<StoreBankAccountIndexItemDto> Items { get; set; } = new();
}

public sealed class StoreBankAccountIndexSummaryDto
{
    public int TotalAccounts { get; set; }
    public int ActiveAccounts { get; set; }
    public int InactiveAccounts { get; set; }
    public int DefaultAccounts { get; set; }
    public int ActiveDefaultAccounts { get; set; }
    public int InactiveDefaultAccounts { get; set; }
}

public sealed class StoreBankAccountIndexItemDto
{
    public int BankAccountId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string? VietQrBankBin { get; set; }
    public string? NoteTemplate { get; set; }
    public BankQrRenderMode QrRenderMode { get; set; }
    public BankQrConfirmMode ConfirmMode { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
}
