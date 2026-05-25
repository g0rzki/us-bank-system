using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferToAccountNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToAccountNumber",
                table: "Transfers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToAccountNumber",
                table: "Transfers");
        }
    }
}
