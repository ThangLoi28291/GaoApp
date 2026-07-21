using System.Text.Json.Serialization;
using GaoApp.Application.Common.Results;

namespace GaoApp.Web.Common.Responses;

/// <summary>
/// Response tối thiểu dùng cho các thao tác AJAX cũ.
/// Tên JSON được cố định để không phá JavaScript đang đọc success/message.
/// </summary>
public sealed class AjaxResponse
{
    private AjaxResponse(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    [JsonPropertyName("success")]
    public bool Success { get; }

    [JsonPropertyName("message")]
    public string Message { get; }

    public static AjaxResponse FromResult(Result result, string successMessage)
        => new(
            result.IsSuccess,
            result.IsSuccess ? successMessage : result.Error.Message);
}
