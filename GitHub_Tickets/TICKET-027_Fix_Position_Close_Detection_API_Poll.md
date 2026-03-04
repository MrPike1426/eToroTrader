# TICKET-027: Fix Position Close Detection — Poll TopStepX API Instead of Local Database

**Status:** Complete
**Priority:** High
**Severity:** Critical
**Assigned To:** GitHub Copilot
**StartDate:** 2026-03-02
**Due Date:** 2026-03-02
**Tokens:** 2
**Labels:** `bug,ai-trade,position-management,critical,data-integrity`

---

## Problem Statement

The AI Trade engine **never reliably detects when a position closes** once both bracket orders
(TP and SL) have been successfully placed on the exchange.

### Root Cause

`StrategyExecutionEngine.DoCheckAsync` polls for open orders every 30 seconds to detect
when the bracket position has closed:

```vb
' BROKEN — queries local SQLite DB, not the exchange:
Dim openOrders = Await _orderService.GetOpenOrdersAsync(_strategy.AccountId)
Dim stillOpen = openOrders.Any(
    Function(o) o.ContractId = _strategy.ContractId AndAlso
                o.Status = OrderStatus.Working)
```

`GetOpenOrdersAsync` queries the **local SQLite `Orders` table**. The `Orders` table is
written at order placement time (`Working`) but is **never updated** when orders fill or
cancel on the exchange. Once both TP and SL are placed successfully:

| Order | Local DB status | Reality |
|-------|----------------|---------|
| TP Limit | `Working` (forever) | Fills when price reaches TP level |
| SL StopLimit | `Working` (forever) | Cancels when TP fills (or fills when price hits SL) |

Because both orders remain `Working` in the local DB, `stillOpen` is always `True` and
`_positionOpen` is **never reset to `False`**. This means:

- No second trade is ever placed during the session (**UAT-BUG-007 regresses**)
- The performance panel trade row stays "⏳ In Progress..." forever
- `TradeClosed` event is never raised → Results table never updates

### Why It Worked Before UAT-BUG-008

Before the SL order type fix (UAT-BUG-008), the SL was **rejected** (Status=5 ≠ Working=1)
every time. With only the TP order showing as `Working`, when the TP filled on the exchange
the TP order remained `Working` in the local DB — but `stillOpen` was `True` due to the TP.
This **accidentally worked** because TopStepX automatically cancelled the SL when TP filled,
but we never knew about it. The detection only appeared to work because the SL was always
rejected.

Now that UAT-BUG-008 is fixed and SL orders are accepted as `Working`, both orders show
`Working` in the local DB permanently, and `stillOpen` is always `True`.

---

## Fix

Add `IOrderService.GetLiveWorkingOrdersAsync(accountId, contractId)` — a new method that
calls the **TopStepX REST API** (`/api/Order/search`) instead of the local database.
The API returns the **current live status** of all orders for the account. Filter for
`Status=1 (Working)` and matching `contractId`.

Replace the local DB call in `DoCheckAsync` with this API-backed method.

### Why This Is Correct

When the TP or SL fills on the exchange:
- TopStepX automatically cancels the opposing bracket order
- The **API** reflects this immediately: TP=`Filled(2)`, SL=`Cancelled(3)` (or vice versa)
- The **local DB** never reflects this — only the API has ground truth

The `TryGetOrderFillPriceAsync` method (already in the codebase) uses the same
`SearchOrdersAsync` API call to determine fill price. The position-close detection
should use the same source of truth.

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Core/Interfaces/IOrderService.vb` | Add `GetLiveWorkingOrdersAsync` to interface |
| `src/TopStepTrader.Services/Trading/OrderService.vb` | Implement — calls `_orderClient.SearchOrdersAsync` |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Use `GetLiveWorkingOrdersAsync` in `DoCheckAsync` |

---

## Acceptance Criteria

- [ ] After TP fills on exchange: engine detects position closed within 30 seconds
- [ ] After SL fills on exchange: engine detects position closed within 30 seconds
- [ ] Performance panel row updates from "⏳ In Progress..." to `TP +$NNN ✅` or `SL -$NNN ❌`
- [ ] A second trade fires when conditions are met after position closes
- [ ] Local DB order status is not consulted for position-close decisions
- [ ] Build: 0 errors, 0 warnings

---

## API Status Codes (TopStepX / ProjectX)

| Code | Meaning | Our `OrderStatus` enum |
|------|---------|----------------------|
| 0 | Pending | `Pending = 0` |
| 1 | **Working** | `Working = 1` |
| 2 | **Filled** | `Filled = 2` |
| 3 | Cancelled | `Cancelled = 4` ⚠️ mismatch (our enum has `PartiallyFilled = 3`) |
| 4 | Rejected | `Rejected = 5` ⚠️ mismatch |
| 5 | Expired | `Expired = 6` ⚠️ mismatch |

> **Note:** The local `OrderStatus` enum does not match the API status integers for values ≥ 3.
> `GetLiveWorkingOrdersAsync` compares `OrderDto.Status = 1` (the raw API integer)
> rather than casting to the enum, to avoid this mismatch.
> A follow-up ticket should reconcile the enum values with the API spec.

---

## Related Tickets

- **UAT-BUG-007:** Position flag never resets — original bug (this is its definitive fix)
- **UAT-BUG-008:** SL order type fix — unmasked this bug by making SL orders actually work
- **TICKET-025:** AI Trade Performance Panel — depends on `TradeClosed` being raised correctly

**Created:** 2026-03-02
**Last Updated:** 2026-03-02
**Model:** GitHub Copilot
