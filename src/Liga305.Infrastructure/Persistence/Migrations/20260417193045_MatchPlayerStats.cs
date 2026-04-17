using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchPlayerStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Assists",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Deaths",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeroId",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kills",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Assists",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Deaths",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HeroId",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Kills",
                table: "MatchPlayers");
        }
    }
}
