# TICKET-024: AI Trade Page — Strategy Summary & Recommended Settings

**Status:** ✅ Complete
**Priority:** High
**Severity:** Medium
**Assigned To:** Claude Sonnet 4.6
**StartDate:** 2026-03-02
**Completed:** 2026-03-02
**Tokens:** 2 (estimate) / 3 (actual)
**Labels:** `feature,ai-trade,ux-enhancement,strategy-summary,plain-english,complete`

> **Delivered.**
> - `StrategyNakedDescription` property + `HasStrategyDescription` + `StrategyDescriptionPanelVisible` added to `AiTradingViewModel`
> - **Panel 2b** "WHAT THIS STRATEGY DOES" added to `AiTradingView.xaml` — visible only when strategy is selected AND engine is not running
> - `ApplyEmaRsiCombined()` sets the ≤200-word Naked Trader prose description
> - Auto-fills TP=40 / SL=20 / Qty=1 as strategy-recommended defaults via property setters
> - Panel collapses automatically when ▶ Start Monitoring is clicked

---

## Problem Statement

When a user selects a strategy card on the AI Trade page, they receive only a technical one-liner
(`"EMA/RSI Combined | 5-min bars | 8-hr session | score ≥ 60% triggers entry"`). There is no plain-English
explanation of what the strategy actually does, what it is waiting for, or what constitutes
"all conditions met." Additionally, the TP/SL/Quantity/Timeframe fields are not auto-populated
with strategy-optimised defaults when a strategy is selected.

**Current Limitations:**
- ❌ No human-readable description of what the strategy waits for
- ❌ No explanation of when a trade fires
- ❌ TP/SL/Qty defaults are generic (40/20/1) and not surfaced as "strategy-recommended"
- ❌ Timeframe (5-min) is embedded in strategy definition but never shown to the user

**Desired Outcome:**
- ✅ Clicking a strategy card displays a ≤200-word plain-English summary panel below the card grid
- ✅ Summary styled in "Naked Trader" prose — conversational, no jargon, tells a story
- ✅ Summary explains: what it waits for · what constitutes all conditions met · when trade fires
- ✅ Strategy card click also auto-fills TP/SL/Qty/Timeframe with strategy-optimal defaults
- ✅ Panel is hidden until a strategy is selected

---

## Requirements

### A. Strategy Summary Panel (UI)

**New panel** inserted between the Strategy card grid and the existing Confidence Check panel.

- Heading: `"WHAT THIS STRATEGY DOES"` (styled using `SectionHeadingStyle`)
- Body: read-only `TextBox` bound to `StrategyNakedDescription`, word-wrapped, `CardBrush` background
- Panel visibility: `Collapsed` until `HasStrategyDescription = True`
- Font size: 12px, `TextPrimaryBrush` foreground
- Padding: `10,8` on the TextBox — enough breathing room

### B. Plain-English Description (EMA/RSI Combined)

**Researched from:** live `StrategyExecutionEngine.vb` EmaRsiWeightedScore logic.
Text must explain all 6 weighted signals, the threshold mechanic, entry, and bracket orders.

**Approved text (186 words — Naked Trader style):**

> Every 30 seconds, this strategy glances at the latest completed 5-minute bar on your chosen
> contract and runs six quick checks, tallying a bull score from 0 to 100.
>
> It awards 25 points if the fast moving average (EMA21) sits above the slow one (EMA50) — that's
> your classic uptrend sign. Another 20 points if the closing price is above the fast EMA, and 15
> more if it's above the slow one too. The RSI14 is also in the mix for up to 20 points — a deeply
> oversold reading (below 30) earns the full amount; an overbought one (above 70) earns nothing.
> Then 10 points if the fast EMA is rising since the last bar, and a final 10 if at least two of
> the last three candles closed higher.
>
> When the total bull score hits your confidence threshold — 75 by default — a market Long order
> fires straight away, bracketed by your take-profit above and stop-loss below. If the bear score
> reaches the threshold first (bull score below 25), a Short goes in instead.
>
> Recommended defaults: 5-minute bars · 40-tick take-profit · 20-tick stop-loss · 1 contract.
> The engine runs for 8 hours, covering both London open and New York session overlap.

### C. Strategy-Recommended Defaults (Auto-fill)

When the EMA/RSI Combined card is clicked, the following UI fields are auto-populated:

