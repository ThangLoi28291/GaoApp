using FluentValidation;
using GaoApp.Application.DTOs.POS;

namespace GaoApp.Application.Validators.POS;

public sealed class OrderListQueryDtoValidator : AbstractValidator<OrderListQueryDto>
{
    public OrderListQueryDtoValidator()
    {
        RuleFor(query => query.Product)
            .MaximumLength(320)
            .WithMessage("Thông tin tìm sản phẩm không được vượt quá 320 ký tự.");
    }
}
