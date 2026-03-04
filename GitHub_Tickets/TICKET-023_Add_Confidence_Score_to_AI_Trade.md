# TICKET-023: Add Confidence Score to AI Trade Page

**Status:** ✅ Complete
**Priority:** High
**Severity:** Medium
**Assigned To:** Claude Haiku
**StartDate:** 2026-03-02
**Completed:** 2026-03-02
**Tokens:** 2
**Labels:** `feature,ai-trade,confidence-threshold,ui-enhancement,complete`

> **Delivered.** `MinConfidencePct` TextBox added inline to Panel 1 Row 2 (right of TP/SL).
> Default 75. VM property clamps 0–100. `ExecuteStart` stamps it onto `_currentStrategy.MinConfidencePct`.
> `StrategyExecutionEngine` compares `upPct`/`downPct` against this threshold.
> Supersedes TICKET-011.

---

## Problem Statement

The AI Trade page currently allows users to configure Capital, Quantity, Take Profit, and Stop Loss, but there is no way to set a **minimum confidence threshold** for strategy signals. Users should be able to control signal confidence filtering to manage trade quality vs. quantity trade-off.

**Current Limitations:**
- ❌ No confidence threshold selector on AI Trade page
- ❌ Confidence is fixed or uses a default backend value
- ❌ Users can't filter out low-confidence signals during live trading
- ❌ Inconsistent with Backtest page (which already has confidence selector per TICKET-011)

**Desired Outcome:**
- ✅ Add inline confidence score field (default 75%)
- ✅ Field integrated into Capital/Qty/TP/SL box layout (squeezed horizontally)
- ✅ Confidence threshold applied when running strategy
- ✅ Consistent UX with Backtest page confidence selector

---

## Requirements

### A. UI Layout (Inline with Existing Controls)

**Current Layout:**
```
Capital:     [50000]      Qty: [4]
TP:          [4542.75]    SL: [4522.75]
```

**New Layout (Squeezed):**
```
Capital: [50000]  Qty: [4]  Confidence: [75%] ▼
TP:      [4542.75]  SL: [4522.75]
```

**Notes:**
- "Confidence:" label + input field added to Row 0 or inline with existing controls
- Squeeze Capital/Qty/TP/SL textboxes to make room (reduce widths if needed)
- Use a NumericUpDown or TextBox (0–100) with % suffix
- Default value: **75%**
- Minimum: 0%, Maximum: 100%
- Styled to match AI Trade page theme (white text on dark background)

### B. Confidence Application

When user clicks "Run Strategy" or "Send Trade":
1. Read `SelectedConfidence` from UI (default 75%)
2. Pass confidence threshold to `StrategyExecutionEngine` or `OutcomeMonitorWorker`
3. Strategy filters signals: only execute if `signal.Confidence >= selectedConfidence`
4. Log confidence threshold in trade outcome (for auditing)

### C. Integration with Backtest Page

- **Not in scope for this ticket:** Synchronizing confidence across Backtest ↔ AI Trade pages
- **Future enhancement (TICKET-011 follow-up):** Share confidence settings between pages via settings or user preference

---

## Implementation Plan

### Phase 1: UI & ViewModel (1 token)

**Duration:** 1 day

1. **AiTradingView.xaml**
   - Squeeze Capital/Qty textboxes (reduce Width or use ColumnDefinitions ratio)
   - Squeeze TP/SL textboxes similarly
   - Add "Confidence:" label + NumericUpDown control (0–100) in same grid row
   - Use existing theme colors (white text on dark background)
   - Tooltip: "Minimum signal confidence threshold (0–100%)"

2. **AiTradingViewModel.vb**
   - Add `SelectedConfidence` property (default "75")
   - Type: String (for TextBox binding) or Decimal (for validation)
   - Include validation: 0 ≤ confidence ≤ 100
   - Expose as public property for command/execution binding

3. **Build Verification**
   - ✅ XAML compiles without errors
   - ✅ Bindings resolve in designer
   - ✅ Default value (75) displays in UI

### Phase 2: Strategy Integration (1 token)

**Duration:** 1–2 days

1. **Strategy Execution Hook**
   - Identify where "Run Strategy" is handled (`ExecuteRunStrategy()` or similar)
   - Capture `SelectedConfidence` value before execution
   - Pass confidence to `StrategyExecutionEngine.ExecuteAsync(..., confidence)`

