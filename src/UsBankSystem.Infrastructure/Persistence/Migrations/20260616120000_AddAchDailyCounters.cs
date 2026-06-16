using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UsBankSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAchDailyCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE ""AchDailyCounters"" (
                    ""Date""  date    NOT NULL,
                    ""Value"" integer NOT NULL DEFAULT 0,
                    CONSTRAINT ""PK_AchDailyCounters"" PRIMARY KEY (""Date"")
                )");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AchDailyCounters""");
        }
    }
}
