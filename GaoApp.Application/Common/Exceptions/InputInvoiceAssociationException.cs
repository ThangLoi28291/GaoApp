namespace GaoApp.Application.Common.Exceptions;

public sealed class InputInvoiceAssociationException : BusinessRuleException
{
    public const string AssociationChangedCode = "AssociationChanged";

    public InputInvoiceAssociationException(
        string code,
        string message,
        int? currentInputInvoiceHeadId = null)
        : base(message)
    {
        Code = code;
        CurrentInputInvoiceHeadId = currentInputInvoiceHeadId;
    }

    public string Code { get; }
    public int? CurrentInputInvoiceHeadId { get; }

    public static InputInvoiceAssociationException AssociationChanged(
        int? currentInputInvoiceHeadId = null)
        => new(
            AssociationChangedCode,
            "Liên kết hóa đơn đã thay đổi. Vui lòng tải lại trước khi tiếp tục.",
            currentInputInvoiceHeadId);
}
