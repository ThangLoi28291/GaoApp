namespace GaoApp.Web.Areas.Admin.Models.Inventory;

public static class TaxCodePresentation
{
    public static string? Format(string? value)
    {
        var text = value?.Trim();
        return text is { Length: 13 } && text.All(char.IsAsciiDigit)
            ? text.Insert(10, "-")
            : text;
    }
}
