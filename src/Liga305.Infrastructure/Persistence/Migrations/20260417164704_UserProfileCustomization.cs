using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liga305.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserProfileCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasCustomProfile",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasCustomProfile",
                table: "Users");
        }
    }
}
