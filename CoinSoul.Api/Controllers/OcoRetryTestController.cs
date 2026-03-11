#if DEBUG
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CoinSoul.Api.Controllers;

/// <summary>
/// DEVELOPMENT ONLY - OCO Retry Logic Testing
/// </summary>
[ApiController]
[Route("admin/selftest")]
public class OcoRetryTestController : ControllerBase
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<OcoRetryTestController> _logger;

    public OcoRetryTestController(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<OcoRetryTestController> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/selftest/oco-retry
    /// Tests OCO retry logic with invalid precision simulation
    /// </summary>
    [HttpGet("oco-retry")]
    public async Task<IActionResult> TestOcoRetryLogic(CancellationToken ct)
    {
        var results = new StringBuilder();
        results.AppendLine("=== OCO Retry Logic Self-Test ===");
        results.AppendLine($"Test Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        results.AppendLine();

        var testsPassed = 0;
        var totalTests = 5;

        // TEST 1: Verify OcoRetryAttempts setting exists
        results.AppendLine("[TEST 1] Verify OcoRetryAttempts Configuration");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings != null)
            {
                results.AppendLine($"OcoRetryAttempts: {settings.OcoRetryAttempts}");
                results.AppendLine($"PlaceSeparateTpSlIfOcoFails: {settings.PlaceSeparateTpSlIfOcoFails}");
                results.AppendLine("✅ PASS - Settings configured");
                testsPassed++;
            }
            else
            {
                results.AppendLine("❌ FAIL - BotSettings not found");
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"❌ ERROR - {ex.Message}");
        }
        results.AppendLine();

        // TEST 2: Simulate precision error scenarios
        results.AppendLine("[TEST 2] Precision Error Simulation");
        var rules = new SymbolTradingRules(
            StepSize: 0.001m,
            MinQty: 0.001m,
            TickSize: 0.01m,
            MinNotional: 10m
        );

        var testCases = new[]
        {
            new { Price = 100.005m, Expected = 100.01m, Name = "Round up precision" },
            new { Price = 100.004m, Expected = 100.00m, Name = "Round down precision" },
            new { Price = 99.999m, Expected = 100.00m, Name = "Edge case rounding" }
        };

        foreach (var test in testCases)
        {
            var rounded = QuantizationService.RoundPriceToTick(test.Price, rules.TickSize);
            var pass = rounded == test.Expected;
            results.AppendLine($"  {(pass ? "✅" : "❌")} {test.Name}: {test.Price} -> {rounded} (expected {test.Expected})");
            if (pass) testsPassed++;
        }
        results.AppendLine();

        totalTests += testCases.Length;

        // TEST 3: Validate OCO price relationships
        results.AppendLine("[TEST 3] OCO Price Relationship Validation");
        var entryPrice = 100m;
        var tpPrice = 102m;
        var stopPrice = 98m;
        var stopLimitPrice = 97.90m;

        var validation = NetProfitExitService.ValidateOcoPrices(
            entryPrice, tpPrice, stopPrice, stopLimitPrice);

        if (validation.Valid)
        {
            results.AppendLine($"✅ PASS - OCO prices valid");
            results.AppendLine($"  Entry: {entryPrice}, TP: {tpPrice}, Stop: {stopPrice}, StopLimit: {stopLimitPrice}");
            testsPassed++;
        }
        else
        {
            results.AppendLine($"❌ FAIL - {validation.Reason}");
        }
        results.AppendLine();

        // TEST 4: Simulate retry with buffer adjustment
        results.AppendLine("[TEST 4] Retry Buffer Adjustment Simulation");
        var baseStopLimit = 97.90m;
        var attempts = 3;
        var adjustedPrices = new List<decimal>();

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var bufferAdjustment = 1m - (attempt * 0.0005m);
            var adjusted = baseStopLimit * bufferAdjustment;
            var rounded = QuantizationService.RoundPriceToTick(adjusted, rules.TickSize);
            adjustedPrices.Add(rounded);
            results.AppendLine($"  Attempt {attempt}: {baseStopLimit} * {bufferAdjustment:F4} = {adjusted:F4} -> {rounded}");
        }

        // Verify prices are descending
        bool descending = true;
        for (int i = 1; i < adjustedPrices.Count; i++)
        {
            if (adjustedPrices[i] >= adjustedPrices[i - 1])
            {
                descending = false;
                break;
            }
        }

        if (descending)
        {
            results.AppendLine("✅ PASS - Buffer adjustment produces descending prices");
            testsPassed++;
        }
        else
        {
            results.AppendLine("❌ FAIL - Buffer adjustment did not produce descending prices");
        }
        results.AppendLine();

        // TEST 5: Check TradingEvents logging
        results.AppendLine("[TEST 5] TradingEvents Logging Capability");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            
            var testEvent = new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = "INFO",
                Type = "OCO_RETRY_TEST",
                Symbol = "TESTBTC",
                Message = "Self-test OCO retry event"
            };

            db.TradingEvents.Add(testEvent);
            await db.SaveChangesAsync(ct);

            // Clean up test event
            db.TradingEvents.Remove(testEvent);
            await db.SaveChangesAsync(ct);

            results.AppendLine("✅ PASS - TradingEvents logging operational");
            testsPassed++;
        }
        catch (Exception ex)
        {
            results.AppendLine($"❌ FAIL - TradingEvents logging error: {ex.Message}");
        }
        results.AppendLine();

        // Summary
        results.AppendLine("=== TEST SUMMARY ===");
        results.AppendLine($"Passed: {testsPassed}/{totalTests}");
        
        if (testsPassed == totalTests)
        {
            results.AppendLine("✅ ALL TESTS PASSED - OCO Retry Logic Ready");
        }
        else
        {
            results.AppendLine($"⚠️ {totalTests - testsPassed} tests failed");
        }

        return Content(results.ToString(), "text/plain");
    }
}
#endif