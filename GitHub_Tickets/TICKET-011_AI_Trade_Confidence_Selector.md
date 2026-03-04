# TICKET-011: AI Trade — Add Confidence Selector

**Status:** ✅ Complete
**Priority:** High
**Assigned To:** Claude Sonnet 4.6
**Due Date:** 08/04/2026
**Completed:** 2026-03-02
**Tokens:** 20 (estimate) / 2 (actual — delivered via TICKET-023)
**Labels:** `feature,ai-trade,confidence-selector,ui,complete`

> **Superseded by TICKET-023.** The MinConfidencePct inline TextBox was implemented as part
> of the AI Trade Page refactor (TICKET-023 / TICKET-014). The confidence threshold field
> appears in Panel 1 Row 2 alongside Capital / Qty / TP / SL with a default value of 75.
> Applied to every signal evaluation in `StrategyExecutionEngine`. See TICKET-023 for details.

## Problem Statement

The **Backtest tab** implements a confidence level selector that shows different SHARPE and win rate metrics at different confidence thresholds. However, the **AI Trade tab** is missing this critical control, preventing users from applying the same confidence filtering to live trading.

**Impact:**
- Users cannot reconcile backtest SHARPE results with live trading performance
- No confidence filtering applied in AI Trade, unlike backtest workflow
- Missing control creates inconsistency between backtest and live modes
- Strategy signal generation ignores user's desired confidence threshold

---

## Requirements

### 1. **Confidence Level Selector UI**
Add a **ComboBox** or **RadioButton** group to AI Trade tab (Panel 1, Row 3) to select confidence level.

**Options:** (align with Backtest implementation)
- `50%` — All signals (lowest threshold)
- `60%` — Medium confidence (default, recommended)
- `70%` — High confidence
- `80%` — Very high confidence (conservative)
- `90%` — Extreme confidence only (rarest signals)

**UI Placement:**
- **Panel 1, Row 3** (below Capital/Qty/TP/SL controls)
- Label: `"Confidence Level:"`
- Style: Dropdown ComboBox with white text on dark background (follow TICKET-019 pattern)
- Default selection: `60%`
- Font size: 12
- Width: Match other controls (≈200px)

### 2. **ViewModel Property**
Add to `AiTradingViewModel.vb`:
```vb
Private _confidenceLevel As Integer = 60  ' Default 60%
Public Property ConfidenceLevel As Integer
    Get
        Return _confidenceLevel
    End Get
    Set(value As Integer)
        If SetProperty(_confidenceLevel, value) Then
            ' Notify strategy if monitoring is running
            If IsRunning Then
                ' Re-evaluate current bar with new confidence
                ' (handled by ExecuteStrategy)
            End If
        End If
    End Set
End Property

' Available options for ComboBox
Public ReadOnly Property AvailableConfidenceLevels As ObservableCollection(Of Integer) =
    New ObservableCollection(Of Integer) From {50, 60, 70, 80, 90}
```

### 3. **Integration with Strategy Execution**
Modify `ApplyEmaRsiCombined()` to pass confidence level to strategy definition:
```vb
Private Sub ApplyEmaRsiCombined()
    ' ...existing code...

    Dim strategy = New StrategyDefinition With {
        .IndicatorType = StrategyIndicatorType.EmaRsiCombined,
        .ConditionType = StrategyConditionType.EmaRsiWeightedScore,
        .ConfidenceLevel = _confidenceLevel,  ' NEW
        ' ...other properties...
    }

    ' ...rest of method...
End Sub
```

### 4. **Strategy Execution Engine**
Modify `StrategyExecutionEngine.vb` to apply confidence threshold:

