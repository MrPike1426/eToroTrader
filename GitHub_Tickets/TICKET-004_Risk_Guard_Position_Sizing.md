# TICKET-004: Risk Guard Position Sizing

**Status:** Backlog
**Priority:** Low
**Severity:** Low
**Assigned To:** Copilot
**Due Date:** 20/05/2026
**Tokens:** 16
**Labels:** `feature,future,risk-management,position-sizing`

---

## Problem Statement

Currently, users manually enter quantity (number of contracts) for each trade. This is **error-prone and doesn't follow risk management best practices**.

**Ideal Workflow:**
1. User sets **account risk percentage** (e.g., "Risk 2% of account per trade")
2. User enters **stop loss distance** (e.g., "Place SL 10 points below entry")
3. System calculates **optimal quantity** based on account size and risk parameters
4. User confirms quantity and executes trade

**Current State:**
- Quantity is entered manually (user guesses)
- No position sizing calculation
- Risk Guard tracks account-level limits but not per-trade sizing

**Business Value:**
- Prevents over-leveraging (key risk management rule)
- Maintains consistent risk/reward ratios
- Protects account from catastrophic losses
- Professional trading best practice

---

## Requirements

### A. Position Sizing Calculator

Formula:
```
RiskAmount = AccountBalance × RiskPercentage
StopLossDistance = EntryPrice - StopLossPrice (in ticks)
OptimalQuantity = RiskAmount / (StopLossDistance × ContractMultiplier)

Example (MES, $100k account, 2% risk, SL 10 points):
  RiskAmount = $100,000 × 0.02 = $2,000
  StopLossDistance = 10 points
  ContractMultiplier = $50/point
  Quantity = $2,000 / (10 × $50) = 4 contracts
```

**Rules:**
- Round down to nearest whole contract (never over-allocate)
- Cap at max position size set in Risk Guard
- Validate against daily loss limit
- Show warning if risk exceeds account risk limit

### B. UI Integration (AI Trade Tab)

Add to AI Trade tab:

```
┌─────────────────────────────────┐
│  Position Sizing Helper         │
├─────────────────────────────────┤
│ Account Balance:  $100,000       │
│ Risk % per Trade: [2%      ]     │
│                                  │
│ Entry Price:      4532.75        │
│ Stop Loss Price:  4522.75        │
│ SL Distance:      10 points      │
│                                  │
│ 💡 Suggested Qty: 4 contracts    │
│ Risk at SL:       $2,000 (2%)    │
│                                  │
│ [Use This Qty] [Custom Qty]      │
└─────────────────────────────────┘
```

### C. Stop Loss Suggestions

For each strategy, suggest appropriate SL distance:

```
Strategy: EMA/RSI Bullish
  Typical SL: 8-15 points below entry
  Suggested: 10 points (based on historical volatility)
  Confidence: 65% win rate
```

Load from backtesting data (TICKET-006 provides this).

### D. Risk Visualization

Show risk metrics:

```
Position Summary:
  Entry:      4532.75
  Stop Loss:  4522.75
  Quantity:   4 contracts

Risk Metrics:
  Max Loss:   $2,000 (2% of account) ✅ SAFE
  Max P&L:    +$5,000 (reward if target hit) ✅ 2.5:1 ratio

Daily Exposure: $2,000 / $10,000 limit → 20% used
```

---

## Implementation Plan

### Phase 1: Design & Architecture (2 tokens)

