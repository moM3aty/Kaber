using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaberSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBlacklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlacklisted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBlacklisted",
                table: "Orders");
        }
    }
}
