using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoinSoul.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionSafetyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExecuteTrades",
                table: "BotSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KillSwitch",
                table: "BotSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAllowedEntrySlippagePct",
                table: "BotSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0.20m);

            migrationBuilder.AddColumn<int>(
                name: "ReconcileIntervalSeconds",
                table: "BotSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "BalanceRefreshCooldownMs",
                table: "BotSettings",
                type: "int",
                nullable: false,
                defaultValue: 2000);

            migrationBuilder.AddColumn<decimal>(
                name: "DustIgnoreUsdThreshold",
                table: "BotSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 1.0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecuteTrades",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "KillSwitch",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "MaxAllowedEntrySlippagePct",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "ReconcileIntervalSeconds",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "BalanceRefreshCooldownMs",
                table: "BotSettings");

            migrationBuilder.DropColumn(
                name: "DustIgnoreUsdThreshold",
                table: "BotSettings");
        }
    }
}