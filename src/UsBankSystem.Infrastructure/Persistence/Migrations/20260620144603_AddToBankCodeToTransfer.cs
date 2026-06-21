using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToBankCodeToTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToBankCode",
                table: "Transfers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToBankCode",
                table: "Transfers");
        }
    }
}
