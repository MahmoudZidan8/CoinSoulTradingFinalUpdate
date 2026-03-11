using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Application;

public sealed class DbSettingsProvider : ISettingsProvider
{
    private readonly CoinSoulDbContext _db;

    public DbSettingsProvider(CoinSoulDbContext db)
    {
        _db = db;
    }

    public async Task<BotSettingsEntity?> GetSettingsSnapshotAsync(CancellationToken ct)
    {
        return await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
    }
}