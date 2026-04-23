using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchDetailedStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Backpack0",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Backpack1",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Backpack2",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Denies",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoldPerMin",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeroDamage",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeroHealing",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item0",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item1",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item2",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item3",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item4",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Item5",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemNeutral",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHits",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetWorth",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TowerDamage",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "XpPerMin",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RadiantGoldAdvJson",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RadiantXpAdvJson",
                table: "Matches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Backpack0",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Backpack1",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Backpack2",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Denies",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "GoldPerMin",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HeroDamage",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HeroHealing",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item0",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item1",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item2",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item3",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item4",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Item5",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ItemNeutral",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "LastHits",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "NetWorth",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "TowerDamage",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "XpPerMin",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "RadiantGoldAdvJson",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RadiantXpAdvJson",
                table: "Matches");
        }
    }
}
