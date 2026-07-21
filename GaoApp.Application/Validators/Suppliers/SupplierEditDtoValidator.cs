using FluentValidation;
using GaoApp.Application.DTOs.Suppliers;

namespace GaoApp.Application.Validators.Suppliers;

public sealed class SupplierEditDtoValidator : AbstractValidator<SupplierEditDto>
{
    public SupplierEditDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên nhà cung cấp.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp không được vượt quá 200 ký tự.");

        RuleFor(x => x.Code)
            .MaximumLength(50).WithMessage("Mã nhà cung cấp không được vượt quá 50 ký tự.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Email)
            .MaximumLength(150).WithMessage("Email không được vượt quá 150 ký tự.")
            .EmailAddress().WithMessage("Email không đúng định dạng.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Address)
            .MaximumLength(300).WithMessage("Địa chỉ không được vượt quá 300 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.Address));

        RuleFor(x => x.ContactName)
            .MaximumLength(150).WithMessage("Người liên hệ không được vượt quá 150 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactName));

        RuleFor(x => x.TaxCode)
            .MaximumLength(50).WithMessage("Mã số thuế không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.TaxCode));

        RuleFor(x => x.Note)
            .MaximumLength(1000).WithMessage("Ghi chú không được vượt quá 1000 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.Note));
    }
}