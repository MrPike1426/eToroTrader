# TICKET-025: AI Trade Performance Panel

**Status:** ✅ Complete
**Priority:** High
**Severity:** High
**Assigned To:** Claude Sonnet 4.6
**StartDate:** 2026-03-02
**Completed:** 2026-03-02
**Tokens:** 6 (estimate) / 18 (actual — includes UAT amendments)
**Labels:** `feature,ai-trade,ui-enhancement,trade-history,performance,complete`

---

## Delivered

### New files
| File | Purpose |
|------|---------|
| `UI/ViewModels/TradeRowViewModel.vb` | Row model: `IsInProgress`, `Result`, `ResultForeground`, `ExternalOrderId`, `TradeIdDisplay`, `Close(exitReason, pnl)` |
| `Core/Events/TradeOpenedEventArgs.vb` | Raised when entry order is placed (Side, ContractId, ConfidencePct, EntryTime, ExternalOrderId) |
| `Core/Events/TradeClosedEventArgs.vb` | Raised when position closes (ExitReason, PnL) |

### Modified files
| File | Change |
|------|--------|
| `Core/Models/StrategyDefinition.vb` | Added `TickValue As Decimal` |
| `Services/Trading/StrategyExecutionEngine.vb` | `TradeOpened`/`TradeClosed` events; `_lastTpExternalId`, `_lastTpPrice`, `_lastSlPrice`, `_pendingConfidencePct` fields; bracket order tracking |
| `UI/ViewModels/AiTradingViewModel.vb` | `TradeRows`, `HasTradeRows`, `HasActivePosition`, `HasConfidenceResult`, `HasNoResults`, `StrategyDescriptionPanelVisible`; event handlers; CollectionChanged wiring |
| `UI/Views/AiTradingView.xaml` | RESULTS panel (Panel 3) with DataGrid + pulsing Ellipse + confidence TextBox + placeholder; TX Order column; Check Confidence moved to control bar; monitoring log in scrollable area; `*/Auto` row split |

### Behaviour
- Trade fires → row appears immediately with `🟠 ⏳ In Progress...` and pulsing amber dot
- TP fills → `TP  +$NNN  ✅` in green
- SL fills → `SL  -$NNN  ❌` in red
- Strategy description collapses when monitoring starts; re-appears after stop
- Table clears when monitoring restarts
- TX Order column shows TopStepX entry order ExternalOrderId

**Build:** ✅ 0 errors, 0 warnings

## Problem Statement

The AI Trade page has a scrolling log panel that shows real-time monitoring output (bar checks,
signals, order placements), but there is **no structured record of trade outcomes**. Over a
session lasting one hour or more, the user cannot easily see:

- How many trades were placed
- Whether each trade hit TP or SL
- The P&L of each trade
- Which direction (Long/Short) was taken
- What confidence level triggered the trade

All of this is buried in the scrolling text log with no at-a-glance summary.

**Current Limitations:**
- ❌ No structured trade history visible on AI Trade page
- ❌ No indicator showing whether a position is currently open
- ❌ "What this strategy does" description text stays visible even while monitoring is active
  (wastes screen space that could show trade history)
- ❌ P&L per trade not visible without querying the database

---

## Desired Outcome

The bottom panel of the AI Trade page is restructured into two sections:

**Section 1 (top of panel) — Trade Performance Table:**
```
[🔴 Trade In Progress]                         ← pulsing dot, inline with open trade row
╔═══════════════╦══════════╦════════╦══════╦═══════════════════╗
║ Date / Time   ║ Contract ║ Conf % ║ Side ║ Result            ║
╠═══════════════╬══════════╬════════╬══════╬═══════════════════╣
║ 02/03 11:30   ║ M.Gold   ║  82%   ║ SELL ║ ⏳ In Progress... ║  ← pulsing row
║ 02/03 10:10   ║ M.Gold   ║  79%   ║ BUY  ║ TP  +$127  ✅     ║  ← green
║ 01/03 14:22   ║ M.Gold   ║  81%   ║ SELL ║ SL   -$63  ❌     ║  ← red
╚═══════════════╩══════════╩════════╩══════╩═══════════════════╝
```

