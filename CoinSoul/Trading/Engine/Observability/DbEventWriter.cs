using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Observability;

public sealed class DbEventWriter : IEventWriter
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<DbEventWriter> _logger;
    private readonly bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    public DbEventWriter(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<DbEventWriter> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _isEnabled = configuration.GetValue<bool>("Observability:EnableDbEvents", true);

        if (!_isEnabled)
        {
            _logger.LogWarning("[EVENT_WRITER_DISABLED] Database event writing is DISABLED via configuration");
        }
    }

    public async Task<bool> WriteAsync(
        string type,
        string message,
        string level = "INFO",
        string? symbol = null,
        int? positionId = null,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (!_isEnabled)
            return false;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var eventEntity = new EventEntity
            {
                AtUtc = DateTime.UtcNow,
                Type = type,
                Level = level,
                Message = correlationId != null ? $"[{correlationId}] {message}" : message,
                Symbol = symbol,
                PositionId = positionId
            };

            db.Events.Add(eventEntity);
            await db.SaveChangesAsync(ct);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("[EVENT_WRITTEN] {Type} | {Message}", type, message);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT_WRITE_ERROR] Failed to write event {Type}: {Error}",
                type, ex.Message);
            return false;
        }
    }
}