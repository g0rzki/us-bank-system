using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservedBalanceCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safety net: prevents ReservedBalance from going negative due to double-decrement
            // bugs (e.g. timeout CTE racing with ACK processing). Any violation surfaces
            // immediately as a constraint error rather than silently corrupting balances.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Accounts""
                ADD CONSTRAINT ""CK_Accounts_ReservedBalance_NonNegative""
                CHECK (""ReservedBalance"" >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Accounts""
                DROP CONSTRAINT IF EXISTS ""CK_Accounts_ReservedBalance_NonNegative""");
        }
    }
}
