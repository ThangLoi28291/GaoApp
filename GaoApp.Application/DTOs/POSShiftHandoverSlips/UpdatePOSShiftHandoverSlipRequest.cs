namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public sealed class UpdatePOSShiftHandoverSlipRequest : CreatePOSShiftHandoverSlipRequest
{
    public byte[] RowVersion { get; set; } = [];
}
