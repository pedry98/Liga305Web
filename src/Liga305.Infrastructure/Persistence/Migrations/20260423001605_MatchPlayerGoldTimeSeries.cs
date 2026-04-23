using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchPlayerGoldTimeSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoldTJson",
                table: "MatchPlayers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoldTJson",
                table: "MatchPlayers");
        }
    }
}
