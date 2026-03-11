using CoinSoul.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Repository.DbContext;

public sealed class CoinSoulDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public CoinSoulDbContext(DbContextOptions<CoinSoulDbContext> options)
        : base(options)
    {
    }

    public DbSet<BotSettingsEntity> BotSettings => Set<BotSettingsEntity>();
    public DbSet<PositionEntity> Positions => Set<PositionEntity>();
    public DbSet<TradeEntity> Trades => Set<TradeEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>(); // ✅ FIX: Use property expression body
    public DbSet<ExecutionLockEntity> ExecutionLocks => Set<ExecutionLockEntity>();
    public DbSet<ExecutionAttemptEntity> ExecutionAttempts => Set<ExecutionAttemptEntity>();
    public DbSet<EquitySnapshotEntity> EquitySnapshotEntity => Set<EquitySnapshotEntity>();
    public DbSet<TradeCooldownEntity> TradeCooldowns => Set<TradeCooldownEntity>();
    public DbSet<TradingEventEntity> TradingEvents => Set<TradingEventEntity>();
    public DbSet<AccountTradeEntity> AccountTrades => Set<AccountTradeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ✅ PART 1: GLOBAL DECIMAL PRECISION - Production Grade Safety
        foreach (var property in modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(8);
        }

        // ================= BotSettings =================
        modelBuilder.Entity<BotSettingsEntity>(e =>
        {
            e.Property(x => x.Min24hQuoteVolumeUsdt).HasPrecision(18, 2);
            e.Property(x => x.EquityStartOfDayUsdt).HasPrecision(18, 2);
            e.Property(x => x.TradeSizeUsd).HasPrecision(18, 2);
            e.Property(x => x.CapitalPerTradeUsdt).HasPrecision(18, 2);
            e.Property(x => x.MinFreeBalanceUsdt).HasPrecision(18, 2);
            e.Property(x => x.MinUsdtToOpenNewPosition).HasPrecision(18, 2);
            e.Property(x => x.NetProfitTargetUsd).HasPrecision(18, 4);
            e.Property(x => x.SlippageBufferUsd).HasPrecision(18, 4);
            e.Property(x => x.TargetUsdPerTrade).HasPrecision(18, 2);
            e.Property(x => x.MinUsdPerTrade).HasPrecision(18, 2);
            e.Property(x => x.MinFreeUsdtReserve).HasPrecision(18, 2);
            e.Property(x => x.MinVolume24hUsd).HasPrecision(18, 2);
        });

        // ================= Positions =================
        modelBuilder.Entity<PositionEntity>(e =>
        {
            e.Property(x => x.QuoteUsedUsdt).HasPrecision(18, 4);
            e.Property(x => x.FeesUsdt).HasPrecision(18, 4);
            e.Property(x => x.NetPnlUsdt).HasPrecision(18, 4);
            e.Property(x => x.TargetNetProfitUsd).HasPrecision(18, 4);
            e.Property(x => x.FeesPaidUsd).HasPrecision(18, 4);
            e.Property(x => x.NetProfitUsd).HasPrecision(18, 4);

            e.HasIndex(x => x.IsOpen);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.Symbol);
        });

        // ================= Trades =================
        modelBuilder.Entity<TradeEntity>(e =>
        {
            e.Property(x => x.QuoteQty).HasPrecision(18, 4);
            e.Property(x => x.FeeUsdt).HasPrecision(18, 4);

            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.Symbol);
        });

        // ================= Orders =================
        modelBuilder.Entity<OrderEntity>(e =>
        {
            e.HasIndex(x => x.Symbol);
            e.HasIndex(x => x.BinanceOrderId);
        });

        // ================= Events ✅ FIX: Fully qualified to avoid ambiguity =================
        modelBuilder.Entity<CoinSoul.Entities.EventEntity>(e =>
        {
            // ✅ Explicitly specify table name to avoid confusion
            e.ToTable("Events");
            
            // ✅ Ensure proper column types
            e.Property(x => x.Type).IsRequired().HasMaxLength(100);
            e.Property(x => x.Level).IsRequired().HasMaxLength(50);
            e.Property(x => x.Message).IsRequired().HasMaxLength(2000);
            e.Property(x => x.Symbol).HasMaxLength(50);
            e.Property(x => x.Data).HasColumnType("nvarchar(max)"); // JSON storage

            // ✅ Indexes
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.Type);
            e.HasIndex(new[] { nameof(EventEntity.Symbol), nameof(EventEntity.AtUtc) });
        });

        // ================= ExecutionLocks =================
        modelBuilder.Entity<ExecutionLockEntity>(e =>
        {
            e.HasIndex(x => x.Symbol);
            e.HasIndex(x => x.ExpiresAtUtc);
            e.HasIndex(new[] { nameof(ExecutionLockEntity.Symbol), nameof(ExecutionLockEntity.LockType) });
        });

        // ================= ExecutionAttempts =================
        modelBuilder.Entity<ExecutionAttemptEntity>(e =>
        {
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.Symbol);
            e.HasIndex(x => x.AttemptedAtUtc);
        });

        // ================= EquitySnapshots =================
        modelBuilder.Entity<EquitySnapshotEntity>(e =>
        {
            e.ToTable("EquitySnapshots");
            e.Property(x => x.TotalEquityUsdt).HasPrecision(18, 2);
            e.Property(x => x.FreeUsdt).HasPrecision(18, 2);
            e.Property(x => x.LockedUsdt).HasPrecision(18, 2);
            e.Property(x => x.StartOfDayEquityUsdt).HasPrecision(18, 2);

            e.HasIndex(x => x.DayUtc);
            e.HasIndex(x => x.AtUtc);
        });

        // ================= TradeCooldowns =================
        modelBuilder.Entity<TradeCooldownEntity>(e =>
        {
            e.ToTable("TradeCooldowns");
            e.HasIndex(x => x.Symbol);
            e.HasIndex(x => x.CooldownUntilUtc);
            e.HasIndex(x => x.LastEntryUtc);
        });

        // ================= TradingEvents =================
        modelBuilder.Entity<TradingEventEntity>(e =>
        {
            e.ToTable("TradingEvents");
            e.Property(x => x.Price).HasPrecision(18, 8);
            e.Property(x => x.Quantity).HasPrecision(18, 8);
            e.Property(x => x.FeeUsdt).HasPrecision(18, 4);
            e.Property(x => x.RealizedPnlUsdt).HasPrecision(18, 4);

            e.HasIndex(x => x.AtUtc);
            e.HasIndex(new[] { nameof(TradingEventEntity.Type), nameof(TradingEventEntity.AtUtc) });
            e.HasIndex(new[] { nameof(TradingEventEntity.Symbol), nameof(TradingEventEntity.AtUtc) });
            e.HasIndex(x => x.CorrelationId);
        });

        // ================= AccountTrades =================
        modelBuilder.Entity<AccountTradeEntity>(e =>
        {
            e.ToTable("AccountTrades");
            
            e.HasKey(x => x.TradeId);
            e.Property(x => x.TradeId).ValueGeneratedNever();

            e.Property(x => x.QuoteQty).HasPrecision(18, 4);
            e.Property(x => x.Commission).HasPrecision(18, 8);

            e.HasIndex(x => x.TradeId).IsUnique();
            e.HasIndex(x => x.TradeTimeUtc);
            e.HasIndex(new[] { nameof(AccountTradeEntity.Symbol), nameof(AccountTradeEntity.TradeTimeUtc) });
            e.HasIndex(x => x.Source);
        });
    }
}
