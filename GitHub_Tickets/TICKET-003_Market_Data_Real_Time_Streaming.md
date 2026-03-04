# TICKET-003: Market Data Real-Time Streaming

**Status:** Backlog
**Priority:** Low
**Severity:** Low
**Assigned To:** Copilot
**Due Date:** 15/05/2026
**Tokens:** 12
**Labels:** `feature,future,market-data,streaming`

---

## Problem Statement

Currently, the **Market Data tab** shows static or delayed market data. Users need **real-time bid/ask spreads, order book depth, and live market information** to make informed trading decisions.

**Business Value:**
- Provides market context during trading decisions
- Shows liquidity (bid/ask spread)
- Displays order book depth (support/resistance levels)
- Helps users understand market conditions before entering trades

**Current State:**
- Market Data tab exists but has placeholder or minimal data
- No live streaming integration
- No bid/ask depth visualization

---

## Requirements

### A. Real-Time Data Streaming

Implement WebSocket or polling-based connection to market data source:

```
Features:
- Connect to TopStep API (or external market data provider)
- Stream bid/ask prices in real-time
- Update display every 100-500ms
- Handle connection loss gracefully (reconnect with backoff)
- Display last update timestamp
```

### B. Order Book Display

Show market depth (support/resistance):

```
Format:
  Bid Side          |  Spread  |  Ask Side
  ────────────────────────────────────
  Price | Qty       |          | Price | Qty
  ────────────────────────────────────

Example (MES):
  4532.50 | 250    |  0.50    | 4532.75 | 150
  4532.25 | 180    |          | 4533.00 | 300
  4532.00 | 420    |          | 4533.50 | 200

Show 5-10 levels on each side (configurable)
```

### C. Bid/Ask Spread Indicator

Visual indicator of market liquidity:

```
Spread = Ask - Bid
Tight spread (< 0.50) → Green (liquid)
Medium spread (0.50 - 1.00) → Yellow
Wide spread (> 1.00) → Red (illiquid)

Display: "Spread: 0.50 (Green) | Last: 4532.75"
```

### D. Volume & VWAP

Optional enhancements (Phase 2):

```
- Cumulative volume at each price level
- Volume-weighted average price (VWAP)
- Bid/Ask volume ratio
- Large order alerts (> 500 contracts)
```

---

## Implementation Plan

### Phase 1: Research & Design (2 tokens)

1. **Investigate TopStep API:**
   - Does API provide real-time WebSocket market data?
   - Rate limits for streaming?
   - Data latency (milliseconds)?
   - Authentication requirements?

2. **Fallback Research:**
   - If TopStep doesn't support streaming, research alternatives:
     - Alpaca API (free tier)
     - IB TWS API
     - Polygon.io
     - Finnhub

3. **Design Market Data View:**
   - Layout: Order book left, charts right?
   - Refresh rate: 100ms, 200ms, or 500ms?
   - WPF control for depth visualization (DataGrid, custom control?)

### Phase 2: Implementation (7 tokens)

1. **MarketDataService.vb:**
   - Create WebSocket connection manager
   - Subscribe to bid/ask stream
   - Handle reconnection logic
   - Implement IDisposable for cleanup

2. **MarketDataViewModel.vb:**
   - Observable collections for bid/ask levels
   - Real-time price binding
   - Spread calculation
   - Timestamp tracking

3. **MarketDataView.xaml:**
   - Order book DataGrid (sortable by price)
   - Spread indicator (color-coded)
   - Connection status indicator
   - Refresh rate selector (if configurable)

4. **Error Handling:**
   - Connection loss → show "Reconnecting..." message
   - Stale data → highlight in gray after 5 seconds without update
   - API errors → display user-friendly message

### Phase 3: Testing & Validation (3 tokens)

1. **Unit Tests:**
   - Mock API responses
   - Test spread calculation
   - Test reconnection logic

2. **Integration Tests:**
   - Connect to live API
   - Verify data updates in real-time
   - Test connection loss/recovery
   - Stress test: 1000+ price updates/sec

