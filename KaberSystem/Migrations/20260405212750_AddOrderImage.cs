using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaberSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceImagePath",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceImagePath",
                table: "Orders");
        }
    }
}
