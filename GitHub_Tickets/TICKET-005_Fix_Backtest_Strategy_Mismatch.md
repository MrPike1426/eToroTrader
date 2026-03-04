# TICKET-005: Fix Backtest Strategy Mismatch (Backtest Page)

**Status:** Backlog
**Priority:** Medium
**Severity:** High
**Assigned To:** Copilot
**Due Date:** 20/05/2026
**Tokens:** 8
**Labels:** `bug,backtest,rsi-reversal,strategy-validation,backtest-page`

---

## Problem Statement

**Critical Discrepancy Observed:**

RSI Reversal strategy generates:
- ✅ **Backtest:** ~1 trade per hour on MESH26 (very active)
- ❌ **AI Trade (Live):** Zero trades with identical settings

**Example:**
```
Contract: MESH26 (E-Mini S&P 500)
Strategy: RSI Reversal
Settings: RSI period 14, threshold 30/70, 5-minute bars
Backtest Result: 47 trades in 8-hour session (1 trade/10 min avg)
Live Result: 0 trades in 8 hours with EXACT SAME settings
```

**Root Cause Unknown:**
Possible causes (to investigate):
1. Strategy parameters not applied identically in live trading
2. Data feed differences (backtest uses historical data, live uses real-time)
3. Timing issues (backtest evaluates at bar close, live evaluates continuously)
4. RSI calculation differences (different period application)
5. Entry/exit logic difference between engines

