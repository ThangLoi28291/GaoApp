namespace GaoApp.Application.Common.Exceptions;

public class BusinessRuleException : Exception
{
    /// <summary>
    /// Thông điệp nghiệp vụ do application service chủ động tạo và cho phép hiển thị.
    /// </summary>
    public string SafeMessage => Message;

    public BusinessRuleException(string message) : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
