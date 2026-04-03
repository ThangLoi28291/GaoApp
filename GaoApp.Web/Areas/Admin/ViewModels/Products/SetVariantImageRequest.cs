namespace GaoApp.Web.Areas.Admin.ViewModels.Products
{
    public sealed class SetVariantImageRequest
    {
        public int VariantId { get; set; }
        public int? PrimaryProductImageId { get; set; }
    }
}
