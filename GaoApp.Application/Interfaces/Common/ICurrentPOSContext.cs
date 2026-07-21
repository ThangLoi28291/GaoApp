namespace GaoApp.Application.Interfaces.Common;

public interface ICurrentPOSContext
{
    int StoreId { get; }
    int TerminalId { get; }
    int? UserId { get; }

    bool IsAvailable { get; }
}