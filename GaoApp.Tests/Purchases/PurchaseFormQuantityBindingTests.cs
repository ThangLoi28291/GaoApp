using System.Globalization;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Web.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseFormQuantityBindingTests
{
    [Theory]
    [InlineData("vi-VN", "2.5", "2.5")]
    [InlineData("vi-VN", "1.125", "1.125")]
    [InlineData("vi-VN", "2,5", "2.5")]
    [InlineData("en-US", "2.5", "2.5")]
    [InlineData("en-US", "2,5", "2.5")]
    [InlineData("fr-FR", "0.001", "0.001")]
    [InlineData("vi-VN", "0", "0")]
    public async Task Native_form_quantity_does_not_treat_decimal_point_as_thousands(string culture, string text, string expected)
    {
        var context = Context(culture, text);
        await new PurchaseFormQuantityModelBinder().BindModelAsync(context);
        Assert.True(context.Result.IsModelSet); Assert.Equal(0,context.ModelState.ErrorCount);
        Assert.Equal(decimal.Parse(expected,CultureInfo.InvariantCulture),context.Result.Model);
    }

    [Theory]
    [InlineData("1.234,5")]
    [InlineData("1,234.5")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData("NaN")]
    public async Task Ambiguous_or_invalid_quantity_is_rejected_instead_of_changing_its_magnitude(string text)
    {
        var context=Context("vi-VN",text);await new PurchaseFormQuantityModelBinder().BindModelAsync(context);
        Assert.False(context.Result.IsModelSet);Assert.False(context.ModelState.IsValid);
    }

    [Fact]
    public void Provider_only_targets_the_four_original_purchase_form_quantity_fields()
    {
        var metadata=new EmptyModelMetadataProvider();var binder=new PurchaseFormQuantityModelBinderProvider();
        foreach(var type in new[]{typeof(PurchaseRequestLineInputDto),typeof(PurchaseOrderLineInputDto),typeof(PurchaseRequestConversionLineRequest),typeof(PurchaseRequestApprovalLineRequest)})
            Assert.IsType<PurchaseFormQuantityModelBinder>(binder.GetBinder(new ProviderContext(metadata.GetMetadataForProperty(type,type==typeof(PurchaseRequestApprovalLineRequest)?"ApprovedQuantity":"Quantity"))));
        foreach(var pair in new[]{(typeof(PurchaseRequestConversionLineRequest),"UnitPriceBeforeVat"),(typeof(CreatePurchaseReceiptLineRequest),"Quantity"),(typeof(PurchaseRequestLineInputDto),"ProductName")})
            Assert.Null(binder.GetBinder(new ProviderContext(metadata.GetMetadataForProperty(pair.Item1,pair.Item2))));
    }

    private static DefaultModelBindingContext Context(string culture,string text)=>new()
    {
        ModelMetadata=new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal)),ModelName="Lines[0].Quantity",
        ModelState=new ModelStateDictionary(),ValueProvider=new FormValueProvider(BindingSource.Form,new FormCollection(new Dictionary<string,StringValues>{["Lines[0].Quantity"]=text}),new CultureInfo(culture))
    };
    private sealed class ProviderContext(ModelMetadata metadata):ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo {get;}=new();
        public override ModelMetadata Metadata=>metadata;
        public override IModelMetadataProvider MetadataProvider {get;}=new EmptyModelMetadataProvider();
        public override IModelBinder CreateBinder(ModelMetadata metadata)=>throw new NotSupportedException();
    }
}
