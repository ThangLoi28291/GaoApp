using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("StoreBankAccounts")]
public class StoreBankAccount : BaseStoreEntity
{
    [StringLength(30)]
    public string BankCode { get; set; } = string.Empty;
    // VD: ACB, VCB, MBBANK

    [StringLength(100)]
    public string BankName { get; set; } = string.Empty;

    [StringLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [StringLength(200)]
    public string AccountName { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public BankQrRenderMode QrRenderMode { get; set; } = BankQrRenderMode.LocalEmvQr;

    public BankQrConfirmMode ConfirmMode { get; set; } = BankQrConfirmMode.Manual;

    [StringLength(50)]
    public string ProviderCode { get; set; } = "LOCAL";
    // LOCAL / VIETQR / ACB / PAYOS...

    [StringLength(500)]
    public string? NoteTemplate { get; set; } = "POS-{OrderId}-{QrRequestId}";

    [StringLength(100)]
    public string? VietQrBankBin { get; set; }
    // Mã BIN ngân hàng dùng để build VietQR local

    [StringLength(500)]
    public string? ApiClientId { get; set; }

    public string? ApiSecretEncrypted { get; set; }

    [StringLength(500)]
    public string? CallbackSecret { get; set; }

    public ICollection<PosPaymentQrRequest> QrRequests { get; set; } = new List<PosPaymentQrRequest>();
}