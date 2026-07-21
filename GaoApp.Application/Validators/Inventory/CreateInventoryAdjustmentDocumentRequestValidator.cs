using FluentValidation;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Validators.Inventory;

public class CreateInventoryAdjustmentDocumentRequestValidator
    : AbstractValidator<CreateInventoryAdjustmentDocumentRequest>
{
    public CreateInventoryAdjustmentDocumentRequestValidator()
    {
        RuleFor(x => x.WarehouseId)
            .GreaterThan(0)
            .WithMessage("Vui lòng chọn kho.");

        RuleFor(x => x.AdjustmentType)
            .Must(x => x == InventoryTransactionType.AdjustmentIncrease ||
                       x == InventoryTransactionType.AdjustmentDecrease)
            .WithMessage("Loại điều chỉnh không hợp lệ.");

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .WithMessage("Ghi chú không được vượt quá 1000 ký tự.");

        RuleFor(x => x.Lines)
            .NotEmpty()
            .WithMessage("Phiếu điều chỉnh phải có ít nhất 1 dòng sản phẩm.");

        RuleForEach(x => x.Lines)
            .SetValidator(new InventoryAdjustmentLineRequestDtoValidator());

        RuleFor(x => x.Lines)
      .Custom((lines, context) =>
      {
          for (var i = 0; i < lines.Count; i++)
          {
              var line = lines[i];

              if (line.UnitCost.HasValue && line.UnitCost.Value <= 0)
              {
                  context.AddFailure(
                      $"Lines[{i}].UnitCost",
                      "Giá vốn phải lớn hơn 0.");
              }

              if (line.ProvisionalUnitCost.HasValue && line.ProvisionalUnitCost.Value <= 0)
              {
                  context.AddFailure(
                      $"Lines[{i}].ProvisionalUnitCost",
                      "Giá vốn tạm phải lớn hơn 0.");
              }
          }
      });
    }
}