using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJuniorAccounts_ParentUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JuniorAccounts_Accounts_ParentAccountId",
                table: "JuniorAccounts");

            migrationBuilder.RenameColumn(
                name: "ParentAccountId",
                table: "JuniorAccounts",
                newName: "ParentUserId");

            migrationBuilder.RenameIndex(
                name: "IX_JuniorAccounts_ParentAccountId",
                table: "JuniorAccounts",
                newName: "IX_JuniorAccounts_ParentUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_JuniorAccounts_Users_ParentUserId",
                table: "JuniorAccounts",
                column: "ParentUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JuniorAccounts_Users_ParentUserId",
                table: "JuniorAccounts");

            migrationBuilder.RenameColumn(
                name: "ParentUserId",
                table: "JuniorAccounts",
                newName: "ParentAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_JuniorAccounts_ParentUserId",
                table: "JuniorAccounts",
                newName: "IX_JuniorAccounts_ParentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_JuniorAccounts_Accounts_ParentAccountId",
                table: "JuniorAccounts",
                column: "ParentAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
