# TICKET-018: Cancel Monitoring on Rejected Trade

**Status:** Ready
**Priority:** High
**Severity:** 🔴 CRITICAL
**Assigned To:** Claude Sonnet 4.6
**Due Date:** 06/03/2026
**Tokens:** 6 (recommend increase to 8-10)
**Labels:** `bug,ai-trade,error-handling,trade-rejection`

---

## Problem Statement

**Critical Bug:** When the TopStep API **rejects a trade** (e.g., insufficient margin, invalid order, connection error), the monitoring and strategy evaluation loop **continues running indefinitely** instead of stopping immediately.

**Observable Symptoms:**
1. User starts monitoring with AI Trade → bars collected, strategy evaluated
2. Strategy generates LONG signal → trade is submitted to TopStep API
3. TopStep API rejects trade: `{"error": "Insufficient margin"}` or timeout
4. User sees error message in monitoring output
5. **BUT:** Monitoring loop continues collecting bars and evaluating strategy
6. 30 seconds later → another signal generated → another API call submitted → another rejection
7. **Cascading rejections:** Same rejected signal generates multiple redundant API calls

**Impact:**
- **Resource waste:** Continued API calls after trade is rejected (quota, rate limits)
- **Confusing UX:** User sees single rejection error, but multiple rejections occur silently
- **Log spam:** Monitoring output cluttered with repeated evaluation attempts
- **Potential order explosion:** If rejection is temporary (e.g., timeout), multiple orders may be submitted when API recovers
- **State inconsistency:** Monitoring continues as if trade succeeded, but no position was opened

---

## Root Cause Analysis

### Likely Causes:

1. **No Error Handler on Trade Execution**
   - `ExecuteTradeAsync()` throws exception or returns error response
   - **No catch block** for trade rejection in monitoring loop
   - Loop continues to next iteration

2. **Rejection Doesn't Cancel CancellationToken**
   - Trade rejection is treated as data error, not signal to stop
   - Should immediately call `_cancellationTokenSource.Cancel()` on rejection
   - Currently: loop continues despite failed trade

3. **No State Transition on Error**
   - Monitoring state machine doesn't handle rejection → stop transition
   - States: `Idle` → `Monitoring` → (on rejection) → **should be `Stopped`** but stays `Monitoring`
   - No way to signal "stop due to trade error"

4. **Retry Logic Without Backoff**
   - If rejection is due to API timeout, monitoring may retry immediately
   - No exponential backoff or cooldown period
   - Creates request storm if API is temporarily unavailable

---

## Requirements

### A. **Implement Trade Rejection Handler**

Add error handling in monitoring loop when trade execution fails:

```vb
' In StartMonitoringAsync() or EvaluateAndTradeAsync()
Private Async Function EvaluateAndTradeAsync() As Task
    Try
        ' ... evaluate strategy ...
        Dim signal = Await EvaluateStrategyAsync(_cancellationToken)

        If signal = StrategySignal.Long Or signal = StrategySignal.Short Then
            ' Attempt to execute trade
            Try
                Dim tradeResult = Await ExecuteTradeAsync(signal, _cancellationToken)

                ' Check for rejection in response
                If Not tradeResult.Success Then
                    ' ⚠️ CRITICAL: Trade was rejected
                    OutputText &= $"[{DateTime.Now:HH:mm:ss}] ❌ TRADE REJECTED: {tradeResult.ErrorMessage}" & vbCrLf

                    ' Immediately cancel monitoring
                    _cancellationTokenSource.Cancel()

                    ' Exit monitoring loop
                    Return
                End If

                ' Trade succeeded
                OutputText &= $"[{DateTime.Now:HH:mm:ss}] ✓ Trade executed: {signal}" & vbCrLf

            Catch ex As HttpRequestException
                ' API connection error (timeout, network failure)
                OutputText &= $"[{DateTime.Now:HH:mm:ss}] ❌ API ERROR: {ex.Message}" & vbCrLf
                OutputText &= "Monitoring cancelled due to API error." & vbCrLf

                ' Don't retry on connection error; stop monitoring
                _cancellationTokenSource.Cancel()
                Return

            Catch ex As OperationCanceledException
                ' Expected when token is cancelled
                Throw  ' Re-throw to exit outer loop

            Catch ex As Exception
                ' Unexpected error
                OutputText &= $"[{DateTime.Now:HH:mm:ss}] ⚠️ Unexpected error: {ex.Message}" & vbCrLf
                _cancellationTokenSource.Cancel()
                Return
            End Try
        End If

    Catch ex As OperationCanceledException
        ' Expected when monitoring is cancelled
        OutputText &= "Monitoring stopped." & vbCrLf
    Catch ex As Exception
        OutputText &= $"Error in monitoring: {ex.Message}" & vbCrLf
    End Try
End Function
```

