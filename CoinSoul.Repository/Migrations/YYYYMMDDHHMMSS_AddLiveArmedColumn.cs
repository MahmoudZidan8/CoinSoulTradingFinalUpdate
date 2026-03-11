using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoinSoul.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveArmedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ✅ Add LiveArmed column to BotSettings table
            migrationBuilder.AddColumn<bool>(
                name: "LiveArmed",
                table: "BotSettings",
                type: "bit",
                nullable: true,
                defaultValue: null);

            // ✅ Ensure Events table has Data column for JSON metadata
            migrationBuilder.AddColumn<string>(
                name: "Data",
                table: "Events",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiveArmed",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "Data",
                table: "Events");
        }
    }
}