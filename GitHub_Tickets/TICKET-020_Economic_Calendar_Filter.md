# TICKET-020 — Economic Calendar Filter for AI Trade

| Field | Value |
|-------|-------|
| **Ticket ID** | TICKET-020 |
| **Status** | Backlog |
| **Priority** | Medium |
| **Attempts** | 0 |
| **Created** | 2026-03-01 |
| **Last Updated** | 2026-03-01 |
| **Assigned To** | Copilot |

---

## Problem Statement

The EMA/RSI Combined strategy (TICKET-014) runs continuously during the session window.
However, experienced traders know that high-impact economic data releases create temporary
volatility spikes that generate false signals and increase risk of adverse fills. Additionally,
the 2–4 AM UTC window has low volume and high noise for US micro-futures.

---

## Required Behaviour

When the strategy engine is running, suppress entry signals (do not place orders) under these conditions:

### 1. Pre-release blackout (30 minutes before each event)
Skip entry signals within **30 minutes before** any of these events:
- NFP (Non-Farm Payrolls) — first Friday of each month, 13:30 UTC
- CPI (Consumer Price Index) — monthly, 13:30 UTC
- FOMC/Fed Rate Decision — ~8 per year, 18:00 UTC
- PPI (Producer Price Index) — monthly, 13:30 UTC
- GDP (advance/preliminary) — quarterly, 13:30 UTC

### 2. Low-volume window block
Skip entry signals between **02:00 UTC and 04:00 UTC** (lowest liquidity period for US micro-futures).

### 3. Logging
When a signal fires but is suppressed by a filter, log the suppression reason:
```
⏸ Signal suppressed — NFP release in 24 minutes (13:30 UTC)
⏸ Signal suppressed — low-volume window (02:17 UTC)
```

---

## Design Options

### Option A — Hardcoded calendar (simple, offline)
Maintain a static list of known release dates/times in a config file or constants class.
Update quarterly. No external API required.

### Option B — Live calendar API
Integrate with a free economic calendar API (e.g., Forex Factory, Trading Economics, FRED).
Requires API key, internet access, and error handling for unavailability.
Higher maintenance cost.

**Recommendation**: Start with Option A. Add Option B as a future enhancement.

---

## Implementation Notes

- Filter logic lives in `StrategyExecutionEngine.DoCheckAsync()`, checked **before** evaluating indicators.
- Add a `IsInBlackoutWindow(utcNow, strategy)` helper — returns `(blocked As Boolean, reason As String)`.
- A `CalendarFilter` class in `TopStepTrader.Services.Trading` handles the date/time logic.
- Allow enable/disable via a toggle in `TradingSettings` (appsettings.json).

---

## Acceptance Criteria

- [ ] No entry orders placed within 30 minutes before a scheduled high-impact US event.
- [ ] No entry orders placed between 02:00–04:00 UTC.
- [ ] Each suppression is logged with reason and time remaining.
- [ ] Filter can be disabled via settings (default: enabled).
- [ ] Unit tests for the calendar filter helper.

---

## Progress Tracking

### Phase 1: Design & Research
- [ ] Economic calendar event dates for 2026 documented
- [ ] CalendarFilter class structure designed
- [ ] IsInBlackoutWindow() helper designed
- [ ] TradingSettings configuration option planned

### Phase 2: Implementation
- [ ] CalendarFilter.vb created with hardcoded calendar
- [ ] IsInBlackoutWindow() method implemented
- [ ] StrategyExecutionEngine.DoCheckAsync() filter integration
- [ ] TradingSettings toggle added
- [ ] Logging with suppression reason added

### Phase 3: Testing & Validation
- [ ] Unit tests (calendar filter logic, time boundaries)
- [ ] Integration tests (filter prevents entry signals)
- [ ] Manual testing (verify suppression logging)
- [ ] Edge case testing (time boundary crossings)

### Phase 4: Enhancement (Future)
- [ ] Live calendar API integration (Option B)
- [ ] Auto-update for calendar events

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (Backlog - lower priority)
**Blocker:** None (TICKET-014 complete)
**Next Concrete Action:** Document 2026 economic calendar dates

---

## Dependencies

- TICKET-014 (EMA/RSI Combined strategy — prerequisite, complete)
