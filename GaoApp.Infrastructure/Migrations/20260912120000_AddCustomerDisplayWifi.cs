using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AddCustomerDisplayWifi : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "GuestWifiName", table: "Stores", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "GuestWifiPassword", table: "Stores", type: "nvarchar(128)", maxLength: 128, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GuestWifiName", table: "Stores");
        migrationBuilder.DropColumn(name: "GuestWifiPassword", table: "Stores");
    }
}