**Impact:**
- Backtest results unreliable (can't trust strategy performance)
- Users can't validate strategies before live trading
- Reduces confidence in AI Trade feature

---

## Requirements

### A. Root Cause Investigation

**Phase 1: Reproduce the Bug**

1. **Backtest with RSI Reversal:**
   - Select MESH26 contract
   - Run backtest with RSI Reversal strategy
   - Record: Number of trades, entry times, exit times
   - Export trade list

2. **Live Trade with Same Settings:**
   - Switch to AI Trade tab
   - Select same MESH26 contract
   - Activate RSI Reversal strategy
   - Monitor for 1-2 hours
   - Record: Number of trades (should match backtest pattern)

3. **Compare Signals:**
   ```
   Backtest Signal Time | Backtest Action | Live Signal Time | Live Action
   ───────────────────────────────────────────────────────────────────
   14:35:22            | BUY (RSI=25)    | 14:35:23         | (none)
   14:45:10            | SELL (RSI=75)   | 14:45:11         | (none)
   ...
   ```

**Phase 2: Identify Differences**

1. **Strategy Parameters:**
   - Are RSI period, thresholds identical?
   - Are bar timing and lookback identical?
   - Log both engines' parameter values

2. **RSI Calculation:**
   - Extract RSI values from backtest at specific times
   - Calculate RSI manually on same data
   - Compare live engine RSI vs manual calculation
   - Trace: Which values diverge first?

3. **Data Feed:**
   - Backtest source: Historical data (TopStep historical bars?)
   - Live source: Real-time bars (TopStep API?)
   - Are they the same contract and timeframe?
   - Latency difference?

4. **Execution Flow:**
   - Backtest: Evaluate at bar close
   - Live: Evaluate every 30 seconds (see TICKET-012)
   - Could timing cause different RSI readings?

### B. Documentation

Create investigation report:

```
TICKET-005 Root Cause Analysis Report
═════════════════════════════════════

1. PARAMETERS COMPARISON
   Backtest RSI Period:  14    ✓
   Live RSI Period:      14    ✓

   Backtest Threshold:   30/70 ✓
   Live Threshold:       30/70 ✓

   Backtest Bars:        5-min ✓
   Live Bars:            5-min ✓

2. RSI CALCULATION TEST
   Sample Time: 14:35:22 UTC

   Data: [4530, 4531, 4529, 4532, 4533, ...]
   Manual RSI: 28.3
   Backtest RSI: 28.3 ✓
   Live RSI: 31.4 ✗ DIVERGENCE FOUND

   Root: Live engine uses different lookback period (10 bars instead of 14)

3. CONCLUSION
   ✗ RSI calculation differs in live vs backtest
   → Fix: Align live engine to use same lookback period

4. NEXT STEPS
   → Implement fix in StrategyExecutionEngine.vb
   → Re-test live trading with corrected RSI
```

### C. Fix Implementation

Once root cause identified:

1. **If parameter mismatch:** Align parameters in both engines
2. **If calculation difference:** Fix calculation logic in live engine
3. **If timing issue:** Ensure evaluation happens at same times
4. **If data feed issue:** Use same data source for backtest and live

---

## Implementation Plan

### Phase 1: Investigation & Diagnosis (3 tokens)

**Duration:** 2-3 days

1. **Setup Backtest:**
   - Open Backtest tab
   - Run RSI Reversal on MESH26
   - Export detailed trade list (timestamps, signals, prices)

2. **Setup Live Trade:**
   - Monitor AI Trade for 2-4 hours
   - Log all signals (even rejected ones)
   - Capture bar data and RSI values

3. **Extract & Compare:**
   - Parse backtest output
   - Parse live trade output
   - Find first divergence point
   - Hypothesis: Where do they differ?

4. **Manual RSI Calculation:**
   - Pick sample time period (e.g., 14:30-15:00 UTC)
   - Fetch OHLCV bars from API
   - Calculate RSI manually
   - Compare: Manual vs Backtest vs Live

5. **Trace Execution:**
   - Add logging to both engines
   - Log RSI values before signal check
   - Log signal decision (true/false)
   - Run live, capture logs
   - Compare signal generation

### Phase 2: Root Cause Determination (1 token)

**Duration:** 1 day

1. **Analyze logs** from Phase 1
2. **Identify discrepancy:**
   - RSI calc difference?
   - Parameter difference?
   - Timing difference?
   - Data difference?
3. **Document root cause** in investigation report

### Phase 3: Fix & Re-test (3 tokens)

**Duration:** 2-3 days

1. **Implement fix** based on root cause
2. **Unit test:**
   - Mock RSI data
   - Verify signal generation matches expected
3. **Integration test:**
   - Run backtest with fix
   - Compare to original backtest (should match)
4. **Live test:**
   - Monitor AI Trade again
   - Verify signals now match backtest pattern
   - Trade for 4-8 hours

### Phase 4: Validation & Closure (1 token)

**Duration:** 1 day

1. **Acceptance testing:**
   - Run backtest 3 times (consistent results?)
   - Run live trading 3 times (consistent results?)
   - Do backtest and live results align?
2. **Update TICKET-005 Notes** with root cause and fix
3. **Mark Complete** when divergence resolved

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Likely fix location |
| `src/TopStepTrader.Services/Indicators/RSIIndicator.vb` | Possible fix if RSI calc differs |
| `src/TopStepTrader.Services/BarCollection/BarCollectionService.vb` | Possible fix if data differs |
| `src/TopStepTrader.Services/Backtesting/BacktestEngine.vb` | Diagnostic - compare logic |

---

## Acceptance Criteria

- [ ] Root cause identified and documented
- [ ] RSI Reversal backtest generates signals consistently
- [ ] RSI Reversal live trading generates signals at same times as backtest
- [ ] Backtest and live results align (both generate ~similar trades in same windows)
- [ ] Investigation report created with detailed findings
- [ ] Fix implemented and tested
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: Other strategies still work correctly

---

## Blocking Notes

⏳ **This ticket is blocked by:**
- **TICKET-006:** Backtest Page Rewrite — Investigation easier once backtest page rewritten with detailed logging
- **TICKET-012:** Fix 30-Second Bar Check — Live evaluation timing may affect signals

**Recommendation:**
- Can start Phase 1 investigation now (don't need to wait for TICKET-006)
- Full fix easier after TICKET-006 complete (better visibility into backtest logic)

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Investigate before fixing** | Prevents wrong fix ✅ | Takes time ❌ |
| **Compare with manual calculation** | Proves/disproves engine issue ✅ | Labor-intensive ❌ |
| **Align to backtest (vs rewrite live)** | Backtest more reliable ✅ | Live might be more correct ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Bug is intermittent (hard to reproduce)** | Run tests multiple times, check for timing-dependent behavior |
| **Root cause is complex (combination of factors)** | Investigate systematically (Phase 1: narrow down) |
| **Fix breaks other strategies** | Run regression tests on all 8 strategies post-fix |
| **Data source unreliable** | Compare with broker's charts, validate OHLCV data |

---

## Related Tickets

- **TICKET-006:** Backtest Page Complete Rewrite — Better logging/debugging tools
- **TICKET-012:** Fix 30-Second Bar Check vs 5-Minute Strategy — May be related to timing
- **TICKET-014:** AI Trade Redesign — RSI Reversal strategy part of redesign

---

## Investigation Checklist

Before starting:

- [ ] Access to backtest engine code
- [ ] Access to live AI Trade engine code
- [ ] Ability to run backtest and capture output
- [ ] Ability to run live trades and capture logs
- [ ] Manual RSI calculation tool or spreadsheet
- [ ] Sample OHLCV data for validation
- [ ] Agreement on acceptable divergence (0%, <1%, etc.)

---

## Success Criteria

- ✅ Backtest and live results divergence **< 5%** (acceptable range)
- ✅ Root cause **identified and documented**
- ✅ Fix **implemented and tested**
- ✅ Confidence **restored in backtest → live correlation**

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Copilot
**Token Estimate:** 8 (investigation 3 + determination 1 + fix 3 + validation 1)
**Severity:** High (strategy validation broken)
**Status:** Backlog. Root cause analysis needed when Backtest Page rewrite (TICKET-006) is complete. Check if strategy parameters or data feeds differ between modes.

## Progress Tracking

### Phase 1: Investigation & Diagnosis
- [ ] Backtest run with RSI Reversal on MESH26
- [ ] Live trade monitoring for 4-8 hours
- [ ] Signal comparison (backtest vs live) completed
- [ ] Manual RSI calculation (verification)
- [ ] Logs extracted and analyzed

### Phase 2: Root Cause Determination
- [ ] Logs analyzed for discrepancies
- [ ] First divergence point identified
- [ ] Root cause hypothesis documented
- [ ] Investigation report created

### Phase 3: Fix & Re-test (After Root Cause Found)
- [ ] Fix implemented based on root cause
- [ ] Unit tests written
- [ ] Integration tests (backtest consistency)
- [ ] Live tests (signal generation alignment)

### Phase 4: Validation & Closure
- [ ] Backtest/live results aligned (< 5% variance)
- [ ] Root cause documented in ticket Notes
- [ ] Regression tests on all 8 strategies
- [ ] Ticket marked Complete

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (awaiting TICKET-006)
**Blocker:** Better suited to start after TICKET-006 (Backtest rewrite) complete
**Next Concrete Action:** Set up backtest run with RSI Reversal on MESH26

---

## Next Steps

### Immediate (After Approval):

1. **Setup Backtest Analysis** (today)
   - Open Backtest tab with RSI Reversal on MESH26
   - Run and record results

2. **Setup Live Trade Monitoring** (next session)
   - Monitor AI Trade with same settings
   - Log all signals for 4-8 hours

3. **Initial Comparison** (next day)
   - Plot backtest signals vs live signals
   - Identify first divergence point

### During Investigation:

Week 1: Phase 1 (Investigation) — systematic tracing
Week 2: Phase 2 (Root Cause) — pinpoint issue
Week 2-3: Phase 3 (Fix) — implement and retest
Week 3: Phase 4 (Validation) — confirm fix works

### Post-Fix:

1. **Update documentation** with root cause lesson learned
2. **Share findings** with team (how to prevent similar issues)
3. **Validate** all 8 strategies work correctly post-fix
