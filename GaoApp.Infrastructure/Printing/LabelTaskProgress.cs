using GaoApp.Domain.Entities;

namespace GaoApp.Infrastructure.Printing;

public static class LabelTaskProgress
{
    public static void Apply(ProductLabelTask task, LabelPrintPayload payload, IReadOnlyList<LabelQuantity> accepted, int? actor)
    {
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson).Select(line =>
        {
            var item = payload.Items.SingleOrDefault(x => x.Product.VariantId == line.Product.VariantId);
            var quantity = accepted.SingleOrDefault(x => x.VariantId == line.Product.VariantId)?.Quantity ?? 0;
            return item is null ? line : line with
            {
                Sent = line.Sent + quantity,
                Handled = line.Handled || item.CountsForProgress && quantity > 0
            };
        }).ToList();
        task.LinesJson = LabelJson.Write(lines);
        CompleteIfHandled(task, actor);
    }

    public static void CompleteIfHandled(ProductLabelTask task, int? actor)
    {
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson).Where(x => !x.Removed).ToList();
        var complete = lines.Count > 0 && lines.All(x => x.Done || x.Skipped);
        if (complete == task.Completed) return;
        task.Completed = complete;
        task.CompletedAtUtc = complete ? DateTime.UtcNow : null;
        task.CompletedByUserId = complete ? actor : null;
    }
}