### B. **Distinguish Between Rejection Types**

Different rejection scenarios require different handling:

| Rejection Type | Response | Action |
|---|---|---|
| **Insufficient Margin** | Permanent error | Stop immediately, notify user |
| **Invalid Order** | Permanent error | Stop immediately, log order details |
| **API Timeout** | Temporary error | Stop immediately, don't retry (user can restart) |
| **Rate Limit** | Temporary error | Stop with cooldown message |
| **Market Closed** | Expected condition | Stop with "market closed" message |

**Implementation:**
```vb
Private Function IsRetryableError(errorCode As String) As Boolean
    ' Retryable errors (shouldn't happen, but handle them)
    Select Case errorCode
        Case "RATE_LIMIT", "TEMPORARY_UNAVAILABLE"
            Return True  ' Could retry with backoff
        Case Else
            Return False  ' All other errors = stop immediately
    End Select
End Function

' In error handler:
If tradeResult.ErrorCode = "RATE_LIMIT" Then
    ' Wait 60 seconds before allowing user to restart
    OutputText &= "Rate limited. Wait 60 seconds before restarting." & vbCrLf
Else
    ' Stop immediately for all other errors
    OutputText &= $"Stopping monitoring: {tradeResult.ErrorMessage}" & vbCrLf
End If

_cancellationTokenSource.Cancel()
```

### C. **Add Trade Rejection State to Monitoring Output**

Log rejection clearly so user knows why monitoring stopped:

```
[14:35:22] EMA/RSI: Bull=75%, Bear=0% → LONG signal
[14:35:23] Executing trade...
[14:35:24] ❌ TRADE REJECTED: Insufficient margin (need $500, have $200)
[14:35:24] ⚠️ Monitoring cancelled due to trade rejection.
[14:35:24] **Action required:** Deposit funds or reduce position size, then restart.
```

### D. **Update ExecuteTradeAsync() to Return Structured Response**

Ensure trade execution returns clear success/failure status:

```vb
Public Class TradeExecutionResult
    Public Property Success As Boolean
    Public Property ErrorCode As String  ' "INSUFFICIENT_MARGIN", "INVALID_ORDER", etc.
    Public Property ErrorMessage As String  ' Human-readable error
    Public Property TradeId As String  ' Only if Success = True
End Class

' Usage:
Dim result = Await _tradeService.ExecuteTradeAsync(signal, _cancellationToken)
If result.Success Then
    ' Trade executed
Else
    ' Trade rejected; handle error
End If
```

---

## Implementation Plan

### Phase 1: Investigation & Diagnosis (2 tokens)
1. Locate monitoring loop in `AiTradingViewModel.StartMonitoringAsync()`
2. Find where `ExecuteTradeAsync()` is called
3. Check for try/catch block around trade execution
4. Inspect `TradeExecutionResult` or error response structure
5. Verify if rejection currently stops or continues monitoring

### Phase 2: Design & Architecture (1-2 tokens)
1. Define rejection handling strategy:
   - Immediate stop on all rejections? Or retry for temporary errors?
   - Recommendation: **Immediate stop** (user can restart)
2. Identify rejection types and error codes
3. Plan logging/output messages for each scenario

### Phase 3: Implementation (2-3 tokens)
1. **AiTradingViewModel.vb:**
   - Add try/catch around `ExecuteTradeAsync()` call
   - Implement `IsRetryableError()` or similar logic
   - Cancel token on rejection: `_cancellationTokenSource.Cancel()`
   - Add detailed rejection logging to OutputText

2. **TradeService.vb** (if needed):
   - Ensure `ExecuteTradeAsync()` returns structured result (not just exception)
   - Map API error codes to human-readable messages

3. **Monitoring Loop:**
   - Check for rejection immediately after trade attempt
   - Don't wait for next evaluation cycle to stop

### Phase 4: Testing & Validation (1-2 tokens)
1. Unit tests:
   - Mock `ExecuteTradeAsync()` to return rejection
   - Verify `CancellationToken.Cancel()` is called

2. Integration tests:
   - Simulate API rejection (Insufficient Margin, Invalid Order, Timeout)
   - Verify monitoring stops immediately (within 1 second)
   - Verify user receives clear error message
   - Verify no duplicate API calls after rejection

3. Stress tests:
   - Simulate rapid rejections (same signal, multiple attempts)
   - Verify monitoring stops after FIRST rejection

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add try/catch for trade rejection, cancel token on error, improve error logging |
| `src/TopStepTrader.Services/Trading/TradeService.vb` | Ensure `ExecuteTradeAsync()` returns clear success/error response structure |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Ensure strategy evaluation continues only if trade succeeds |
| `src/TopStepTrader.API/Responses/TradeExecutionResponse.vb` | Create/verify structured response with `Success`, `ErrorCode`, `ErrorMessage` |

