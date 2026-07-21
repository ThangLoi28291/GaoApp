using FluentValidation;
using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Validators.Inventory;

public class InventoryAdjustmentLineRequestDtoValidator
    : AbstractValidator<InventoryAdjustmentLineRequestDto>
{
    public InventoryAdjustmentLineRequestDtoValidator()
    {
        RuleFor(x => x.ProductVariantId)
            .GreaterThan(0)
            .WithMessage("Sản phẩm không hợp lệ.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Số lượng điều chỉnh phải lớn hơn 0.");

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .WithMessage("Ghi chú dòng không được vượt quá 1000 ký tự.");
    }
}