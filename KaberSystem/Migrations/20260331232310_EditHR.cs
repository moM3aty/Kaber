using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KaberSystem.Migrations
{
    /// <inheritdoc />
    public partial class EditHR : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualReturnDate",
                table: "LeaveRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturned",
                table: "LeaveRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualReturnDate",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "IsReturned",
                table: "LeaveRequests");
        }
    }
}
