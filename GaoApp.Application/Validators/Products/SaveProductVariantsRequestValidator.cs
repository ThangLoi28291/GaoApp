using FluentValidation;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Validators.Products;

public sealed class SaveProductVariantsRequestValidator : AbstractValidator<SaveProductVariantsRequest>
{
    public SaveProductVariantsRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("ProductId không hợp lệ.");

        RuleFor(x => x.Variants)
            .NotNull().WithMessage("Danh sách biến thể không hợp lệ.");

        RuleForEach(x => x.Variants)
            .SetValidator(new ProductVariantRowDtoValidator());
    }
}