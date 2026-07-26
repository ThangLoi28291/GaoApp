using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Web.Middlewares;

internal sealed record SafeExceptionDiagnostic(
    string ExceptionType,
    int HResult,
    string StackFrames,
    string InnerExceptionTypes,
    string Fingerprint);

internal static class SafeExceptionDiagnosticBuilder
{
    private const int MaxStackFrames = 40;
    private const int MaxInnerExceptions = 8;
    private const int MaxStackLength = 4096;

    public static SafeExceptionDiagnostic Build(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptionType = GetTypeName(exception.GetType());
        var stackFrames = BuildStackFrames(exception);
        var innerExceptionTypes = BuildInnerExceptionTypes(exception);
        var fingerprint = BuildFingerprint(
            exceptionType,
            stackFrames,
            innerExceptionTypes);

        return new SafeExceptionDiagnostic(
            exceptionType,
            exception.HResult,
            stackFrames,
            innerExceptionTypes,
            fingerprint);
    }

    private static string BuildStackFrames(Exception exception)
    {
        var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
        if (frames is not { Length: > 0 })
            return "unavailable";

        var methods = frames
            .Take(MaxStackFrames)
            .Select(frame => frame.GetMethod())
            .Where(method => method is not null)
            .Select(method =>
            {
                var declaringType = method!.DeclaringType?.FullName ?? "<global>";
                return $"{declaringType}.{method.Name}";
            });

        var result = string.Join(" > ", methods);
        if (string.IsNullOrWhiteSpace(result))
            return "unavailable";

        return result.Length <= MaxStackLength
            ? result
            : result[..MaxStackLength];
    }

    private static string BuildInnerExceptionTypes(Exception exception)
    {
        var types = new List<string>(MaxInnerExceptions);
        var current = exception.InnerException;

        while (current is not null && types.Count < MaxInnerExceptions)
        {
            types.Add(GetTypeName(current.GetType()));
            current = current.InnerException;
        }

        return types.Count == 0
            ? "none"
            : string.Join(" > ", types);
    }

    private static string BuildFingerprint(
        string exceptionType,
        string stackFrames,
        string innerExceptionTypes)
    {
        var input = string.Join(
            "\n",
            exceptionType,
            stackFrames,
            innerExceptionTypes);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static string GetTypeName(Type type) =>
        type.FullName ?? type.Name;
}
