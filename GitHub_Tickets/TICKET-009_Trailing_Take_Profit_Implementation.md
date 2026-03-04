# TICKET-009: Trailing Take Profit Implementation

**Status:** For Development
**Priority:** Medium
**Severity:** Medium
**Assigned To:** Copilot
**Due Date:** 10/04/2026
**Tokens:** 16
**Labels:** `feature,ai-trade,trailing-tp,research-required`

---

## Problem Statement

Currently, **take profit (TP) is static** — once set at entry, it never changes.

**Limitation:**
```
User enters LONG trade at 4532.75 with TP at 4540.00
Market rallies to 4545.00 (great opportunity!)
But TP is still at 4540.00 (user only gets $362.50 instead of $620.00)
```

**Ideal Behavior:**
```
User enters LONG trade at 4532.75 with TP at 4540.00
Market rallies to 4545.00
TP automatically adjusts to 4541.00 (follows price up, locks in profits)
If price drops back to 4541.00, trade exits with higher profit
```

**Business Value:**
- **Maximize profits** — Capture upside moves automatically
- **Lock in gains** — Never lose winning trades to reversals
- **Reduce manual management** — Don't need to manually adjust TP
- **Professional feature** — Most advanced traders use trailing TP

**Challenge:**
Research required: Does Claude API integration enhance trailing TP (e.g., predict momentum to adjust aggressively), or use simpler rule-based approach?

---

## Requirements

### A. Trailing TP Algorithm

Implement trailing stop logic:

```
TRAILING TP - LONG TRADES:
  Entry: 4532.75
  Initial TP: 4540.00
  Trailing Distance: 5 points

  Time | Price | Highest | TP Value | Action
  ───────────────────────────────────────────
  0    | 4532  | 4532    | 4540     | (static)
  1    | 4535  | 4535    | 4540     | (waiting for higher)
  2    | 4542  | 4542    | 4537     | TP moves up (4542 - 5 = 4537)
  3    | 4545  | 4545    | 4540     | TP moves up (4545 - 5 = 4540)
  4    | 4543  | 4545    | 4540     | TP stays at 4540 (price below high)
  5    | 4539  | 4545    | 4540     | Exit at 4539 (hit TP)

Key Points:
- TP follows price UP (locks in gains)
- TP never goes DOWN (no risk of closing early)
- Trailing distance = how many points to keep as buffer
```

### B. Configuration UI

Add to AI Trade tab:

```
┌─────────────────────────────────┐
│  Take Profit Settings           │
├─────────────────────────────────┤
│ Mode: [Static ▼] [Trailing ▼]   │
│                                  │
│ Static Mode:                      │
│  TP Price: [4540.00]             │
│                                  │
│ Trailing Mode:                    │
│  Base TP: [4540.00]              │
│  Trailing Distance: [5 points]   │
│  Refresh Interval: [5 sec]       │
│                                  │
│  [Estimate TP Path]              │
└─────────────────────────────────┘
```

### C. Monitoring & Adjustment

Every 5 seconds (configurable):

1. Get latest market price
2. Compare to highest price since entry
3. If (highest - trailing distance) > current TP:
   - Update TP to new value
   - Log adjustment
4. If price hits TP:
   - Exit trade
   - Record exit as "Trailing TP"

### D. Visualization

Show trailing TP path on live chart:

```
Price Chart:
  Entry: 4532.75 ─→ ■ (entry point)
  High: 4545.00  ─→ ▲ (highest price)
  TP Path: ═════════════════════════
            Static: ┌─────────
            Trailing: ┌─────┌────┌───
  Current TP: 4540.00

Shows how TP adjusts as price moves
```

### E. Research: Claude API Enhancement (Optional)

**Question:** Can Claude API improve trailing TP?

**Potential Uses:**
1. **Momentum Detection:**
   - Use Claude to analyze price momentum
   - If momentum strong → set larger trailing distance
   - If momentum weak → set smaller trailing distance

2. **Volatility Adjustment:**
   - Analyze recent volatility
   - High volatility → larger trailing distance (avoid whipsaws)
   - Low volatility → smaller trailing distance (lock in gains faster)

3. **Contextual Rules:**
   - "If price has moved > 50 points, use aggressive trailing (3-point distance)"
   - "If in consolidation, use conservative trailing (10-point distance)"

**Recommendation:** Start with simple rule-based approach. Revisit Claude integration in Phase 2.

---

## Implementation Plan

### Phase 1: Research & Design (2 tokens)