2. **StrategyExecutionEngine Update**
   - Add optional `minConfidence` parameter to `ExecuteAsync()` or similar
   - Filter signals: `if (signal.Confidence >= minConfidence) then execute trade`
   - Log confidence threshold in trade outcome record

3. **OutcomeMonitorWorker Update (if applicable)**
   - If live-trading background worker also executes strategies, update it similarly
   - Use confidence from `TradingSettings` or per-strategy config if not overridden by UI

4. **Testing**
   - Verify confidence threshold filters signals correctly
   - Verify trades only execute when `signal.Confidence >= threshold`
   - Verify outcome records include confidence threshold

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | NEW — Add Confidence field inline with Capital/Qty/TP/SL |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | NEW — Add SelectedConfidence property |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | UPDATE — Accept minConfidence parameter, filter signals |
| `src/TopStepTrader.Services/Feedback/OutcomeMonitorWorker.vb` | UPDATE (optional) — Apply confidence threshold if applicable |
| `src/TopStepTrader.UI/Commands/RunStrategyCommand.vb` | UPDATE — Pass confidence to execution method |

---

## Acceptance Criteria

- [ ] Confidence field appears inline with Capital/Qty/TP/SL (squeezed layout)
- [ ] Default value is 75%
- [ ] User can change confidence to 0–100%
- [ ] Validation prevents invalid entries (< 0 or > 100)
- [ ] Confidence threshold is applied when running strategy
- [ ] Trades only execute if signal.Confidence >= selected threshold
- [ ] Trade outcome records include applied confidence threshold
- [ ] UI styled consistently (white text on dark background)
- [ ] Tooltip explains confidence purpose
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: Existing AI Trade page functionality still works

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Inline confidence vs. separate control** | Compact UI ✅ | Takes up horizontal space ❌ |
| **Default 75% vs. 50%** | Conservative (fewer trades, higher quality) ✅ | May miss good signals ❌ |
| **0–100% range vs. 0.0–1.0** | User-friendly (%) ✅ | Slightly more parsing ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Field too small to read when squeezed** | Use fixed-width font, test at different window sizes |
| **User forgets to adjust confidence, trades at 75% by default** | Tooltip + help text + UI prominence |
| **Confidence not passed to execution engine** | Add test case verifying parameter flows through |

---

## Related Tickets

- **TICKET-011:** AI Trade Confidence Selector — Similar feature, future sync point
- **TICKET-006:** Backtest Page — Already has confidence selector (future consistency improvement)
- **TICKET-007:** Multi-Strategy Trading — May benefit from per-strategy confidence tuning

---

## Future Enhancements (Phase 2)

- [ ] Save user's preferred confidence to settings/database
- [ ] Per-strategy confidence defaults (e.g., EMA/RSI Combined always at 80%)
- [ ] Sync confidence across Backtest ↔ AI Trade pages
- [ ] Confidence confidence intervals/histogram in strategy results
- [ ] A/B test: confidence threshold impact on P&L

---

## Success Metrics

- ✅ Confidence field integrated without breaking AI Trade page UX
- ✅ Users can confidently set and apply confidence thresholds
- ✅ Trade filtering works as expected (no low-confidence trades execute)
- ✅ Build: 0 errors, 0 warnings
- ✅ No performance degradation

---

**Created:** 2026-03-02
**Model:** Claude Haiku (Assigned)
**Token Estimate:** 2 (UI field 1 + strategy integration 1)
**Severity:** Medium (improves trade quality, not blocking)
**Status:** Ready for Implementation — No blockers

---

## Implementation Notes for Claude Haiku

1. **UI Layout:** Current Capital/Qty/TP/SL layout is likely in a StackPanel or Grid. Adjust column widths to fit confidence field. Consider using `Width="Auto"` + `MinWidth` to squeeze elements.

2. **Binding:** Use simple `SelectedConfidence` property (string or decimal) with TwoWay binding.

3. **Validation:** Simple validation (0–100). Consider using a NumericUpDown control which enforces range automatically.

4. **Integration:** Find where `ExecuteRunStrategy()` is called, capture confidence, pass to engine. Follow existing parameter patterns.

5. **Testing:** Add simple unit test verifying confidence threshold filters signals (mock StrategyExecutionEngine with test signal).

6. **Styling:** Match AI Trade page theme (white text on dark ComboBox background, if using dropdown for presets).

---
