using FluentValidation;
using GaoApp.Application.DTOs.Display;

namespace GaoApp.Application.Validators.Display;

public class UpsertDisplayPromotionDtoValidator
    : AbstractValidator<UpsertDisplayPromotionDto>
{
    public UpsertDisplayPromotionDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(250);

        RuleFor(x => x.MediaType)
            .NotEmpty()
            .Must(x => x is "image" or "video" or "text")
            .WithMessage("Loại media chỉ hỗ trợ image, video hoặc text.");

        RuleFor(x => x.MediaUrl)
            .MaximumLength(500);

        RuleFor(x => x.DurationSeconds)
            .InclusiveBetween(3, 60);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => !x.StartAt.HasValue || !x.EndAt.HasValue || x.EndAt > x.StartAt)
            .WithMessage("Ngày kết thúc phải lớn hơn ngày bắt đầu.");
    }
}