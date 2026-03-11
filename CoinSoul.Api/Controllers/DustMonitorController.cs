#if DEBUG
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CoinSoul.Api.Controllers;

/// <summary>
/// DEVELOPMENT ONLY - Dust position monitoring
/// </summary>
[ApiController]
[Route("admin/monitoring")]
public class DustMonitorController : ControllerBase
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<DustMonitorController> _logger;

    public DustMonitorController(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<DustMonitorController> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/monitoring/dust-report
    /// Shows all dust positions and statistics
    /// </summary>
    [HttpGet("dust-report")]
    public async Task<IActionResult> GetDustReport(CancellationToken ct)
    {
        var results = new StringBuilder();
        results.AppendLine("=== CoinSoul Dust Position Report ===");
        results.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        results.AppendLine();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get dust positions
        var dustPositions = await db.Positions
            .Where(p => p.CloseReason == "DUST_IGNORED")
            .OrderByDescending(p => p.ClosedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        results.AppendLine($"[DUST POSITIONS] Found {dustPositions.Count} dust positions");
        results.AppendLine();

        if (dustPositions.Any())
        {
            var totalDustLoss = dustPositions.Sum(p => p.NetPnlUsdt);
            results.AppendLine($"Total Dust Loss: ${totalDustLoss:N4}");
            results.AppendLine();

            results.AppendLine("Recent Dust Positions:");
            results.AppendLine("ID    | Symbol      | Qty           | Entry Price | USD Value | Closed At");
            results.AppendLine("------|-------------|---------------|-------------|-----------|-------------------");

            foreach (var pos in dustPositions.Take(20))
            {
                var usdValue = pos.Quantity * pos.EntryPrice;
                results.AppendLine($"{pos.Id,-5} | {pos.Symbol,-11} | {pos.Quantity,13:0.########} | ${pos.EntryPrice,10:N4} | ${usdValue,8:N4} | {pos.ClosedAtUtc:MM-dd HH:mm}");
            }
        }
        else
        {
            results.AppendLine("No dust positions found.");
        }

        results.AppendLine();

        // Get dust events from TradingEvents
        var dustEvents = await db.TradingEvents
            .Where(e => e.Type == "DUST_SKIP")
            .OrderByDescending(e => e.AtUtc)
            .Take(20)
            .ToListAsync(ct);

        results.AppendLine($"[DUST EVENTS] Found {dustEvents.Count} recent dust events");
        results.AppendLine();

        if (dustEvents.Any())
        {
            results.AppendLine("Recent Dust Events:");
            foreach (var evt in dustEvents)
            {
                results.AppendLine($"  {evt.AtUtc:MM-dd HH:mm:ss} | {evt.Symbol} | {evt.Message}");
            }
        }

        results.AppendLine();

        // Get current settings
        var settings = await db.BotSettings.FirstOrDefaultAsync(ct);
        if (settings != null)
        {
            results.AppendLine("[CURRENT SETTINGS]");
            results.AppendLine($"DustIgnoreUsdThreshold: ${settings.DustIgnoreUsdThreshold:N2}");
            results.AppendLine($"QtyBufferPct: {settings.QtyBufferPct:P2}");
        }

        return Content(results.ToString(), "text/plain");
    }

    /// <summary>
    /// POST /admin/monitoring/test-dust-handling
    /// Simulates dust detection logic
    /// </summary>
    [HttpPost("test-dust-handling")]
    public async Task<IActionResult> TestDustHandling(CancellationToken ct)
    {
        var results = new StringBuilder();
        results.AppendLine("=== Dust Handling Logic Test ===");
        results.AppendLine($"Test Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        results.AppendLine();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

        if (settings == null)
        {
            return BadRequest("BotSettings not found");
        }

        var testScenarios = new[]
        {
            new { Symbol = "BTCUSDT", Qty = 0.00005m, Price = 50000m, ExpectedDust = false, Name = "Normal BTC position" },
            new { Symbol = "ALTUSDT", Qty = 0.5m, Price = 1.5m, ExpectedDust = true, Name = "Low value ALT ($0.75)" },
            new { Symbol = "SHIBUSDT", Qty = 1000m, Price = 0.00001m, ExpectedDust = true, Name = "High qty low price ($0.01)" },
            new { Symbol = "ETHUSDT", Qty = 0.001m, Price = 3000m, ExpectedDust = false, Name = "Small ETH position ($3)" }
        };

        var rules = new SymbolTradingRules(
            StepSize: 0.00001m,
            MinQty: 0.00001m,
            TickSize: 0.01m,
            MinNotional: 10m
        );

        results.AppendLine($"DustIgnoreUsdThreshold: ${settings.DustIgnoreUsdThreshold:N2}");
        results.AppendLine($"MinNotional: ${rules.MinNotional:N2}");
        results.AppendLine();

        var passCount = 0;

        foreach (var scenario in testScenarios)
        {
            var usdValue = scenario.Qty * scenario.Price;
            var isDust = usdValue < settings.DustIgnoreUsdThreshold || 
                         scenario.Qty < rules.MinQty || 
                         usdValue < rules.MinNotional;

            var pass = isDust == scenario.ExpectedDust;
            var status = pass ? "✅" : "❌";

            results.AppendLine($"{status} {scenario.Name}");
            results.AppendLine($"   Symbol: {scenario.Symbol}");
            results.AppendLine($"   Qty: {scenario.Qty:0.########}, Price: ${scenario.Price:N4}");
            results.AppendLine($"   USD Value: ${usdValue:N4}");
            results.AppendLine($"   Detected as Dust: {isDust} (Expected: {scenario.ExpectedDust})");
            results.AppendLine();

            if (pass) passCount++;
        }

        results.AppendLine("=== TEST SUMMARY ===");
        results.AppendLine($"Passed: {passCount}/{testScenarios.Length}");

        if (passCount == testScenarios.Length)
        {
            results.AppendLine("✅ ALL DUST SCENARIOS HANDLED CORRECTLY");
        }
        else
        {
            results.AppendLine($"⚠️ {testScenarios.Length - passCount} scenarios failed");
        }

        return Content(results.ToString(), "text/plain");
    }
}
#endif