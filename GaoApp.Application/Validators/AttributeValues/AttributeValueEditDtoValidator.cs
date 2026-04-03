using FluentValidation;
using GaoApp.Application.DTOs.AttributeValues;

namespace GaoApp.Application.Validators.AttributeValues;

public sealed class AttributeValueEditDtoValidator : AbstractValidator<AttributeValueEditDto>
{
    public AttributeValueEditDtoValidator()
    {
        RuleFor(x => x.AttributeId)
            .GreaterThan(0).WithMessage("Vui lòng chọn thuộc tính.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã giá trị.")
            .MaximumLength(30).WithMessage("Mã giá trị tối đa 30 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên giá trị.")
            .MaximumLength(200).WithMessage("Tên giá trị tối đa 200 ký tự.");
    }
}