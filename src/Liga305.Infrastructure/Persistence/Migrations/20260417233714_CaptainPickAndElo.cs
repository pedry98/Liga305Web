using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaptainPickAndElo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PickOrder",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DireCaptainUserId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RadiantCaptainUserId",
                table: "Matches",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickOrder",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "DireCaptainUserId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RadiantCaptainUserId",
                table: "Matches");
        }
    }
}
