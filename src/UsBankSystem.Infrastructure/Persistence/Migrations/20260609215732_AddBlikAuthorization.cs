using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlikAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear seed data from previous schema — rows have no valid UserId
            migrationBuilder.Sql("DELETE FROM \"BlikCodes\";");

            migrationBuilder.DropColumn(
                name: "CodeHash",
                table: "BlikCodes");

            migrationBuilder.DropColumn(
                name: "Used",
                table: "BlikCodes");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BlikCodes",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "BlikCodes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "BlikCodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "BlikAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KlikTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsOnUs = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiryTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "pending"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LocalTransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlikAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlikAuthorizations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlikCodes_UserId",
                table: "BlikCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BlikAuthorizations_KlikTransactionId",
                table: "BlikAuthorizations",
                column: "KlikTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlikAuthorizations_UserId_Status",
                table: "BlikAuthorizations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_BlikCodes_Users_UserId",
                table: "BlikCodes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BlikCodes_Users_UserId",
                table: "BlikCodes");

            migrationBuilder.DropTable(
                name: "BlikAuthorizations");

            migrationBuilder.DropIndex(
                name: "IX_BlikCodes_UserId",
                table: "BlikCodes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BlikCodes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "BlikCodes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "BlikCodes");

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "BlikCodes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Used",
                table: "BlikCodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
