using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaberSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddDamageLoss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalLoss",
                table: "DamagedParts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalLoss",
                table: "DamagedParts");
        }
    }
}
