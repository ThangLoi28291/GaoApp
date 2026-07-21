namespace GaoApp.Application.Common.Results;

/// <summary>
/// Lỗi validation chi tiết theo từng field.
/// Dùng để đẩy nhiều lỗi vào ModelState hoặc trả về API.
/// </summary>
public sealed class ValidationError
{
    public ValidationError(string propertyName, string errorMessage)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Tên field bị lỗi.
    /// Ví dụ: Name, Code, SortOrder...
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Thông điệp lỗi.
    /// </summary>
    public string ErrorMessage { get; }
}