**Duration:** 1 day

1. **Research:**
   - Look at professional trailing TP implementations
   - Validate algorithm logic
   - Research optimal refresh intervals (100ms, 500ms, 5s?)

2. **Design:**
   - Define TrailingTPManager service
   - Design UI configuration
   - Plan monitoring loop

3. **Spike:**
   - Implement simple trailing TP on sample data
   - Validate algorithm correctness
   - Estimate performance impact

### Phase 2: Core Implementation (10 tokens)

**Duration:** 3-4 days

1. **TrailingTPManager.vb:**
   ```vb
   Public Class TrailingTPManager
       Private _baseTP As Decimal
       Private _currentTP As Decimal
       Private _highestPrice As Decimal
       Private _trailingDistance As Decimal

       Public Sub AdjustTP(currentPrice As Decimal)
           ' Update highest price
           If currentPrice > _highestPrice Then
               _highestPrice = currentPrice
           End If

           ' Calculate new TP
           Dim newTP = _highestPrice - _trailingDistance
           If newTP > _currentTP Then
               _currentTP = newTP  ' TP only moves up
           End If
       End Sub

       Public Function ShouldExit(currentPrice As Decimal) As Boolean
           Return currentPrice <= _currentTP
       End Function
   End Class
   ```

2. **AiTradingViewModel Updates:**
   - Add `TakeProfitMode` property (Static vs Trailing)
   - Add `TrailingDistance` property
   - Add `BaseTP` property
   - Integrate with existing TP monitoring

3. **AiTradingView.xaml Updates:**
   - Add TP mode selector (Static/Trailing)
   - Add trailing distance input
   - Show current TP value (updates every 5 sec)

4. **Monitoring Loop:**
   - Every 5 seconds (configurable):
     - Get latest price
     - Call AdjustTP()
     - Check if should exit
     - Log adjustments

5. **Error Handling:**
   - Handle missing price data
   - Validate inputs (distance > 0)
   - Prevent TP from going below entry (should never happen)

### Phase 3: Testing & Validation (3 tokens)

**Duration:** 1-2 days

1. **Unit Tests:**
   - Test TP adjustment logic with sample data
   - Test TP never goes down
   - Test exit condition accuracy

2. **Integration Tests:**
   - Run live trading with trailing TP
   - Monitor for 4-8 hours
   - Compare exit prices with expected values

3. **Validation:**
   - Verify exit at correct TP price (within 1 point)
   - Verify monitoring doesn't miss price updates
   - Check refresh interval (is 5 sec responsive enough?)

### Phase 4: Enhancement (1 token) - OPTIONAL

**Duration:** 1 day (after Phase 1-3 complete)

If Claude API research successful:

1. **ContextualTPAdjuster.vb:**
   - Analyze momentum
   - Adjust trailing distance based on conditions
   - Use Claude for signal processing

