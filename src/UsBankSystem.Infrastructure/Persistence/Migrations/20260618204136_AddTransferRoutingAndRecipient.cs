using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferRoutingAndRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "Transfers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToRoutingNumber",
                table: "Transfers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ToRoutingNumber",
                table: "Transfers");
        }
    }
}