**Section 2 (below table) — Existing scrolling log:**
- Unchanged from current behaviour
- "What this strategy does" text is cleared from this section once monitoring starts
  (strategy description is no longer needed once the engine is running)

---

## Requirements

### A. Trade Performance Table

**Columns:**

| Column | Content | Example |
|--------|---------|---------|
| **Date / Time** | Entry time, format `DD/MM HH:mm` | `02/03 11:30` |
| **Contract** | Friendly short name (see mapping below) | `M.Gold` |
| **Conf %** | Signal confidence that triggered the trade | `82%` |
| **Side** | `BUY` or `SELL` | `SELL` |
| **Result** | See result format spec below | `TP  +$127  ✅` |

**Contract Friendly Name Mapping (hardcoded — see TICKET-026 for API-driven version):**

| ContractId (contains) | Display Name |
|-----------------------|-------------|
| `MGC` | `M.Gold` |
| `MNQ` | `M.Nasdaq` |
| `MCL` | `M.Oil` |
| `MES` | `M.S&P` |
| *(fallback)* | First 6 chars of ContractId |

**Result Column Format:**

| State | Display | Colour |
|-------|---------|--------|
| Trade open | `⏳ In Progress...` | White / neutral |
| TP filled | `TP  +$127  ✅` | **Green** |
| SL filled | `SL   -$63  ❌` | **Red** |
| Unknown close | `Closed` | Grey |

P&L is formatted as `+$NNN` or `-$NNN` rounded to whole dollars.

**Behaviour:**
- **New entries appear at the top** (newest first) — the current in-progress row is always row 1
- When monitoring starts, previous session rows are **cleared** (fresh session = clean table)
- All trades from the current session are retained (no row limit)
- Table is read-only (no click actions required in this ticket)

---

### B. "Trade In Progress" Pulsing Dot

- A small coloured dot is placed **inline at the left of the active trade row** in the Result column
- When `_positionOpen = True`: dot is **visible and pulsing** (CSS-style opacity animation, ~1 second cycle)
- When no position is open: dot is **hidden**
- Colour: **Amber/Orange** (`#FF8C00`) — distinct from the green (TP) and red (SL) result colours
- Size: 10×10px circle
- Animation: WPF `DoubleAnimation` on `Opacity` from 1.0 → 0.2 → 1.0, `RepeatBehavior="Forever"`

---

### C. Strategy Description Text — Clear on Monitoring Start

**Current behaviour:**
- When user selects a strategy, a text block shows "What this strategy does" description
- This text remains visible even after monitoring starts

**New behaviour:**
- When the user clicks **Start Monitoring**, the strategy description text is **cleared** (or
  collapsed) and the trade table takes its place at the top of the panel
- If monitoring is stopped and the user re-selects a strategy, the description reappears

**Rationale:** During active monitoring, the strategy description adds no value. The screen
space is better used showing live trade performance data.

---

### D. Data Source for Trade Results

The trade table is populated from two sources:

**1. New trades (placed this session):**
- `StrategyExecutionEngine` raises a `LogMessage` event when orders are placed
- The ViewModel already subscribes to `LogMessage` and writes to the scrolling log
- Extend this: when a new entry order fires, add a row to `TradeRows` collection with
  `Result = "⏳ In Progress..."`

**2. Position close detection (already implemented — UAT-BUG-007 fix):**
- `DoCheckAsync` now logs `"✓ Position closed — bracket orders no longer active"`
- Extend this: when position closes, update the top row's Result column with TP or SL outcome
  - To determine TP vs SL: check `GetOpenOrdersAsync` — whichever bracket order is now
    `Filled` (Status=2) tells us which one triggered
  - Calculate P&L from the filled order's fill price vs entry price

**Implementation note:** The engine does not currently expose TP/SL fill details via events.
The simplest approach is to poll `GetOrderHistoryAsync` or check the filled bracket orders
when the position-close condition is detected. This is consistent with the existing polling model.

---

## UI Layout (Wireframe)

