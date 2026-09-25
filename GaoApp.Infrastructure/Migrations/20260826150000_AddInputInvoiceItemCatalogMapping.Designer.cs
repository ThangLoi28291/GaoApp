using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public sealed partial class AddInputInvoiceItemCatalogMapping
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "8.0.17");
        // Runtime migration behavior is fully defined in Up/Down. The canonical
        // complete target model remains AppDbContextModelSnapshot.
#pragma warning restore 612, 618
    }
}
