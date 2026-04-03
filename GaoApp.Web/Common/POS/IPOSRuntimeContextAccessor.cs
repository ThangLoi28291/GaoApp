namespace GaoApp.Web.Common.POS;

public interface IPOSRuntimeContextAccessor
{
    int? StoreId { get; }
    string? StoreName { get; }

    int? TerminalId { get; }
    string? TerminalName { get; }
    string? TerminalCode { get; }

    int? UserId { get; }
    string? UserName { get; }
}