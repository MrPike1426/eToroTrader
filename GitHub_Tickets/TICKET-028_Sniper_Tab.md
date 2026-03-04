# TICKET-028 — Sniper Tab (3-EMA Cascade + Scaling Position)

## Status
In Progress — 2026-03-02

## Summary
Add a new "🎯 Sniper" sidebar view for fast 1-minute momentum trading.
Implements the **3-EMA Cascade** strategy (EMA8/EMA21/EMA50 on 1-min bars) with
**pyramiding scale-in** up to 10 contracts and an automatic **free-ride SL** (breakeven
protection) once 3+ positions are open.

## Strategy: 3-EMA Cascade

### Entry (Initial Signal)
**LONG (pump):**
- EMA8 crosses above EMA21 (prev bar: EMA8 ≤ EMA21, curr: EMA8 > EMA21)
- Price > EMA50 (uptrend confirmation)
- EMA50 rising (EMA50_curr > EMA50_prev)

**SHORT (dump):**
- EMA8 crosses below EMA21
- Price < EMA50
- EMA50 falling

### Scale-In (every 30s poll while position open, qty < 10)
- EMA8 still above/below EMA21 (momentum holds)
- Price moved ≥ ScaleInTriggerTicks further from last entry price

After each scale-in:
- Recalculate AverageEntry = weighted mean of all fill prices
- Cancel existing TP/SL bracket orders
- Place new TP Limit and SL StopLimit for total qty at new average entry ±ticks

### Free-Ride SL
Once qty ≥ 3: move SL to AverageEntry (breakeven). Sets `_freeRideActive = True`.

### Defaults
- TP: 10 ticks, SL: 5 ticks, ScaleIn trigger: 5 ticks, Duration: 2 hours
- Timeframe: 1-minute bars

## Files Created
- `src/TopStepTrader.Services/Trading/SniperExecutionEngine.vb`
- `src/TopStepTrader.UI/Views/SniperView.xaml`
- `src/TopStepTrader.UI/Views/SniperView.xaml.vb`
- `src/TopStepTrader.UI/ViewModels/SniperViewModel.vb`

## Files Modified
- `src/TopStepTrader.Core/Enums/StrategyConditionType.vb` — TripleEmaCascade = 7
- `src/TopStepTrader.Core/Enums/StrategyIndicatorType.vb` — TripleEma = 5
- `src/TopStepTrader.Core/Trading/StrategyDefaults.vb` — 3-EMA Cascade defaults
- `src/TopStepTrader.Core/Interfaces/IBacktestService.vb` — StrategyCondition added to BacktestConfiguration
- `src/TopStepTrader.Services/Backtest/BacktestEngine.vb` — TripleEmaCascade backtest branch
- `src/TopStepTrader.UI/Infrastructure/AppBootstrapper.vb` — Register SniperView + SniperViewModel
- `src/TopStepTrader.UI/ViewModels/ViewModelLocator.vb` — SniperView property
- `src/TopStepTrader.UI/Views/MainWindow.xaml` — 🎯 Sniper sidebar button
- `src/TopStepTrader.UI/Views/MainWindow.xaml.vb` — Case "Sniper" in NavigateTo()

## Testing Checklist
- [ ] Build: 0 errors, 0 warnings
- [ ] "🎯 Sniper" appears in sidebar and navigates to SniperView
- [ ] Account ComboBox text is white
- [ ] Contract selector populates correctly
- [ ] Live tab: Start Sniper → log shows "Monitoring 1-min bars…" poll messages
- [ ] Live tab: scale-in log event fires when conditions are met
- [ ] Live tab: free-ride log event fires at qty ≥ 3
- [ ] Backtest tab: Run → results populate (trade count > 0 on volatile day)
- [ ] Backtest tab: Results grid shows entries with correct direction