1. **Define Position Sizing Service:**
   - `CalculateOptimalQuantity(balance, riskPct, entryPrice, slPrice, contractMultiplier)`
   - Contract multiplier per symbol (MES=$50, MNQ=$20, MGC=$100, MCL=$10)
   - Validation rules (don't exceed max position, daily limit)

2. **Design Risk Metrics Model:**
   - Store historical win rates per strategy (from backtest)
   - Calculate suggested SL distance based on volatility
   - Show risk/reward ratio

3. **Plan UI Integration:**
   - Where on AI Trade tab? (new panel or integrate with existing?)
   - When to show? (after selecting contract and strategy)
   - Real-time calculation as user adjusts entry/SL

### Phase 2: Implementation (10 tokens)

1. **PositionSizingService.vb:**
   ```vb
   Public Class PositionSizingService
       Function CalculateOptimalQuantity(
           accountBalance As Decimal,
           riskPercentage As Decimal,
           entryPrice As Decimal,
           stopLossPrice As Decimal,
           contractMultiplier As Decimal,
           maxPosition As Integer) As Integer

           ' Calculate risk amount
           Dim riskAmount = accountBalance * riskPercentage

           ' Calculate SL distance
           Dim slDistance = Math.Abs(entryPrice - stopLossPrice)

           ' Calculate quantity
           Dim quantity = Math.Floor(riskAmount / (slDistance * contractMultiplier))

           ' Cap at max position
           Return Math.Min(quantity, maxPosition)
       End Function
   End Class
   ```

2. **AiTradingViewModel.vb Updates:**
   - Add `RiskPercentageEditable` binding
   - Add `SuggestedQuantity` property (calculated)
   - Add `RiskMetrics` (max loss, reward, ratio)
   - Integrate with existing entry/SL price inputs

3. **AiTradingView.xaml Updates:**
   - Add "Position Sizing Helper" panel
   - Show suggested quantity
   - Allow manual override
   - Display risk metrics

4. **Contract Multiplier Lookup:**
   - Store in ContractSelectorControl or database
   - Load when contract selected
   - Use in calculation

### Phase 3: Enhancement with Backtest Data (2 tokens)

Once TICKET-006 (Backtest) is complete:

1. Load historical strategy win rates
2. Calculate suggested SL distance based on volatility
3. Show confidence level for SL distance
4. Example: "EMA/RSI Bullish: 65% win rate, suggest SL 10 points"

### Phase 4: Testing & Validation (2 tokens)

1. **Unit Tests:**
   - Test quantity calculation vs manual formula
   - Test max position capping
   - Test edge cases (SL = entry, extreme risk %)

2. **Integration Tests:**
   - Verify with Risk Guard daily loss limit
   - Test quantity suggestion in UI
   - Test with all 4 contracts (MES, MNQ, MGC, MCL)

3. **Manual Testing:**
   - Calculate quantity manually, compare with app
   - Verify risk metrics accuracy
   - Test override workflow

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/RiskManagement/PositionSizingService.vb` | NEW - Calculation logic |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | UPDATE - Add sizing properties |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | UPDATE - Add sizing panel |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml.vb` | UPDATE - Bind sizing inputs |
| `src/TopStepTrader.Core/Models/ContractInfo.vb` | UPDATE - Add ContractMultiplier |

---

## Acceptance Criteria

- [ ] Position sizing calculation is mathematically correct (verify vs manual formula)
- [ ] Suggested quantity respects MaxPosition limit
- [ ] Suggested quantity respects daily loss limit
- [ ] UI shows suggested quantity based on entry/SL prices
- [ ] User can override suggested quantity
- [ ] Risk metrics display correctly (max loss %, reward, ratio)
- [ ] Works for all 4 contracts (MES, MNQ, MGC, MCL)
- [ ] Risk visualization is accurate and helpful
- [ ] No impact on existing trading workflow
- [ ] Build succeeds: 0 errors, 0 warnings

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Auto-calculate vs Manual** | Auto-calc prevents mistakes ✅ | User may not trust it ❌ |
| **Show in AI Trade tab (vs new tab)** | Integrated workflow ✅ | Crowds UI ❌ |
| **Round down (vs round to nearest)** | Conservative ✅ | Leaves money on table ❌ |
| **Use account balance (vs dynamic equity)** | Simple ✅ | Doesn't account for open P&L ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **User ignores suggestion and over-sizes** | Show warning, color-code risk level (red if > risk limit) |
| **Calculation inaccuracy → wrong quantity** | Unit test against manual formula, show calculation breakdown |
| **Doesn't account for slippage** | Add 1-2 tick buffer to SL distance for realistic risk |
| **Feature feels incomplete without backtest data** | Phase 3 adds context (win rate, historical SL distance) |

---

## Related Tickets

- **TICKET-006:** Backtest Page — Provides historical strategy data (win rates, optimal SL)
- **TICKET-004 (this):** Feeds into position sizing for live trading
- **TICKET-010:** Stop Loss Bug — Position sizing useless if SL doesn't execute

---

## ML Enhancement (Future)

Once you have trading data:

```
Implement machine learning model:
- Input: Strategy, market volatility, account size
- Output: Optimal position size + suggested SL distance
- Train on: Historical trades, their P&L, actual SL distance

This becomes truly adaptive and learns from your trading patterns.
```

---

## Next Steps

### Immediate (After Approval):

1. **Design Contract Multipliers** (1 day)
   - Verify multipliers for all 4 contracts
   - Micro-futures: MES=$50/pt, MNQ=$20/pt, MGC=$100/oz, MCL=$10/bbl
   - Document source (CBOT, CME)

2. **Design Position Sizing Service** (1 day)
   - Write calculation logic in pseudocode
   - Define validation rules
   - Plan UI placement

3. **Spike: Basic Calculator** (1 day)
   - Build PositionSizingService with formula
   - Test manually with spreadsheet
   - Prove concept works

### During Implementation:

1. Phase 1 (Design) — 2 days
2. Phase 2 (Implementation) — 3-4 days
3. Phase 3 (Backtest integration) — Deferred until TICKET-006 done
4. Phase 4 (Testing) — 1 day
5. Total: ~1 week (Phase 1-2, Phase 4)

### Post-Implementation:

1. **Backtest Integration** (Phase 3) once TICKET-006 complete
2. **Real trading feedback** — Does suggested quantity feel right?
3. **ML Enhancement** (Phase 5) after 100+ trades with actual data

---

## Success Metrics

- ✅ Quantity calculation within 1 contract of manual formula
- ✅ Risk metrics accurate to nearest $100
- ✅ Users find suggestions helpful (feedback)
- ✅ Prevents over-leveraging (tracked in trading logs)
- ✅ Zero calculation errors in production

---

---

## Progress Tracking

### Phase 1: Design & Architecture
- [ ] Position sizing formula designed and validated
- [ ] Contract multipliers documented (MES, MNQ, MGC, MCL)
- [ ] Validation rules defined (max position, daily limit)
- [ ] Risk metrics model designed

### Phase 2: Core Implementation
- [ ] PositionSizingService.vb created with calculation logic
- [ ] ContractInfo.vb updated with ContractMultiplier
- [ ] AiTradingViewModel properties added (RiskPercentage, SuggestedQuantity)
- [ ] AiTradingView.xaml UI panel added
- [ ] Risk metrics display implemented

### Phase 3: Backtest Integration (After TICKET-006)
- [ ] Historical win rates loaded from backtest
- [ ] Suggested SL distance calculated (per strategy)
- [ ] Confidence levels integrated

### Phase 4: Testing & Validation
- [ ] Unit tests (formula vs manual calculation)
- [ ] Integration tests (all 4 contracts)
- [ ] Risk limit validation
- [ ] Edge case testing (SL=entry, extreme %s)

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (blocked on framework finalization)
**Blocker:** Strategy framework finalization needed
**Next Concrete Action:** Verify contract multipliers with CME documentation

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Copilot
**Token Estimate:** 16 (design 2 + core 10 + backtest 2 + testing 2)
**Severity:** Low (nice-to-have, not critical for trading)
**Status:** Placeholder. Requires strategy framework finalization first.