| Field | Recommended Value | Rationale |
|-------|------------------|-----------|
| TP (ticks) | **40** | ~2× average 5-min ATR on micro-futures; achievable without over-reaching |
| SL (ticks) | **20** | 1:2 risk/reward; tight enough to protect capital |
| Quantity | **1** | Conservative starting point for new users |
| Timeframe | **5 min** | Embedded in strategy definition (TimeframeMinutes=5); shown in description |

These values are set via the ViewModel property setters (TakeProfitTicks, StopLossTicks, Quantity)
so bindings update the TextBoxes automatically.

---

## Implementation Plan

### Phase 1: ViewModel (AiTradingViewModel.vb)

1. **Add `StrategyNakedDescription` property** (String, default empty)
2. **Add `HasStrategyDescription` property** (Boolean, default False — drives panel visibility)
3. **Update `ApplyEmaRsiCombined()`:**
   - Set `TakeProfitTicks = 40`, `StopLossTicks = 20`, `Quantity = 1` (via property setters)
   - Set `StrategyNakedDescription` to approved text above
   - Set `HasStrategyDescription = True`

### Phase 2: UI (AiTradingView.xaml)

1. **Insert new panel** between `</Border>` (end of Panel 2) and `<!-- Panel 3: Confidence Check -->`:
   ```xaml
   <!-- ── Panel 2b: Strategy Summary ──────────────────────── -->
   <Border Background="{StaticResource SurfaceBrush}"
           CornerRadius="6" Padding="20,14" Margin="0,8,0,0"
           Visibility="{Binding HasStrategyDescription,
                        Converter={StaticResource BoolToVisibilityConverter}}">
       <StackPanel>
           <TextBlock Text="WHAT THIS STRATEGY DOES"
                      Style="{StaticResource SectionHeadingStyle}"
                      Margin="0,0,0,6"/>
           <TextBox Text="{Binding StrategyNakedDescription, Mode=OneWay}"
                    IsReadOnly="True"
                    AcceptsReturn="True" TextWrapping="Wrap"
                    FontSize="12" Padding="10,8"
                    Background="{StaticResource CardBrush}"
                    BorderThickness="0"
                    Foreground="{StaticResource TextPrimaryBrush}"/>
       </StackPanel>
   </Border>
   ```

### Phase 3: Build & Verify

- Build: `dotnet build src/TopStepTrader.UI/TopStepTrader.UI.vbproj`
- ✅ 0 errors, 0 warnings

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add 2 properties + update ApplyEmaRsiCombined() |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Add Panel 2b (strategy description) |

---

## Acceptance Criteria

- [ ] Strategy summary panel is **hidden** on page load (no strategy selected)
- [ ] Clicking EMA/RSI Combined card shows the 186-word Naked Trader description
- [ ] Description is readable (white text, dark background, word-wrapped)
- [ ] TP/SL/Qty TextBoxes auto-update to 40/20/1 when EMA/RSI Combined is selected
- [ ] User can still override TP/SL/Qty after selection
- [ ] Build: 0 errors, 0 warnings
- [ ] No regression: existing AI Trade functionality still works

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Hardcoded description vs. database** | Simpler (only 1 active strategy) ✅ — Requires code change for future strategies ❌ |
| **Auto-fill defaults vs. show-only recommendation** | Better UX (fields pre-filled) ✅ — Overrides user's current values ❌ |
| **Panel below card grid vs. tooltip** | More space for prose ✅ — Longer page ❌ |
| **ReadOnly TextBox vs. TextBlock** | Allows text selection/copy ✅ — Minimal visual difference ❌ |

---

## Future Enhancements

- [ ] Per-strategy descriptions for Strategies 2–4 when they are activated
- [ ] Dynamic description generation from backtest results (win rate, avg TP hit %, etc.)
- [ ] Backtest-derived TP/SL recommendations per contract (e.g., MGC vs. MES may differ)
- [ ] User-editable description field for custom note-taking

---

## Related Tickets

- **TICKET-023:** Add Confidence Score to AI Trade Page — both improve AI Trade UX
- **TICKET-022:** AI Trade UI Consolidation — layout clean-up that this panel sits within
- **TICKET-006:** Backtest Page — source of future backtest-derived recommendation data

---

**Created:** 2026-03-02
**Model:** Claude Sonnet 4.6
**Token Estimate:** 2 (ViewModel + XAML)
**Severity:** Medium (UX improvement, not blocking)
**Status:** Ready for Implementation — No blockers
