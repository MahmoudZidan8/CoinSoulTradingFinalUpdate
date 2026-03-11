using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoinSoul.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RecreateAccountTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountTrades",
                columns: table => new
                {
                    TradeId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    QuoteQty = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CommissionAsset = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsMaker = table.Column<bool>(type: "bit", nullable: false),
                    TradeTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTrades", x => x.TradeId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BotSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StrategyModeValue = table.Column<int>(type: "int", nullable: false),
                    IsRunning = table.Column<bool>(type: "bit", nullable: false),
                    LastStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStopUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TradeSizeUsd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CapitalPerTradeUsdt = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TakeProfitGrossPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    StopLossGrossPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxTradeDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    QtyBufferPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    OcoStopLimitBufferPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    UniqueSymbolOnly = table.Column<bool>(type: "bit", nullable: false),
                    MaxConcurrentPositions = table.Column<int>(type: "int", nullable: false),
                    BlockSameSymbolReentry = table.Column<bool>(type: "bit", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: false),
                    NetProfitTargetUsd = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SlippageBufferPct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpreadBufferPct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AutoScalperEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MaxTradeDurationSeconds = table.Column<int>(type: "int", nullable: false),
                    HardStopLossPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxSpreadPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    Min24hQuoteVolumeUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SlippageBufferUsd = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EquityStartOfDayUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PauseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StopUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RiskGuardPause30MinPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    RiskGuardPause3HourPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    RiskGuardStopUntilMidnightPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxUsdPerTrade = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaperTrading = table.Column<bool>(type: "bit", nullable: false),
                    TickSeconds = table.Column<int>(type: "int", nullable: false),
                    TimeExitMinutes = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MaxEntrySlippagePct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CooldownSameSymbolSeconds = table.Column<int>(type: "int", nullable: false),
                    CooldownAfterLossSeconds = table.Column<int>(type: "int", nullable: false),
                    CooldownAfterEntrySeconds = table.Column<int>(type: "int", nullable: false),
                    MaxEntryAttemptsPerSymbolPer15Min = table.Column<int>(type: "int", nullable: false),
                    CooldownAfterTooManyAttemptsSeconds = table.Column<int>(type: "int", nullable: false),
                    SpikeBlockAtrPct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpikeBlock1mMovePct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpikeCheckLookbackMinutes = table.Column<int>(type: "int", nullable: false),
                    EnableSmartCooldown = table.Column<bool>(type: "bit", nullable: false),
                    EnableSpikeBlock = table.Column<bool>(type: "bit", nullable: false),
                    EnableMarketRegimeFilter = table.Column<bool>(type: "bit", nullable: false),
                    RegimeAnchorSymbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegimeTimeframe = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RegimeLookbackBars = table.Column<int>(type: "int", nullable: false),
                    RegimeFastEmaPeriod = table.Column<int>(type: "int", nullable: false),
                    RegimeSlowEmaPeriod = table.Column<int>(type: "int", nullable: false),
                    RegimeAtrPeriod = table.Column<int>(type: "int", nullable: false),
                    SidewaysAtrPctThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TrendAtrPctThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BlockTradingOnCrash = table.Column<bool>(type: "bit", nullable: false),
                    Crash1hMovePct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CrashLookbackMinutes = table.Column<int>(type: "int", nullable: false),
                    RiskMultBull = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RiskMultBear = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RiskMultSideways = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RiskMultCrash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TpMultBull = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TpMultBear = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TpMultSideways = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TpMultCrash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ForceConservativeInBear = table.Column<bool>(type: "bit", nullable: false),
                    MinFreeBalanceUsdt = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinUsdtToOpenNewPosition = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllowMultipleSymbols = table.Column<bool>(type: "bit", nullable: false),
                    IncludeFeesInTP = table.Column<bool>(type: "bit", nullable: false),
                    UseOcoExit = table.Column<bool>(type: "bit", nullable: false),
                    PlaceSeparateTpSlIfOcoFails = table.Column<bool>(type: "bit", nullable: false),
                    EntryCooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    SmartCooldownMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxReentriesPerSymbolPerHour = table.Column<int>(type: "int", nullable: false),
                    BlockRevengeTradingMinutes = table.Column<int>(type: "int", nullable: false),
                    RegimeTimeframeMinutes = table.Column<int>(type: "int", nullable: false),
                    BtcEmaPeriod = table.Column<int>(type: "int", nullable: false),
                    HighVolAtrPctThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RegimeRiskScale = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RegimeTpScale = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RegimeAtrLookback = table.Column<int>(type: "int", nullable: false),
                    RsiMaxForEntry = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MomentumMinPct = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RejectShortTermPeak = table.Column<bool>(type: "bit", nullable: false),
                    MinVolume24hUsd = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TradingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TradingStartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    TradingEndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinFreeUsdtReserve = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UseLimitMakerEntry = table.Column<bool>(type: "bit", nullable: false),
                    LimitMakerDiscountBps = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LimitMakerTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    FallbackToMarketOnEntryTimeout = table.Column<bool>(type: "bit", nullable: false),
                    OcoRetryAttempts = table.Column<int>(type: "int", nullable: false),
                    TargetUsdPerTrade = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinUsdPerTrade = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxOpenTrades = table.Column<int>(type: "int", nullable: false),
                    PreventSameSymbolTwice = table.Column<bool>(type: "bit", nullable: false),
                    TradeHistoryTopSymbols = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EquitySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalEquityUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreeUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LockedUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartOfDayEquityUsdt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TopHoldings = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsStartOfDay = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AttemptType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LockType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BinanceOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    QuoteUsedUsdt = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TargetNetProfitUsd = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeesPaidUsd = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetProfitUsd = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FeesUsdt = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetPnlUsdt = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExitRequested = table.Column<bool>(type: "bit", nullable: false),
                    ExitCompleted = table.Column<bool>(type: "bit", nullable: false),
                    ExitAttempts = table.Column<int>(type: "int", nullable: false),
                    LastExitAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitReasonValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyOrderId = table.Column<long>(type: "bigint", nullable: true),
                    OcoOrderId = table.Column<long>(type: "bigint", nullable: true),
                    SellOrderId = table.Column<long>(type: "bigint", nullable: true),
                    TakeProfitOrderId = table.Column<long>(type: "bigint", nullable: true),
                    StopLossOrderId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeCooldowns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptsInWindow = table.Column<int>(type: "int", nullable: false),
                    CooldownUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastEntryUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastLossUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRejectionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeCooldowns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    QuoteQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FeeUsdt = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradingEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FeeUsdt = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RealizedPnlUsdt = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BotInstanceId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTrades_Source",
                table: "AccountTrades",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTrades_Symbol_TradeTimeUtc",
                table: "AccountTrades",
                columns: new[] { "Symbol", "TradeTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTrades_TradeId",
                table: "AccountTrades",
                column: "TradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTrades_TradeTimeUtc",
                table: "AccountTrades",
                column: "TradeTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySnapshots_AtUtc",
                table: "EquitySnapshots",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySnapshots_DayUtc",
                table: "EquitySnapshots",
                column: "DayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionAttempts_AttemptedAtUtc",
                table: "ExecutionAttempts",
                column: "AttemptedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionAttempts_IdempotencyKey",
                table: "ExecutionAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionAttempts_Symbol",
                table: "ExecutionAttempts",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLocks_ExpiresAtUtc",
                table: "ExecutionLocks",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLocks_Symbol",
                table: "ExecutionLocks",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLocks_Symbol_LockType",
                table: "ExecutionLocks",
                columns: new[] { "Symbol", "LockType" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_IsOpen",
                table: "Positions",
                column: "IsOpen");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Symbol",
                table: "Positions",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCooldowns_CooldownUntilUtc",
                table: "TradeCooldowns",
                column: "CooldownUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCooldowns_LastEntryUtc",
                table: "TradeCooldowns",
                column: "LastEntryUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCooldowns_Symbol",
                table: "TradeCooldowns",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_AtUtc",
                table: "Trades",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Symbol",
                table: "Trades",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_TradingEvents_AtUtc",
                table: "TradingEvents",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TradingEvents_CorrelationId",
                table: "TradingEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingEvents_Symbol_AtUtc",
                table: "TradingEvents",
                columns: new[] { "Symbol", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingEvents_Type_AtUtc",
                table: "TradingEvents",
                columns: new[] { "Type", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTrades");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BotSettings");

            migrationBuilder.DropTable(
                name: "EquitySnapshots");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "ExecutionAttempts");

            migrationBuilder.DropTable(
                name: "ExecutionLocks");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "TradeCooldowns");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TradingEvents");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