```
┌─────────────────────────────────────────────────────────────┐
│  AI Trade Tab                                               │
│ ─────────────────────────────────────────────────────────── │
│  [Contract ▼]  [Strategy ▼]   [Account ▼]                  │
│  Capital: [50000]  Qty: [4]  Confidence: [75%]             │
│  TP: [...]  SL: [...]                                       │
│  [Start Monitoring]  [Stop]                                 │
│ ─────────────────────────────────────────────────────────── │
│  TRADE HISTORY                                              │
│ ┌──────────────┬──────────┬───────┬──────┬────────────────┐ │
│ │ Date/Time    │ Contract │ Conf% │ Side │ Result         │ │
│ ├──────────────┼──────────┼───────┼──────┼────────────────┤ │
│ │ 02/03 11:30  │ M.Gold   │  82%  │ SELL │ 🟠⏳ In Prog.. │ │
│ │ 02/03 10:10  │ M.Gold   │  79%  │ BUY  │ TP +$127 ✅    │ │
│ └──────────────┴──────────┴───────┴──────┴────────────────┘ │
│                                                             │
│  MONITORING LOG                              ↑ scrollable  │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ 11:30:08  ✅ EMA/RSI weighted: UP=82% — LONG signal!   │ │
│ │ 11:30:07  Entry SELL order placed — Market, qty=1      │ │
│ │ 11:29:37  Bar checked — EMA21=5401.2 EMA50=5398.4 ...  │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## Implementation Plan

### Phase 1: ViewModel — TradeRow Model & Collection (1 token)

1. **Create `TradeRowViewModel` class** (or record) with properties:
   - `EntryTime As DateTimeOffset`
   - `ContractDisplay As String` (friendly name, mapped from ContractId)
   - `ConfidencePct As Integer`
   - `Side As String` (`"BUY"` / `"SELL"`)
   - `Result As String` (bound to Result cell text)
   - `ResultColour As Brush` (Green / Red / White)
   - `IsInProgress As Boolean` (drives pulsing dot visibility)

2. **Add to `AiTradingViewModel`:**
   - `TradeRows As ObservableCollection(Of TradeRowViewModel)` — bound to DataGrid
   - `HasActivePosition As Boolean` — drives pulsing dot visibility at session level
   - `AddTradeRow(side, contractId, confidence)` — called when entry fires
   - `CloseLastTradeRow(exitReason, pnl)` — called when position close detected

3. **Contract friendly name helper:**
   ```vb
   Friend Shared Function ToFriendlyName(contractId As String) As String
       If contractId.Contains("MGC") Then Return "M.Gold"
       If contractId.Contains("MNQ") Then Return "M.Nasdaq"
       If contractId.Contains("MCL") Then Return "M.Oil"
       If contractId.Contains("MES") Then Return "M.S&P"
       Return contractId.Substring(0, Math.Min(6, contractId.Length))
   End Function
   ```

### Phase 2: Engine Events — Expose Trade Entry & Close Data (2 tokens)

1. **New event on `StrategyExecutionEngine`:**
   ```vb
   Public Event TradeOpened As EventHandler(Of TradeOpenedEventArgs)
   ' Args: Side, ContractId, ConfidencePct, EntryTime
   ```

2. **New event on `StrategyExecutionEngine`:**
   ```vb
   Public Event TradeClosed As EventHandler(Of TradeClosedEventArgs)
   ' Args: ExitReason ("TP"/"SL"/"Manual"), PnL As Decimal
   ```

3. **Raise `TradeOpened`** in `PlaceBracketOrdersAsync` immediately after entry order accepted.

4. **Raise `TradeClosed`** in `DoCheckAsync` when `_positionOpen` resets to `False`:
   - Check which bracket order filled (poll `GetOpenOrdersAsync` — the one now absent was filled)
   - Calculate P&L: `(fillPrice - entryPrice) * tickValue * quantity` (adjusted for side)
   - Raise event with `ExitReason` and `PnL`

5. **`AiTradingViewModel`** subscribes to both events, calls `AddTradeRow` / `CloseLastTradeRow`

### Phase 3: UI — Trade Table DataGrid & Pulsing Dot (2 tokens)

1. **DataGrid** bound to `TradeRows`:
   - Columns: Date/Time, Contract, Conf%, Side, Result
   - Result column: `DataTrigger` on `IsInProgress` to show pulsing dot animation
   - Row background: driven by `ResultColour` (transparent for in-progress, subtle tint for closed)
   - No selection / no row click needed
   - `AutoGenerateColumns=False`, `IsReadOnly=True`, `CanUserSortColumns=False`

2. **Pulsing dot** (WPF animation):
   ```xml
   <Ellipse Width="10" Height="10" Fill="#FF8C00"
            Visibility="{Binding IsInProgress, Converter={StaticResource BoolToVisibility}}">
       <Ellipse.Triggers>
           <EventTrigger RoutedEvent="Loaded">
               <BeginStoryboard>
                   <Storyboard RepeatBehavior="Forever" AutoReverse="True">
                       <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                        From="1.0" To="0.2" Duration="0:0:0.8"/>
                   </Storyboard>
               </BeginStoryboard>
           </EventTrigger>
       </Ellipse.Triggers>
   </Ellipse>
   ```

3. **Strategy description collapse:**
   - `StrategyDescriptionVisibility` property: `Visible` when not monitoring, `Collapsed` when `IsMonitoring = True`
   - Bind existing strategy description TextBlock's `Visibility` to this property

### Phase 4: P&L Calculation (1 token)

The engine does not currently store entry price after placing the bracket order. Add:

1. **`_lastEntryPrice As Decimal`** field on `StrategyExecutionEngine`
2. Set in `PlaceBracketOrdersAsync`: `_lastEntryPrice = lastClose` (same value used for TP/SL calc)
3. On position close, retrieve the filled bracket order's `FillPrice` via `TryGetOrderFillPriceAsync`
4. P&L formula:
   ```vb
   Dim rawPnl = If(side = OrderSide.Buy,
                   (fillPrice - _lastEntryPrice) * tickValue * qty,
                   (_lastEntryPrice - fillPrice) * tickValue * qty)
   ```
5. `tickValue` comes from `_strategy.TickValue` (already on the strategy model)

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Core/Events/TradeOpenedEventArgs.vb` | NEW |
| `src/TopStepTrader.Core/Events/TradeClosedEventArgs.vb` | NEW |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Add events + _lastEntryPrice |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add TradeRows, AddTradeRow, CloseLastTradeRow |
| `src/TopStepTrader.UI/ViewModels/TradeRowViewModel.vb` | NEW |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Add DataGrid + pulsing dot + collapse description |