In the `DoCheckAsync()` method, when `StrategyConditionType.EmaRsiWeightedScore` is evaluated:
```vb
' Confidence filtering
Dim confidenceThreshold As Decimal = CDec(_strategy.ConfidenceLevel) / 100D

If signal = StrategySignal.Long Then
    If upPct < confidenceThreshold Then
        signal = StrategySignal.None  ' Filter out: below confidence
    End If
ElseIf signal = StrategySignal.Short Then
    If downPct < confidenceThreshold Then
        signal = StrategySignal.None  ' Filter out: below confidence
    End If
End If
```

### 5. **Monitoring Output**
When confidence filter suppresses a signal, log it:
```
[14:35:22] EMA/RSI Weighted Score calculated: Bull=65%, Bear=0%
[14:35:22] ✓ LONG signal qualified (65% >= 60% confidence)
--- or ---
[14:35:22] EMA/RSI Weighted Score calculated: Bull=55%, Bear=0%
[14:35:22] ✗ Signal blocked by confidence filter (55% < 60% threshold)
```

---

## Confidence Level Behavior

| Level | Meaning | Use Case |
|-------|---------|----------|
| **50%** | All EMA/RSI signals (no filtering) | Maximum signal frequency, allows swing trades |
| **60%** | Medium confidence (default) | Balanced risk/reward, aligns with backtest results |
| **70%** | Strong directional signals only | Conservative, filters marginal trades |
| **80%** | Very strong signals | Risk-averse, tight entries |
| **90%** | Extreme confidence only | Rarest signals, potential high-probability trades |

---

## Design Considerations

### A. **Real-time Re-evaluation**
When user changes confidence level during monitoring:
- **Option 1:** Apply to next bar evaluation only (simplest, no interruption)
- **Option 2:** Re-evaluate current bar immediately with new threshold
- **Recommendation:** Option 1 — safer, avoids mid-bar confusion

### B. **Backtest Sync**
The confidence selector **must use the same values/naming** as the Backtest tab to avoid user confusion. Coordinate with TICKET-006 (Backtest Page rewrite) to ensure alignment.

### C. **Default Behavior**
- Default confidence: `60%` (medium)
- Pre-selected in ComboBox on app startup
- Persisted to `TradingSettings` (if desired, see next steps)

### D. **Color/Visual Feedback (Future)**
- Green highlight for "Good confidence" trades (≥70%)
- Yellow highlight for "Medium confidence" (60-70%)
- Gray/dim for "Suppressed by filter" signals (logged but not traded)

---

## Implementation Plan

### Phase 1: ViewModel & Properties (4 tokens)
1. Add `ConfidenceLevel` property to `AiTradingViewModel.vb`
2. Add `AvailableConfidenceLevels` collection
3. Update `ApplyEmaRsiCombined()` to include confidence in strategy definition
4. Add re-evaluation logic trigger on confidence change

### Phase 2: View & UI (3 tokens)
1. Add ComboBox to `AiTradingView.xaml` Panel 1, Row 3
2. Bind to `ConfidenceLevel` property
3. Bind ItemsSource to `AvailableConfidenceLevels`
4. Apply white text styling (follow TICKET-019 pattern)
5. Set default selected value to `60`

### Phase 3: Execution Engine Integration (8 tokens)
1. Modify `StrategyDefinition.vb` to add `ConfidenceLevel` property
2. Update `StrategyExecutionEngine.DoCheckAsync()` to apply confidence threshold
3. Add logging for filtered signals
4. Test confidence filtering across all confidence levels

### Phase 4: Testing & Validation (5 tokens)
1. Unit tests for confidence threshold logic
2. Integration test: EMA/RSI signals at different confidence levels
3. Live trade simulation with confidence filtering enabled
4. Validate backtest/live consistency
5. Performance testing (verify no polling delays)

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add `ConfidenceLevel` property, update `ApplyEmaRsiCombined()` |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Add ComboBox control to Panel 1, Row 3 |
| `src/TopStepTrader.Core/Models/StrategyDefinition.vb` | Add `ConfidenceLevel` property |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Implement confidence threshold filtering in `DoCheckAsync()` |
| `GitHub_Tickets/TICKETS.csv` | Update status when complete |

