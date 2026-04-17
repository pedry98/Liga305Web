using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartingMmr = table.Column<double>(type: "double precision", nullable: false),
                    StartingRd = table.Column<double>(type: "double precision", nullable: false),
                    StartingVolatility = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    DotaMatchId = table.Column<long>(type: "bigint", nullable: true),
                    LobbyName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LobbyPassword = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BotSteamName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSec = table.Column<int>(type: "integer", nullable: true),
                    RadiantWin = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeasonPlayers",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mmr = table.Column<double>(type: "double precision", nullable: false),
                    Rd = table.Column<double>(type: "double precision", nullable: false),
                    Volatility = table.Column<double>(type: "double precision", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    Abandons = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPlayers", x => new { x.SeasonId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SeasonPlayers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeasonPlayers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchPlayers",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Team = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MmrBefore = table.Column<double>(type: "double precision", nullable: false),
                    RdBefore = table.Column<double>(type: "double precision", nullable: false),
                    MmrAfter = table.Column<double>(type: "double precision", nullable: true),
                    RdAfter = table.Column<double>(type: "double precision", nullable: true),
                    JoinedLobby = table.Column<bool>(type: "boolean", nullable: false),
                    Abandoned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlayers", x => new { x.MatchId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MatchPlayers_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPlayers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MmrHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MmrBefore = table.Column<double>(type: "double precision", nullable: false),
                    MmrAfter = table.Column<double>(type: "double precision", nullable: false),
                    Delta = table.Column<double>(type: "double precision", nullable: false),
                    Won = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MmrHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MmrHistory_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MmrHistory_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MmrHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueEntries_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QueueEntries_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueueEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_CreatedAt",
                table: "Matches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_DotaMatchId",
                table: "Matches",
                column: "DotaMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_Status",
                table: "Matches",
                columns: new[] { "SeasonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayers_UserId",
                table: "MatchPlayers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MmrHistory_MatchId",
                table: "MmrHistory",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MmrHistory_SeasonId",
                table: "MmrHistory",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_MmrHistory_UserId_SeasonId_CreatedAt",
                table: "MmrHistory",
                columns: new[] { "UserId", "SeasonId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_MatchId",
                table: "QueueEntries",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_SeasonId_Status_EnqueuedAt",
                table: "QueueEntries",
                columns: new[] { "SeasonId", "Status", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_SeasonId_UserId_Status",
                table: "QueueEntries",
                columns: new[] { "SeasonId", "UserId", "Status" },
                unique: true,
                filter: "\"Status\" = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_UserId",
                table: "QueueEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPlayers_SeasonId_Mmr",
                table: "SeasonPlayers",
                columns: new[] { "SeasonId", "Mmr" });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPlayers_UserId",
                table: "SeasonPlayers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_IsActive",
                table: "Seasons",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPlayers");

            migrationBuilder.DropTable(
                name: "MmrHistory");

            migrationBuilder.DropTable(
                name: "QueueEntries");

            migrationBuilder.DropTable(
                name: "SeasonPlayers");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