2. **Example Logic:**
   ```vb
   Dim momentum = AnalyzeMomentum(priceHistory)
   If momentum > 0.8 Then
       ' Strong uptrend → aggressive trailing
       _trailingDistance = 3
   Else If momentum < 0.2 Then
       ' Weak momentum → conservative trailing
       _trailingDistance = 10
   End If
   ```

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/Trading/TrailingTPManager.vb` | NEW |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | UPDATE - Add trailing TP properties |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | UPDATE - Add TP configuration |
| `src/TopStepTrader.Core/Models/TakeProfitMode.vb` | NEW - Enum (Static/Trailing) |

---

## Acceptance Criteria

- [ ] Trailing TP mode can be selected in UI
- [ ] TP adjusts correctly based on trailing distance
- [ ] TP never goes down (monotonically increasing)
- [ ] Trade exits when price hits trailing TP
- [ ] Exit price matches expected TP value (within $1 tolerance)
- [ ] Monitoring updates every 5 seconds (or configured interval)
- [ ] Logging shows all TP adjustments with timestamps
- [ ] Static mode still works (backward compatible)
- [ ] No performance regression (monitoring doesn't slow down other features)
- [ ] Build succeeds: 0 errors, 0 warnings

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Trailing distance in points (vs %)** | Easy to understand ✅ | Doesn't scale with volatility ❌ |
| **5-second refresh (vs 100ms)** | Lower CPU usage ✅ | Slight lag in fast markets ❌ |
| **Manual distance input (vs adaptive)** | User control ✅ | Requires tuning ❌ |
| **Research Claude integration (vs just rules)** | Explore AI benefit ✅ | Adds complexity ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Algorithm logic error** | Unit test with manual calculations |
| **TP doesn't update in fast market** | Monitor refresh rate, potentially increase to 2-second |
| **Exit at wrong price** | Validate within 1 point tolerance, log all exits |
| **TP goes below entry (shouldn't happen)** | Add guard condition: `newTP = Math.Max(newTP, entryPrice)` |

---

## Related Tickets

- **TICKET-010:** Stop Loss Bug — TP/SL persistence related
- **TICKET-009 (this):** Trailing TP feature
- **TICKET-004:** Position Sizing — TP affects profit calculation

---

## Future Enhancements (Phase 2)

- [ ] Volatile-distance trailing (adjusts based on volatility)
- [ ] Partial profit taking (close 50% at +50 points, 50% at +100 points)
- [ ] Time-based TP (close after N minutes if profitable)
- [ ] Percentage-based trailing (instead of points)
- [ ] Claude API enhancement (context-aware trailing distance)

---

## Success Metrics

- ✅ Trailing TP adjusts within 5 seconds of price moving
- ✅ TP never goes down during trade
- ✅ Exit at correct price (verified with manual checks)
- ✅ Users report "captures more upside than static TP"
- ✅ Zero false exits (edge case handling robust)

---

---

## Progress Tracking

### Phase 1: Research & Design
- [ ] Professional trailing TP implementations researched
- [ ] Algorithm validated with manual examples
- [ ] Refresh interval determined (100ms vs 5 sec)
- [ ] TrailingTPManager service designed

### Phase 2: Core Implementation
- [ ] TrailingTPManager.vb created with adjust logic
- [ ] AiTradingViewModel TP mode properties added
- [ ] AiTradingView.xaml TP configuration UI added
- [ ] Monitoring loop integration completed
- [ ] Error handling for stale data added

### Phase 3: Testing & Validation
- [ ] Unit tests for trailing logic (TP never goes down)
- [ ] Integration tests with live market data
- [ ] Validation (exit price within $1 tolerance)
- [ ] Exit accuracy verified

### Phase 4: Enhancement (Optional - Claude API)
- [ ] Research Claude momentum detection capability
- [ ] Implement contextual trailing distance (if viable)
- [ ] Document results

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (pending assignment)
**Blocker:** None
**Next Concrete Action:** Research professional trailing TP implementations

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Copilot
**Token Estimate:** 16 (research 2 + core 10 + testing 3 + enhancement 1)
**Severity:** Medium (profit optimization)
**Status:** For Development. Needs research phase on AI API requirements. Profit optimization feature.

## Next Steps

### Immediate (After Approval):

1. **Algorithm Research** (1 day)
   - Look at professional trailing TP implementations
   - Validate logic with manual examples
   - Decide on refresh interval

2. **Technical Spike** (1 day)
   - Build TrailingTPManager skeleton
   - Test algorithm on sample price data
   - Verify performance (refresh interval impact)

3. **Design UI** (1 day)
   - Wireframe TP configuration panel
   - Plan integration with existing TP logic
   - Design visualization

### During Implementation:

Phase 1 (Research) — 1 day
Phase 2 (Implementation) — 3-4 days
Phase 3 (Testing) — 1-2 days
Phase 4 (Enhancement) — 1 day (optional)
**Total: ~6-8 days**

### Post-Implementation:

1. **Live testing** with real market data
2. **User feedback** on trailing distance tuning
3. **Performance profiling** (is 5-second refresh adequate?)
4. **Consider Phase 2 enhancements** (volatile distance, Claude integration, etc.)

---

## Claude API Research (For Phase 4 or future)

If exploring Claude API for smart trailing distance:

```
Questions to Answer:
═════════════════════════════════════════════════════════

1. Can Claude analyze price momentum reliably?
   - Train on historical price data
   - Predict if momentum will continue or reverse
   - Use prediction to set trailing distance

2. Can Claude adjust for volatility?
   - Measure recent volatility (ATR, Bollinger Bands, etc.)
   - Increase trailing distance in high volatility
   - Decrease in low volatility

3. What's the latency?
   - Can Claude API respond in < 1 second?
   - Or would it need to batch analyze every 5 minutes?

4. What's the accuracy?
   - Can it predict momentum better than simple rules?
   - Would it reduce false exits?
   - Would it capture more upside?

Recommendation: Start with simple rules. Test Claude integration after gathering real trading data.
```