---

## Acceptance Criteria

- [ ] Trade table appears at the top of the AI Trade bottom panel
- [ ] New trades appear as row 1 with `⏳ In Progress...` result and pulsing amber dot
- [ ] When TP fills: row updates to `TP  +$NNN ✅` in green, dot disappears
- [ ] When SL fills: row updates to `SL  -$NNN ❌` in red, dot disappears
- [ ] Contract shows friendly name (`M.Gold`, `M.Nasdaq`, etc.)
- [ ] Confidence % shows the signal confidence that triggered the trade
- [ ] Strategy description text is hidden/collapsed once monitoring starts
- [ ] Table clears when monitoring is restarted
- [ ] Existing scrolling log is unchanged
- [ ] Build: 0 errors, 0 warnings
- [ ] No regression on other tabs

---

## Related Tickets

- **TICKET-026:** Contract Friendly Names from API *(unblocks richer display names)*
- **UAT-BUG-007:** `_positionOpen` reset fix *(prerequisite — already fixed)*
- **UAT-BUG-008:** SL order type fix *(prerequisite — already fixed)*
- **TICKET-022:** AI Trade UI Consolidation *(layout changes may overlap — check for conflicts)*

---

## Notes for Implementer

- The pulsing dot uses a WPF `Storyboard` with `RepeatBehavior="Forever"` — no code-behind needed
- `TradeRows` should be an `ObservableCollection` — UI binds directly, no `OnPropertyChanged` needed for individual rows
- `TradeRowViewModel` properties that change (Result, ResultColour, IsInProgress) **must** raise `PropertyChanged`
- P&L calculation requires `TickValue` on the strategy — verify this field exists on `StrategyConfiguration` before starting Phase 4
- The `TryGetOrderFillPriceAsync` method already exists on `IOrderService` (added for UAT-BUG-007 investigation) — use it to get the actual fill price

**Created:** 2026-03-02
**Last Updated:** 2026-03-02
**Model:** GitHub Copilot
