namespace GaoApp.Application.Common;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message, Exception? inner = null)
        : base(message, inner) { }
}
