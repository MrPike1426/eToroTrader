# TICKET-022 — AI Trade Tab UI Consolidation and Layout Optimization

| Field | Value |
|-------|-------|
| **Ticket ID** | TICKET-022 |
| **Status** | Ready |
| **Priority** | Low |
| **Attempts** | 0 |
| **Created** | 2026-03-01 |
| **Last Updated** | 2026-03-01 |
| **Assigned To** | Copilot |
| **Proposed Model** | Claude Haiku 4.5 |

---

## Problem Statement

The current AI Trade layout has redundant visual elements and awkward spacing that reduces clarity during live trading. The Confidence Check panel is separated with its own scrolling text box, creating visual clutter. The Start/Stop buttons are in the control bar at the bottom, requiring users to look away from the main trading area. Font sizes in output windows are too small for quick readability.

**User workflow should be linear and visual**:
1. Choose **Account** (pre-selected Practice)
2. Choose **Contract**
3. Click **Strategy** (one-click activate)
4. Click **Confidence Check** (optional market context)
5. Click **▶ Start Monitoring** (begins execution)
6. View all output in a single scrolling pane (Confidence → Monitoring logs)

---

## Required Changes

### 1. Consolidate Confidence Check and Monitoring Output

**Current**: Confidence Check has its own TextBox with `MaxHeight="100"`.
**New**: Remove the separate Confidence Check TextBox. Instead:
- Show Confidence Check result inline (same TextBox as monitoring output)
- When user clicks "Check Confidence", append result to a shared output log
- When monitoring starts, continue appending to the same log
- Single unified scrolling window for all output

### 2. Move Start/Stop Buttons to Panel 1, Row 2

**Current location**: Bottom control bar (Grid.Row="1").
**New location**: Panel 1 (Settings), same row as Capital/Qty/TP/SL fields.

**Layout**:
```
Row 1: Account ComboBox | Contract ComboBox | Contract ID TextBox  [unchanged]
Row 2: Capital/Qty | TP/SL | [▶ Start Monitoring] [■ Stop] [● Status]
```

- Buttons should be right-aligned in Row 2
- Status label (e.g., "● Idle", "● Running") placed between or after Stop button
- Maintains compact look without adding extra rows

### 3. Increase Output Font Size to 12

**Current**: `FontSize="11"` in Confidence Check and monitoring log boxes.
**New**: `FontSize="12"` for better readability during live trading.

---

## UI Structure After Changes

```
Panel 1: Account / Contract / Risk
├─ Row 1: Account CB | Contract CB | Contract ID TB
└─ Row 2: Capital/Qty | TP/SL | [▶ Start] [■ Stop] [Status]

Panel 2: Strategy Selection
├─ Header
├─ 4 Strategy Buttons (EMA/RSI active + 3 In Dev)
└─ Active Strategy Indicator Card

Panel 3: Unified Output Log
├─ Header "Output" or "Monitoring & Analysis"
├─ Single TextBox (FontSize=12):
│  ├─ Confidence Check results (appended when user clicks button)
│  └─ Monitoring execution logs (appended when engine runs)
└─ Auto-scroll to latest (newest entries at top)

[Bottom Control Bar removed or simplified]
```

---

## Files Affected

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Restructure Panel 1 Row 2, consolidate Confidence/Output panels, remove Confidence TextBox, add Start/Stop to Panel 1 |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Modify `ExecuteGetConfidence()` to append to shared log instead of replacing ConfidenceText; ensure monitoring logs append same way |

---

## Acceptance Criteria

- [ ] Start Monitoring and Stop buttons appear in Panel 1, Row 2 (right-aligned with Capital/Qty/TP/SL).
- [ ] Status label displays "● Idle" / "● Running" near the buttons.
- [ ] Confidence Check results are appended to the shared output log (not a separate TextBox).
- [ ] Monitoring logs append to the same output log after Confidence Check results.
- [ ] Single unified scrolling text box with `FontSize="12"`.
- [ ] Layout is compact — no extra vertical space.
- [ ] No regression to existing button/combobox styling or bindings.

---

## Notes

- The shared output log should use `ObservableCollection(Of String)` (already exists as `LogEntries`).
- Confidence Check result should be wrapped with a header like `"--- Confidence Check ---"` for clarity.
- Monitoring logs already insert at position 0 (newest first); keep same pattern.
- Bottom control bar (currently containing Start/Stop/Status) can be removed entirely OR simplified to just the execution log.
