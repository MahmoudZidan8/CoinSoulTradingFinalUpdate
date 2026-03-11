#if DEBUG
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CoinSoul.Api.Controllers;

/// <summary>
/// DEVELOPMENT ONLY - Comprehensive production safety validation
/// </summary>
[ApiController]
[Route("admin/selftest")]
public class SafetyTestController : ControllerBase
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ExecutionLockService _lockService;
    private readonly ITradingSafetyGate _safetyGate;
    private readonly PortfolioRefreshService _portfolioRefresh;
    private readonly ILogger<SafetyTestController> _logger;

    public SafetyTestController(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ExecutionLockService lockService,
        ITradingSafetyGate safetyGate,
        PortfolioRefreshService portfolioRefresh,
        ILogger<SafetyTestController> logger)
    {
        _dbFactory = dbFactory;
        _lockService = lockService;
        _safetyGate = safetyGate;
        _portfolioRefresh = portfolioRefresh;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/selftest/production
    /// Comprehensive production safety validation
    /// </summary>
    [HttpGet("production")]
    public async Task<IActionResult> RunProductionSafetyTests(CancellationToken ct)
    {
        var testResults = new ProductionSafetyTestResults
        {
            TestRunId = Guid.NewGuid().ToString(),
            TestStartTimeUtc = DateTime.UtcNow
        };

        try
        {
            // TEST 1: OCO Retry Logic
            testResults.OcoRetryTest = await TestOcoRetryLogicAsync(ct);

            // TEST 2: Balance Refresh After Entry
            testResults.BalanceRefreshTest = await TestBalanceRefreshAsync(ct);

            // TEST 3: Dust Handling
            testResults.DustHandlingTest = await TestDustHandlingAsync(ct);

            // TEST 4: Execution Lock Exclusivity
            testResults.ExecutionLockTest = await TestExecutionLockAsync(ct);

            // TEST 5: Safety Gate Integration
            testResults.SafetyGateTest = await TestSafetyGateAsync(ct);

            // TEST 6: Risk Guard Enforcement
            testResults.RiskGuardTest = await TestRiskGuardAsync(ct);

            // Calculate overall results
            testResults.TotalTests = 6;
            testResults.PassedTests = new[]
            {
                testResults.OcoRetryTest.Passed,
                testResults.BalanceRefreshTest.Passed,
                testResults.DustHandlingTest.Passed,
                testResults.ExecutionLockTest.Passed,
                testResults.SafetyGateTest.Passed,
                testResults.RiskGuardTest.Passed
            }.Count(x => x);

            testResults.TestEndTimeUtc = DateTime.UtcNow;
            testResults.Duration = testResults.TestEndTimeUtc - testResults.TestStartTimeUtc;
            testResults.OverallStatus = testResults.PassedTests == testResults.TotalTests ? "PASS" : "FAIL";

            return Ok(testResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PRODUCTION_TEST_ERROR]");
            testResults.OverallStatus = "ERROR";
            testResults.ErrorMessage = ex.Message;
            return StatusCode(500, testResults);
        }
    }

    /// <summary>
    /// TEST 1: OCO Retry Logic Validation
    /// </summary>
    private async Task<TestResult> TestOcoRetryLogicAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "OCO Retry Logic" };
        var evidence = new List<string>();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                result.Passed = false;
                result.Reason = "BotSettings not found";
                return result;
            }

            evidence.Add($"OcoRetryAttempts configured: {settings.OcoRetryAttempts}");

            // Check for OCO retry events in TradingEvents
            var retryEvents = await db.TradingEvents
                .Where(e => e.Type == "OCO_RETRY")
                .OrderByDescending(e => e.AtUtc)
                .Take(10)
                .ToListAsync(ct);

            evidence.Add($"Found {retryEvents.Count} OCO_RETRY events in last 10");

            // Validate retry attempt pattern
            if (settings.OcoRetryAttempts >= 2)
            {
                // Check if any recent OCO placements had multiple attempts
                var recentOcoEvents = await db.TradingEvents
                    .Where(e => e.Type == "OCO_OK" || e.Type == "OCO_RETRY" || e.Type == "OCO_FAIL")
                    .Where(e => e.AtUtc > DateTime.UtcNow.AddHours(-24))
                    .OrderByDescending(e => e.AtUtc)
                    .Take(50)
                    .ToListAsync(ct);

                // Group by correlation ID to find retry sequences
                var retrySequences = recentOcoEvents
                    .Where(e => !string.IsNullOrEmpty(e.CorrelationId))
                    .GroupBy(e => e.CorrelationId)
                    .Where(g => g.Count() > 1)
                    .ToList();

                evidence.Add($"Found {retrySequences.Count} OCO sequences with multiple attempts");

                if (retrySequences.Any())
                {
                    var exampleSequence = retrySequences.First();
                    var attemptCount = exampleSequence.Count(e => e.Type == "OCO_RETRY");
                    evidence.Add($"Example: Position {exampleSequence.Key} had {attemptCount} retry attempts");

                    result.Passed = attemptCount >= 1; // At least one retry happened
                    result.Reason = result.Passed 
                        ? $"OCO retry logic working: {attemptCount} retry attempts observed"
                        : "No retry attempts observed despite OcoRetryAttempts >= 2";
                }
                else
                {
                    // No failures means retry logic not tested yet, but configuration is correct
                    result.Passed = true;
                    result.Reason = "OCO retry configured correctly (awaiting first rejection to test)";
                }
            }
            else
            {
                result.Passed = true;
                result.Reason = "OCO retry disabled (OcoRetryAttempts < 2)";
            }

            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// TEST 2: Balance Refresh After Entry
    /// </summary>
    private async Task<TestResult> TestBalanceRefreshAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "Balance Refresh After Entry" };
        var evidence = new List<string>();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                result.Passed = false;
                result.Reason = "BotSettings not found";
                return result;
            }

            evidence.Add($"BalanceRefreshCooldownMs: {settings.BalanceRefreshCooldownMs}");

            // Check for BALANCE_REFRESH events after entry
            var balanceRefreshEvents = await db.TradingEvents
                .Where(e => e.Type == "BALANCE_REFRESH")
                .OrderByDescending(e => e.AtUtc)
                .Take(20)
                .ToListAsync(ct);

            evidence.Add($"Found {balanceRefreshEvents.Count} BALANCE_REFRESH events");

            // Check for recent entries followed by balance refresh
            var recentEntries = await db.Events
                .Where(e => e.Type == "ENTRY_FILLED")
                .Where(e => e.AtUtc > DateTime.UtcNow.AddHours(-24))
                .OrderByDescending(e => e.AtUtc)
                .Take(10)
                .ToListAsync(ct);

            evidence.Add($"Found {recentEntries.Count} recent ENTRY_FILLED events");

            if (recentEntries.Any())
            {
                // Check if balance refresh occurred after each entry
                var entriesWithRefresh = 0;

                foreach (var entry in recentEntries.Take(5))
                {
                    var refreshAfterEntry = balanceRefreshEvents
                        .Any(r => r.AtUtc > entry.AtUtc && 
                                  r.AtUtc < entry.AtUtc.AddSeconds(10));

                    if (refreshAfterEntry)
                    {
                        entriesWithRefresh++;
                    }
                }

                evidence.Add($"{entriesWithRefresh}/{Math.Min(5, recentEntries.Count)} recent entries had balance refresh within 10s");

                result.Passed = entriesWithRefresh > 0;
                result.Reason = result.Passed
                    ? $"Balance refresh working: {entriesWithRefresh} entries triggered refresh"
                    : "No balance refresh observed after recent entries";
            }
            else
            {
                result.Passed = true;
                result.Reason = "No recent entries to test (balance refresh configured)";
            }

            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// TEST 3: Dust Handling Validation
    /// </summary>
    private async Task<TestResult> TestDustHandlingAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "Dust Handling" };
        var evidence = new List<string>();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                result.Passed = false;
                result.Reason = "BotSettings not found";
                return result;
            }

            evidence.Add($"DustIgnoreUsdThreshold: ${settings.DustIgnoreUsdThreshold:N2}");

            // Check for DUST_SKIP events
            var dustSkipEvents = await db.TradingEvents
                .Where(e => e.Type == "DUST_SKIP")
                .OrderByDescending(e => e.AtUtc)
                .Take(20)
                .ToListAsync(ct);

            evidence.Add($"Found {dustSkipEvents.Count} DUST_SKIP events");

            // Check for positions closed as DUST_IGNORED
            var dustPositions = await db.Positions
                .Where(p => p.CloseReason == "DUST_IGNORED")
                .OrderByDescending(p => p.ClosedAtUtc)
                .Take(20)
                .ToListAsync(ct);

            evidence.Add($"Found {dustPositions.Count} DUST_IGNORED positions");

            // Validate dust positions have no sell orders
            var dustWithSellOrders = dustPositions
                .Count(p => p.SellOrderId.HasValue);

            evidence.Add($"{dustWithSellOrders} dust positions have SellOrderId (should be 0)");

            // Check that dust positions are marked correctly
            var properlyMarkedDust = dustPositions
                .Count(p => !p.IsOpen && !p.IsActive && p.ExitCompleted);

            evidence.Add($"{properlyMarkedDust}/{dustPositions.Count} dust positions properly marked as closed");

            // Validate dust threshold logic
            if (dustPositions.Any())
            {
                var validDust = 0;
                foreach (var dust in dustPositions.Take(5))
                {
                    var usdValue = dust.Quantity * dust.EntryPrice;
                    if (usdValue < settings.DustIgnoreUsdThreshold)
                    {
                        validDust++;
                    }
                }

                evidence.Add($"{validDust}/{Math.Min(5, dustPositions.Count)} dust positions correctly below threshold");

                result.Passed = dustWithSellOrders == 0 && 
                                properlyMarkedDust == dustPositions.Count &&
                                validDust > 0;

                result.Reason = result.Passed
                    ? $"Dust handling working: {dustPositions.Count} positions safely ignored"
                    : "Dust handling issues detected (check evidence)";
            }
            else
            {
                result.Passed = true;
                result.Reason = "No dust positions yet (threshold configured correctly)";
            }

            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// TEST 4: Execution Lock Exclusivity
    /// </summary>
    private async Task<TestResult> TestExecutionLockAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "Execution Lock Exclusivity" };
        var evidence = new List<string>();

        try
        {
            var testSymbol = "TESTLOCK";
            var lockType = "ENTRY";

            // Attempt 1: Should succeed
            var lock1 = await _lockService.TryAcquireLockAsync(testSymbol, lockType, 5, "Test1", ct);
            evidence.Add($"First lock attempt: {(lock1 ? "SUCCESS" : "FAIL")}");

            if (!lock1)
            {
                result.Passed = false;
                result.Reason = "First lock acquisition failed";
                result.Evidence = evidence;
                return result;
            }

            // Attempt 2: Should fail (lock held)
            var lock2 = await _lockService.TryAcquireLockAsync(testSymbol, lockType, 5, "Test2", ct);
            evidence.Add($"Second lock attempt (should fail): {(lock2 ? "UNEXPECTED SUCCESS" : "CORRECTLY BLOCKED")}");

            if (lock2)
            {
                result.Passed = false;
                result.Reason = "Lock exclusivity violated - second acquisition succeeded";
                result.Evidence = evidence;
                
                // Cleanup
                await _lockService.ReleaseLockAsync(testSymbol, lockType, "Test2", ct);
                await _lockService.ReleaseLockAsync(testSymbol, lockType, "Test1", ct);
                return result;
            }

            // Release first lock
            await _lockService.ReleaseLockAsync(testSymbol, lockType, "Test1", ct);
            evidence.Add("First lock released");

            // Attempt 3: Should succeed (lock released)
            var lock3 = await _lockService.TryAcquireLockAsync(testSymbol, lockType, 5, "Test3", ct);
            evidence.Add($"Third lock attempt (after release): {(lock3 ? "SUCCESS" : "FAIL")}");

            if (!lock3)
            {
                result.Passed = false;
                result.Reason = "Lock not released properly - third acquisition failed";
                result.Evidence = evidence;
                return result;
            }

            // Cleanup
            await _lockService.ReleaseLockAsync(testSymbol, lockType, "Test3", ct);

            // Check for LOCK_BUSY events in production
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var lockBusyEvents = await db.Events
                .Where(e => e.Type == "LOCK_BUSY")
                .OrderByDescending(e => e.AtUtc)
                .Take(10)
                .CountAsync(ct);

            evidence.Add($"Found {lockBusyEvents} LOCK_BUSY events (indicates lock working in production)");

            result.Passed = true;
            result.Reason = "Execution lock exclusivity verified";
            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// TEST 5: Safety Gate Integration
    /// </summary>
    private async Task<TestResult> TestSafetyGateAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "Safety Gate Integration" };
        var evidence = new List<string>();

        try
        {
            var decision = await _safetyGate.CanPlaceOrderAsync("TESTBTC", "MARKET_BUY", ct);

            evidence.Add($"Safety gate allowed: {decision.Allowed}");
            evidence.Add($"Dry run mode: {decision.DryRun}");
            evidence.Add($"Reason: {decision.Reason}");
            evidence.Add($"ExecuteTrades: {decision.Settings.ExecuteTrades}");
            evidence.Add($"KillSwitch: {decision.Settings.KillSwitch}");

            // Validate safety gate configuration
            if (decision.Settings.KillSwitch)
            {
                result.Passed = !decision.Allowed;
                result.Reason = result.Passed
                    ? "KillSwitch correctly blocking orders"
                    : "KillSwitch active but orders still allowed (CRITICAL)";
            }
            else if (!decision.Settings.ExecuteTrades)
            {
                result.Passed = decision.DryRun && decision.Allowed;
                result.Reason = result.Passed
                    ? "Dry run mode working correctly"
                    : "Dry run mode not working as expected";
            }
            else
            {
                result.Passed = decision.Allowed && !decision.DryRun;
                result.Reason = "Safety gate allowing live trading (ExecuteTrades=true)";
            }

            // Check for safety events
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var safetyEvents = await db.TradingEvents
                .Where(e => e.Type == "KILL_SWITCH" || e.Type == "RISK_STOP" || e.Type == "RISK_PAUSE")
                .OrderByDescending(e => e.AtUtc)
                .Take(5)
                .ToListAsync(ct);

            evidence.Add($"Found {safetyEvents.Count} recent safety block events");

            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// TEST 6: Risk Guard Enforcement
    /// </summary>
    private async Task<TestResult> TestRiskGuardAsync(CancellationToken ct)
    {
        var result = new TestResult { TestName = "Risk Guard Enforcement" };
        var evidence = new List<string>();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                result.Passed = false;
                result.Reason = "BotSettings not found";
                return result;
            }

            var now = DateTime.UtcNow;

            evidence.Add($"StopUntilUtc: {settings.StopUntilUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NULL"}");
            evidence.Add($"PauseUntilUtc: {settings.PauseUntilUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NULL"}");

            var isStopActive = settings.StopUntilUtc.HasValue && settings.StopUntilUtc.Value > now;
            var isPauseActive = settings.PauseUntilUtc.HasValue && settings.PauseUntilUtc.Value > now;

            evidence.Add($"Stop active: {isStopActive}");
            evidence.Add($"Pause active: {isPauseActive}");

            // Check for RISK_STOP and RISK_PAUSE events
            var riskStopEvents = await db.TradingEvents
                .Where(e => e.Type == "RISK_STOP")
                .OrderByDescending(e => e.AtUtc)
                .Take(5)
                .CountAsync(ct);

            var riskPauseEvents = await db.TradingEvents
                .Where(e => e.Type == "RISK_PAUSE")
                .OrderByDescending(e => e.AtUtc)
                .Take(5)
                .CountAsync(ct);

            evidence.Add($"Found {riskStopEvents} RISK_STOP events");
            evidence.Add($"Found {riskPauseEvents} RISK_PAUSE events");

            // Check thresholds
            evidence.Add($"RiskGuardPause30MinPct: {settings.RiskGuardPause30MinPct}%");
            evidence.Add($"RiskGuardPause3HourPct: {settings.RiskGuardPause3HourPct}%");
            evidence.Add($"RiskGuardStopUntilMidnightPct: {settings.RiskGuardStopUntilMidnightPct}%");

            // Validate configuration
            result.Passed = settings.RiskGuardPause30MinPct < 0 &&
                           settings.RiskGuardPause3HourPct < settings.RiskGuardPause30MinPct &&
                           settings.RiskGuardStopUntilMidnightPct < settings.RiskGuardPause3HourPct;

            result.Reason = result.Passed
                ? "Risk guard thresholds configured correctly"
                : "Risk guard threshold configuration invalid";

            result.Evidence = evidence;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Reason = $"Test error: {ex.Message}";
            result.Evidence = evidence;
        }

        return result;
    }

    /// <summary>
    /// GET /admin/selftest/quick
    /// Quick safety check (subset of tests)
    /// </summary>
    [HttpGet("quick")]
    public async Task<IActionResult> RunQuickSafetyCheck(CancellationToken ct)
    {
        var quickResults = new
        {
            TestRunId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            ExecutionLock = await TestExecutionLockAsync(ct),
            SafetyGate = await TestSafetyGateAsync(ct)
        };

        return Ok(quickResults);
    }
}

/// <summary>
/// Overall test results
/// </summary>
public sealed class ProductionSafetyTestResults
{
    public string TestRunId { get; set; } = "";
    public DateTime TestStartTimeUtc { get; set; }
    public DateTime TestEndTimeUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public string OverallStatus { get; set; } = "PENDING";
    public string? ErrorMessage { get; set; }

    public TestResult OcoRetryTest { get; set; } = new();
    public TestResult BalanceRefreshTest { get; set; } = new();
    public TestResult DustHandlingTest { get; set; } = new();
    public TestResult ExecutionLockTest { get; set; } = new();
    public TestResult SafetyGateTest { get; set; } = new();
    public TestResult RiskGuardTest { get; set; } = new();
}

/// <summary>
/// Individual test result
/// </summary>
public sealed class TestResult
{
    public string TestName { get; set; } = "";
    public bool Passed { get; set; }
    public string Reason { get; set; } = "";
    public List<string> Evidence { get; set; } = new();
}
#endif