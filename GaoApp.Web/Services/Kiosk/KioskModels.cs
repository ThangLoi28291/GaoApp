using System.ComponentModel.DataAnnotations;
namespace GaoApp.Web.Services.Kiosk;
public sealed class KioskCommand
{
    public Guid SessionKey { get; set; }
    public long Revision { get; set; }
    public Guid CommandId { get; set; }
    [Required, MaxLength(30)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? Barcode { get; set; }
    public int VariantId { get; set; }
    public int? UnitId { get; set; }
    public int LineId { get; set; }
    [Range(0, 999)] public decimal Quantity { get; set; } = 1;
}
public sealed class KioskCreateRequest
{
    [Range(1,int.MaxValue)] public int TerminalId { get; set; }
    [Range(1,int.MaxValue)] public int WarehouseId { get; set; }
}
public sealed record KioskSecretRequest([property:Required,MaxLength(64)] string Key);
public sealed record KioskPhoneRequest([property:Required,MaxLength(20)] string Phone);
public sealed record KioskPollRequest(bool RecentActivity = false, bool CheckPayment = false);