3. **User Acceptance:**
   - Verify data accuracy vs. broker platform
   - Check latency (is real-time fast enough?)
   - Test on low-bandwidth connection

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/MarketData/MarketDataService.vb` | NEW - WebSocket connection & data streaming |
| `src/TopStepTrader.Services/MarketData/MarketDataViewModel.vb` | NEW - Real-time bid/ask binding |
| `src/TopStepTrader.UI/Views/MarketDataView.xaml` | UPDATE - Add order book + spread display |
| `src/TopStepTrader.UI/Views/MarketDataView.xaml.vb` | NEW - View logic |
| `src/TopStepTrader.API/Responses/MarketDataResponse.vb` | NEW - Response DTOs |

---

## Acceptance Criteria

- [ ] Real-time bid/ask prices stream to UI every 500ms or faster
- [ ] Order book displays 5-10 levels on both sides (configurable)
- [ ] Spread calculated and color-coded (green/yellow/red)
- [ ] Connection status visible (Connected/Reconnecting/Disconnected)
- [ ] Reconnection works automatically after 3-second timeout
- [ ] Data doesn't go stale (timestamps update in real-time)
- [ ] UI is responsive (no freezing during high-frequency updates)
- [ ] Handles 10+ contracts simultaneously without lag
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: Other tabs still function normally

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Real-time streaming (vs polling every 1sec)** | More responsive ✅ | Higher bandwidth ❌ |
| **WebSocket (vs REST polling)** | Lower latency ✅ | More complex ❌ |
| **Show 10 levels (vs 20)** | Clearer view ✅ | Less market depth ❌ |
| **Update every 500ms (vs 100ms)** | Good responsiveness ✅ | Slight lag vs broker ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **API doesn't support streaming** | Research alternatives (Alpaca, IB, Polygon) early in Phase 1 |
| **High latency from API** | Implement client-side caching + interpolation |
| **Memory leak with high update frequency** | Test with profiler, implement disposal properly |
| **Spread calculation inaccuracy** | Compare against broker's spread, log discrepancies |

---

## Related Tickets

- **TICKET-020:** Economic Calendar Filter — Context for when to trade
- **TICKET-009:** Trailing TP — Uses market price from this feature
- **TICKET-011:** Confidence Selector — Complements market context

---

## Next Steps

### Immediate (After Approval):

1. **Research API capabilities** (1 day)
   - Contact TopStep support or review API docs
   - Confirm WebSocket support for market data
   - Document rate limits and latency specs

2. **Design Market Data View** (1 day)
   - Create wireframe (order book layout)
   - Define data structures (BidLevel, AskLevel)
   - Plan error handling (connection loss, stale data)

3. **Spike: POC WebSocket Connection** (1-2 days)
   - Build minimal WebSocket client
   - Receive first price update
   - Prove latency acceptable

### During Implementation:

1. Phase 1 (Research) — 2 days
2. Phase 2 (Implementation) — 3-4 days
3. Phase 3 (Testing) — 2 days
4. Total: ~1 week at normal pace

### Post-Implementation:

1. **Live testing** with real market data
2. **Latency profiling** (compare to broker app)
3. **Load testing** (100+ price updates/sec)
4. **User feedback** from actual trading

---

## Success Metrics

- ✅ Data updates within 500ms of market change
- ✅ Zero memory leaks (test with profiler)
- ✅ Reconnection succeeds within 3 seconds of connection loss
- ✅ Users report "accurate market picture"
- ✅ No impact on other features (Dashboard, AI Trade, Backtest)

---

---

## Progress Tracking

### Phase 1: Research & Design
- [ ] TopStep API WebSocket capability verified
- [ ] Rate limits and latency documented
- [ ] Fallback data sources researched (Alpaca, IB, Polygon)
- [ ] Market Data View layout designed
- [ ] Data structure defined (BidLevel, AskLevel)

### Phase 2: Implementation
- [ ] MarketDataService.vb created (WebSocket connection)
- [ ] MarketDataViewModel.vb created (real-time binding)
- [ ] MarketDataView.xaml updated (order book display)
- [ ] Order book DataGrid implemented
- [ ] Spread indicator (color-coded) added
- [ ] Reconnection logic implemented

### Phase 3: Testing & Validation
- [ ] Unit tests (mock API responses)
- [ ] Integration tests (live API connection)
- [ ] Stress tests (1000+ updates/sec)
- [ ] Data accuracy verification (vs broker)

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (blocked on API confirmation)
**Blocker:** Awaiting TopStep API WebSocket capability confirmation
**Next Concrete Action:** Contact TopStep support to confirm API streaming capability

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Copilot
**Token Estimate:** 12 (research 2 + implementation 7 + testing 3)
**Severity:** Low (nice-to-have, not blocking trades)
**Status:** Placeholder. Awaiting TopStep API capability confirmation.
