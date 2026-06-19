using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionReferenceIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Prevents duplicate credits for the same incoming transfer (e.g. ACH idempotency
            // under race conditions when multiple polling pods process the same file).
            // Partial index — rows with NULL ReferenceId (manual/internal transactions) are excluded.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Transactions_ReferenceId_AccountId""
                ON ""Transactions"" (""ReferenceId"", ""AccountId"")
                WHERE ""ReferenceId"" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Transactions_ReferenceId_AccountId""");
        }
    }
}
