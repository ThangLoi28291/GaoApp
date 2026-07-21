using FluentValidation;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Validators.Products;

public sealed class ProductVariantRowDtoValidator : AbstractValidator<ProductVariantRowDto>
{
    public ProductVariantRowDtoValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU không được rỗng.")
            .MaximumLength(100).WithMessage("SKU tối đa 100 ký tự.");

        RuleFor(x => x.ProductVariantName)
            .MaximumLength(255).When(x => !string.IsNullOrWhiteSpace(x.ProductVariantName))
            .WithMessage("Tên biến thể tối đa 255 ký tự.");

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá vốn không hợp lệ.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).When(x => x.Price.HasValue)
            .WithMessage("Giá bán không hợp lệ.");
    }
}