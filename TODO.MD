# TopStepTrader — Cross-Session TODO

**Last Updated:** 2026-03-02
**Build Status:** ✅ 0 errors, 0 warnings
**Current Session:** TICKET-023 ✅ (Confidence Score) + TICKET-024 ✅ (Strategy Summary + Recommended Defaults)

---

## 🔄 In Progress

### TICKET-006 — Backtest Page Complete Rewrite
**Status:** Phases 1–4 ✅ + Phase 5 unit tests ✅ + Session 2 UAT fixes ✅ — Manual testing ongoing
**Spec:** `GitHub_Tickets/TICKET-006_Backtest_Page_Complete_Rewrite.md`

#### Session 2 Changes (2026-03-02) ✅
- ✅ Button reorder: Train Model → Run Backtest → Export CSV (no Cancel)
- ✅ Fixed Run Backtest button disabled after training (command invalidation)
- ✅ Updated contract IDs: H26 (expired) → K26 (active) for MNQ, MES
- ✅ Fixed DatePicker background color (dark blue CardBrush + white text)
- ✅ Added Interval selector combobox (7 timeframe options)

#### Phase 5 Unit Tests — DONE ✅
- 54 unit tests, 0 failures — `src/TopStepTrader.Tests/Backtest/`
- New files added: `BacktestMetrics.vb`, `StrategyDefaults.vb`, `TopStepTrader.Tests.vbproj`

#### Phase 5 Manual Testing Checklist (next session pick-up point)
- [ ] Select MGC or MNQ contract (K26 updated in Session 2)
- [ ] Select "EMA/RSI Combined" strategy → verify Capital=50000, Qty=4, TP=20, SL=10 auto-populate
- [ ] Wait for bar download → confirm ✓ status text, "Run Backtest" button enables
- [ ] **NEW:** Verify Interval selector defaults to "5 min" and lists all 7 options
- [ ] Click "Train Model" → confirm indeterminate progress bar + "✓ Model trained…" → completes
- [ ] **NEW:** Verify "Run Backtest" button is now **enabled** (not disabled) after training
- [ ] Click "Run Backtest" → confirm trades appear in DataGrid + metrics row populates
- [ ] Verify Exit Time > Entry Time on all trades (fixed UAT-BUG-005)
- [ ] Verify StopLoss trades have negative P&L, TakeProfit positive (fixed UAT-BUG-006)
- [ ] Click "Export CSV" → SaveFileDialog opens → file written correctly
- [ ] Repeat for all 4 contracts (MGC, MNQ, MCL, MES)
- [ ] Verify Previous Runs tab shows saved run after backtest

#### New files created in TICKET-006 (all build clean):
| File | Layer | Purpose |
|------|-------|---------|
| `src/TopStepTrader.Core/Interfaces/IBarCollectionService.vb` | Core | Interface + BarEnsureResult |
| `src/TopStepTrader.Services/Market/BarCollectionService.vb` | Services | Paginated bar download (500-bar batches) + SQLite cache |

#### Modified files in TICKET-006:
| File | Change |
|------|--------|
| `src/TopStepTrader.UI/Views/BacktestView.xaml` | Complete rewrite — 13-row grid, new strategy/qty/status controls |
| `src/TopStepTrader.UI/ViewModels/BacktestViewModel.vb` | Complete rewrite — new workflow, training, CSV export |
| `src/TopStepTrader.Services/ServicesExtensions.vb` | Added `IBarCollectionService → BarCollectionService` (Scoped) |

---

## 📋 Backlog (Priority Order)

| Ticket | Title | Priority | Assigned | Blocks |
|--------|-------|----------|----------|--------|
| TICKET-005 | Fix Backtest Strategy Mismatch | High | — | — |
| TICKET-022 | AI Trade UI Consolidation | High | — | — |
| TICKET-016 | Fix Tab Switching Process Persistence | High | — | — |
| TICKET-006-TODO-001 | Support multiple bar timeframes in backtest | Low | Claude Haiku | — |

### TICKET-006-TODO-001 — Multiple Bar Timeframe Support
**Priority:** Low (UI control exists, functionality deferred)
**Assigned To:** Claude Haiku
**Effort:** 3 tokens
**Description:** Make Interval selector actually control bar timeframe (currently hardcoded to 5-min)

**Scope:**
1. BarCollectionService: Add timeframe parameter to `EnsureBarsAsync()`
2. BacktestViewModel: Pass selected interval to bar download
3. Update API calls to use selected timeframe (not hardcoded)
4. SQLite bars table already supports multiple timeframes

