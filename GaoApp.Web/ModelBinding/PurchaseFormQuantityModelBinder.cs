using System.Globalization;
using GaoApp.Application.DTOs.Purchases;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GaoApp.Web.ModelBinding;

/// <summary>Native number inputs and review/conversion hidden fields post an ungrouped decimal.
/// Keep their meaning independent of the server's Windows culture. JSON and monetary fields
/// retain their existing binders, validation and application policies.</summary>
public sealed class PurchaseFormQuantityModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var metadata = context.Metadata;
        if (metadata.ModelType != typeof(decimal)) return null;
        var type = metadata.ContainerType;
        var quantity = metadata.PropertyName == "Quantity" &&
            (type == typeof(PurchaseRequestLineInputDto) || type == typeof(PurchaseOrderLineInputDto) ||
             type == typeof(PurchaseRequestConversionLineRequest));
        var approval = type == typeof(PurchaseRequestApprovalLineRequest) && metadata.PropertyName == "ApprovedQuantity";
        return quantity || approval ? new PurchaseFormQuantityModelBinder() : null;
    }
}

public sealed class PurchaseFormQuantityModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var text = value.FirstValue?.Trim() ?? "";
        // Legacy manual clients may use one comma as the decimal separator; grouping and
        // mixed separators are deliberately ambiguous and must never multiply a quantity.
        if (!text.Contains('.')) text = text.Replace(',', '.');
        if (value.Values.Count == 1 && decimal.TryParse(text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var quantity))
            bindingContext.Result = ModelBindingResult.Success(quantity);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Số lượng không hợp lệ. Nhập số không có dấu phân cách hàng nghìn.");
        return Task.CompletedTask;
    }
}