---

## Dependencies & Blockers

**Blocked By:**
- ✅ TICKET-014 (AI Trade Redesign) — *Complete*
- ⏳ **TICKET-006 (Backtest Page Rewrite)** — Must coordinate confidence level values with backtest implementation

**Related Tickets:**
- TICKET-021 (Default Practice Account) — Independent, can proceed in parallel
- TICKET-022 (UI Consolidation) — May affect ComboBox placement; coordinate on final layout

**No Blockers:** Can proceed immediately after receiving approval.

---

## Acceptance Criteria

- [ ] ComboBox rendered in Panel 1, Row 3 with white text visible on dark background
- [ ] Default confidence level is `60%` on app startup
- [ ] Changing confidence level immediately affects next bar evaluation
- [ ] EMA/RSI Weighted Score respects confidence threshold (filters low-confidence signals)
- [ ] Filtered signals are logged with reason: "✗ Signal blocked by confidence filter (XX% < YY% threshold)"
- [ ] Backtest and AI Trade use identical confidence level names/values (aligned with TICKET-006)
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: Existing EMA/RSI strategy works at default 60% confidence
- [ ] Performance: No polling latency increase from confidence filtering logic

---

## Next Steps

### Immediate (After Approval):
1. **Coordinate with TICKET-006 owner** on backtest confidence implementation
   - What are the exact confidence level values used in backtest?
   - Are confidence thresholds pre-computed or real-time?
   - How are confidence results displayed in backtest results table?

2. **Clarify re-evaluation timing**
   - Should confidence change apply immediately or on next bar?
   - If real-time re-eval is needed, should it trigger new trade evaluation?

3. **Define "signal blocked" logging format**
   - Should blocked signals show in monitoring output?
   - Should they appear in trade history (with "Rejected - Low Confidence" reason)?

### During Implementation:
1. Start with **Phase 1 (ViewModel)** — low risk, no UI dependencies
2. Add **Phase 2 (UI)** once ViewModel is wired
3. Coordinate **Phase 3 (Execution Engine)** with TICKET-006 completion to ensure confidence definitions match backtest

### Post-Implementation:
1. **Live trading validation:** Run for 1 week at 60% confidence, then test 70% and 80%
2. **Compare results:** AI Trade P&L vs Backtest SHARPE at each confidence level
3. **Consider persistence:** Should confidence level be saved to `TradingSettings.xml`?

---

## Related Documentation

- **TICKET-014**: AI Trade Layout Redesign — EMA/RSI Weighted Scoring logic
- **TICKET-006**: Backtest Page Rewrite — Must define confidence level values
- **TICKET-021**: Default Practice Account — UI consolidation context
- **patterns.md**: MVVM property binding patterns

---

## Progress Tracking

### Phase 1: Backtest Integration (Waiting for TICKET-006)
- [ ] TICKET-006 Backtest Page completed
- [ ] Confidence level values extracted from backtest results
- [ ] Confidence ranges documented (50-100%)

### Phase 2: Confidence Selector UI
- [ ] AiTradingViewModel confidence properties added
- [ ] AiTradingView.xaml slider/selector added
- [ ] Real-time confidence display implemented

### Phase 3: Strategy Execution Engine
- [ ] EmaRsiWeightedScore condition type updated
- [ ] Confidence threshold logic integrated
- [ ] Weighted scoring adjusted for confidence input

### Phase 4: Testing & Validation
- [ ] Unit tests for confidence filtering
- [ ] Integration tests (various confidence levels)
- [ ] Live trading validation (P&L comparison)
- [ ] SHARPE consistency verification

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Blocked by TICKET-006 (Backtest Page completion)
**Blocker:** TICKET-006 must be completed first (need confidence level definitions)
**Next Concrete Action:** Monitor TICKET-006 progress; start Phase 1 upon completion

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Claude Sonnet 4.6