---

## Acceptance Criteria

- [ ] Trade rejection is caught in monitoring loop (try/catch around ExecuteTradeAsync)
- [ ] On any trade rejection, `_cancellationTokenSource.Cancel()` is called
- [ ] Monitoring stops immediately after rejection (within 1 second)
- [ ] Rejection message is logged to OutputText with timestamp and error code
- [ ] No duplicate API calls after rejection (monitored via logs/network)
- [ ] User receives clear error message explaining why monitoring stopped
- [ ] Different rejection types are handled appropriately (temporary vs permanent)
- [ ] Rapid rejection scenarios (same signal, multiple attempts) don't cause request storm
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: successful trades still work correctly

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Immediate stop on rejection (vs retry)** | Prevents cascading errors ✅ | User must manually restart ❌ |
| **Log rejection to OutputText (vs silent stop)** | User informed ✅ | May clutter UI ❌ |
| **Distinguish rejection types (vs treat all same)** | Better UX ✅ | More complex logic ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Rejection response missing `Success` field** → NullReferenceException | Defensive coding: `result?.Success ?? false` |
| **Multiple rejections create request storm** → API quota exhausted | Immediate cancel + no retry logic |
| **User doesn't understand rejection reason** → Confusion | Clear, detailed error message in OutputText |
| **API timeout looks like rejection** → User confused | Distinguish "timeout" from "rejection" in messaging |

---

## Related Tickets

- **TICKET-017:** Implement Complete Stop on Stop Button — related cleanup on error
- **TICKET-016:** Fix Tab Switching Process Persistence — related orphaned tasks
- **TICKET-012:** Fix 30-Second Bar Check vs 5-Minute Strategy — affects polling loop

---

## Next Steps

### Immediate (After Approval):
1. **Locate trade execution** in monitoring loop
   - Find `ExecuteTradeAsync()` call
   - Check if it has try/catch
2. **Inspect API response** structure
   - How does API communicate rejection? (Exception? Response field?)
   - What error codes exist?
3. **Test rejection manually** (if possible)
   - Force trade rejection (low margin, invalid order)
   - Watch monitoring behavior

### During Implementation:
1. Phase 1 (Investigation) — 20-30 minutes
2. Phase 2 (Design) — 15 minutes
3. Phase 3 (Implementation) — 45 minutes - 1 hour
4. Phase 4 (Testing) — 30-45 minutes

### Post-Implementation:
1. **Live testing:** Start monitoring, trigger API rejection, verify clean stop
2. **Edge case testing:** Multiple rejection types (margin, invalid order, timeout)
3. **Integration testing:** Verify related tickets (TICKET-017, TICKET-016) work together

---

## Error Code Reference (To Be Populated)

Populate these based on actual TopStep API documentation:

| Error Code | Meaning | Action |
|---|---|---|
| `INSUFFICIENT_MARGIN` | Account doesn't have enough buying power | Stop, notify user to deposit funds |
| `INVALID_ORDER` | Order parameters invalid (price, qty, etc.) | Stop, log order details |
| `SYMBOL_NOT_TRADING` | Market is closed or symbol not available | Stop, notify market hours |
| `TIMEOUT` | API request timed out | Stop, allow user to retry |
| `RATE_LIMIT` | Too many requests (quota exceeded) | Stop, notify wait time |
| `POSITION_LIMIT` | Max open positions exceeded | Stop, notify user |

---

---

## Progress Tracking

### Phase 1: Investigation & Diagnosis
- [ ] Trade rejection scenarios documented
- [ ] Monitoring loop error handling audited
- [ ] Root cause identified (missing try/catch around ExecuteTradeAsync)

### Phase 2: Design & Architecture
- [ ] Rejection handler strategy designed
- [ ] Error code categorization completed
- [ ] Retry logic decisions documented

### Phase 3: Implementation
- [ ] Try/catch block added around ExecuteTradeAsync()
- [ ] IsRetryableError() logic implemented
- [ ] Rejection detection and CancellationToken.Cancel() integrated
- [ ] Detailed rejection logging added to OutputText

### Phase 4: Testing & Validation
- [ ] Unit tests for rejection handler
- [ ] Integration tests (simulate API rejection)
- [ ] Stress tests (rapid rejections)
- [ ] Network tests (timeout scenarios)

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (pending assignment)
**Blocker:** None
**Next Concrete Action:** Locate ExecuteTradeAsync() call in monitoring loop and add try/catch

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Claude Sonnet 4.6
**Token Estimate:** 8-10 (increased from 6 due to error handling complexity)
**Severity:** 🔴 CRITICAL (cascading rejections, stale state)
