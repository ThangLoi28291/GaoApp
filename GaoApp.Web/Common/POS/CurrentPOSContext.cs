using GaoApp.Application.Interfaces.Common;

namespace GaoApp.Web.Common.POS;

public sealed class CurrentPOSContext : ICurrentPOSContext
{
    private readonly IPOSRuntimeContextAccessor _runtime;

    public CurrentPOSContext(IPOSRuntimeContextAccessor runtime)
    {
        _runtime = runtime;
    }

    public int StoreId => _runtime.StoreId ?? 0;
    public int TerminalId => _runtime.TerminalId ?? 0;
    public int? UserId => _runtime.UserId;

    public string? TerminalCode => _runtime.TerminalCode;
    public string? TerminalName => _runtime.TerminalName;

    public bool IsAvailable => StoreId > 0 && TerminalId > 0;
}