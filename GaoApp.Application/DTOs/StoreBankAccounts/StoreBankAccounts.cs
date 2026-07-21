using GaoApp.Domain.Enums;


namespace GaoApp.Application.DTOs.StoreBankAccounts;

public class StoreBankAccountUpsertDto
{
    public int? Id { get; set; }

    public string BankCode { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public string? VietQrBankBin { get; set; }

    public string? NoteTemplate { get; set; }

    public BankQrRenderMode QrRenderMode { get; set; }

    public BankQrConfirmMode ConfirmMode { get; set; }

    public string ProviderCode { get; set; } = "LOCAL";
}