**Acceptance:** User selects any interval → bars downloaded at that timeframe → backtest runs on selected timeframe

| TICKET-017 | Implement Complete Stop on Stop Button | High | — |
| TICKET-018 | Cancel Monitoring on Rejected Trade | Medium | — |
| TICKET-011 | AI Trade Confidence Selector | Medium | TICKET-006 |
| TICKET-007 | Multi-Strategy Concurrent Trading | Medium | TICKET-006 |
| TICKET-020 | Economic Calendar Filter | Medium | — |
| TICKET-009 | Trailing Take Profit Implementation | Low | — |
| TICKET-010 | Fix Stop Loss Hardcoding Bug | Low | — |
| TICKET-003 | Market Data Real-Time Streaming | Low | — |
| TICKET-004 | Risk Guard Position Sizing | Low | — |

---

## ✅ Completed This Session (2026-03-02)

- **TICKET-023** — Add Confidence Score to AI Trade Page ✅ (0 errors, 0 warnings)
  - Added `MinConfidencePct` (default 75) to `StrategyDefinition.vb`
  - Added `MinConfidencePct` property (clamped 0–100) to `AiTradingViewModel.vb`
  - Squeezed Capital/Qty/TP/SL TextBoxes; added inline Confidence % TextBox in `AiTradingView.xaml`
  - `StrategyExecutionEngine.vb`: replaced hardcoded 60 threshold → `_strategy.MinConfidencePct`
  - `ExecuteStart()`: assigns `_currentStrategy.MinConfidencePct = _minConfidencePct`

- **TICKET-024** — AI Trade Strategy Summary + Recommended Defaults ✅ (0 errors, 0 warnings)
  - Added `StrategyNakedDescription` + `HasStrategyDescription` properties to `AiTradingViewModel.vb`
  - `ApplyEmaRsiCombined()` now auto-fills TP=40, SL=20, Qty=1 (strategy-optimal defaults)
  - `ApplyEmaRsiCombined()` now sets 186-word Naked Trader prose description
  - Added Panel 2b ("WHAT THIS STRATEGY DOES") to `AiTradingView.xaml` — hidden until strategy selected
  - Spec: `GitHub_Tickets/TICKET-024_AI_Trade_Strategy_Summary_and_Recommendations.md`

## ✅ Completed Previous Session (2026-03-01)

- **TICKET-014** — Redesign AI Trade Layout → `GitHub_Tickets/Completed_Tickets/`
- **TICKET-019** — ComboBox Font Fix (white text pattern) → `GitHub_Tickets/Completed_Tickets/`
- **TICKET-021** — Default Practice Account Display → `GitHub_Tickets/Completed_Tickets/`
- **TICKET-006 Phases 1–4** — Backtest Page Complete Rewrite
- **TICKET-006 Session 2 UAT** — Button reorder, contract ID fix (H26→K26), Interval selector

---

## 🔑 Key Design Decisions (do not revert)

### Combined Strategies Only (TICKET-006)
Single-indicator strategies (pure RSI, pure EMA, RSI Reversal, Double Bottom, etc.) are **excluded**
from backtesting. Only combined multi-indicator strategies produce reliable live trading signals.
- `AvailableStrategies` contains only `"EMA/RSI Combined"` (Strategy #1)
- `_strategyDefaults` has only one entry: `{"EMA/RSI Combined", ("50000", "4", "20", "10")}`
- Future strategies must be combined (e.g., "EMA/MACD Combined", "RSI/BB Combined")

### ContractSelectorControl (TICKET-021 / TICKET-019)
Self-contained — hardcoded 4 contracts in code-behind. No VM `AvailableContracts` needed.
Exposes single DP: `ContractId` (string, long-form e.g. `CON.F.US.MES.H26`).

### WPF White Text ComboBox
Use `<ComboBox.ItemTemplate><DataTemplate><TextBlock Foreground="White"/></DataTemplate></ComboBox.ItemTemplate>`
+ `Style="{StaticResource UnclippedComboBoxStyle}"` + `ItemContainerStyle="{StaticResource DarkComboBoxItemStyle}"`

---

## 🐛 Known Bugs (not yet ticketed)

- "TestTrade" nav button in sidebar has NO case in `NavigateTo()` — clicking does nothing
  (Test Trade lives as Tab 3 inside BacktestView as a workaround)
