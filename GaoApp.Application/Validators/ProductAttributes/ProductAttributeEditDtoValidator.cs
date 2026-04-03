using FluentValidation;
using GaoApp.Application.DTOs.ProductAttributes;

namespace GaoApp.Application.Validators.ProductAttributes;

public sealed class ProductAttributeEditDtoValidator : AbstractValidator<ProductAttributeEditDto>
{
    public ProductAttributeEditDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã thuộc tính.")
            .MaximumLength(30).WithMessage("Mã thuộc tính tối đa 30 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên thuộc tính.")
            .MaximumLength(200).WithMessage("Tên thuộc tính tối đa 200 ký tự.");
    }
}