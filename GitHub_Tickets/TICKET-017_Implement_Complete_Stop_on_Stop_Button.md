# TICKET-017: Implement Complete Stop on Stop Button

**Status:** Ready
**Priority:** High
**Severity:** 🔴 CRITICAL
**Assigned To:** Claude Sonnet 4.6
**Due Date:** 08/04/2026
**Tokens:** 8 (recommend increase to 10-12)
**Labels:** `bug,ai-trade,background-tasks,cleanup`

---

## Problem Statement

**Critical Bug:** When user clicks the "Stop" button in AI Trade tab **while monitoring is active**, the UI updates (button changes state, IsRunning flag flips to false) **but the background bar collection and strategy evaluation loop continues running in the background**.

**Observable Symptoms:**
1. Click "Start Monitoring" → monitoring begins, bars are collected, strategy evaluated every 30s
2. Click "Stop" button → UI shows "Stopped" state, button becomes disabled
3. Check Immediate Window → still sees bar collection logs and strategy evaluations continuing
4. Task manager → process still consuming API calls and CPU
5. Switch tabs and back → monitoring still running (stale state)

**Impact:**
- **Resource leak:** Background threads/tasks consume CPU, memory, and API quota
- **API call waste:** Continued API calls to fetch bars even though user clicked Stop
- **Misleading UI:** User thinks monitoring stopped, but it's actually still running
- **Compounding with other bugs:** Combined with TICKET-016 (tab switch leak), orphaned tasks accumulate

---

## Root Cause Analysis

### Likely Causes:

1. **CancellationToken Not Cancelled in ExecuteStop()**
   - `ExecuteStop()` method sets `IsRunning = false` but never calls `_cancellationTokenSource.Cancel()`
   - Monitoring loop checks `IsRunning` flag instead of `CancellationToken.IsCancellationRequested`
   - Background task continues running indefinitely

2. **Monitoring Loop Doesn't Check CancellationToken**
   - Loop may check only `IsRunning` flag: `While IsRunning` (✅ manual flag)
   - Should check: `While Not _cancellationToken.IsCancellationRequested` (✅ auto-cancellation)
   - Without token check, even if token is cancelled, loop doesn't respond

3. **Async Task Not Properly Awaited**
   - Monitoring task may be `Fire-and-Forget` (started with `_ = StartMonitoringAsync()`)
   - Parent ViewModel doesn't track task reference
   - No way to await or cancel the running task

4. **Delay in Loop Doesn't Respect CancellationToken**
   - Loop may use `Task.Delay(30000)` instead of `Task.Delay(30000, _cancellationToken)`
   - Without token parameter, delay ignores cancellation request
   - Task continues blocked on delay even after Stop clicked

---

## Requirements

### A. **Implement Proper Cancellation in ExecuteStop()**

```vb
Private Sub ExecuteStop()
    ' Immediate UI update
    IsRunning = False

    ' Critical: Cancel the CancellationToken
    Try
        _cancellationTokenSource.Cancel()
    Catch ex As ObjectDisposedException
        ' Token was already disposed; safe to ignore
    End Try

    ' Optionally: Wait for monitoring task to finish
    ' If _monitoringTask IsNot Nothing AndAlso Not _monitoringTask.IsCompleted Then
    '     _monitoringTask.Wait(TimeSpan.FromSeconds(5))  ' Max 5 sec wait
    ' End If

    ' Reset state
    SelectedContractId = Nothing
    OutputText = "Monitoring stopped."
End Sub
```

### B. **Verify Monitoring Loop Respects CancellationToken**

```vb
' In StartMonitoringAsync() or similar
Private Async Function StartMonitoringAsync() As Task
    Try
        While Not _cancellationToken.IsCancellationRequested  ' ✅ Check token
            ' Fetch bars
            Dim bars = Await FetchBarsAsync(_cancellationToken)  ' ✅ Pass token

            ' Evaluate strategy
            Dim signal = Await EvaluateStrategyAsync(bars, _cancellationToken)  ' ✅ Pass token

            ' Log result
            OutputText &= signal.ToString() & vbCrLf

            ' Polling delay - CRITICAL: Pass cancellation token
            Await Task.Delay(30000, _cancellationToken)  ' ✅ Respects cancellation
            ' NOT: Await Task.Delay(30000)  ' ❌ Ignores cancellation
        End While
    Catch ex As OperationCanceledException
        ' Expected when CancellationToken is cancelled
        OutputText &= "Monitoring cancelled." & vbCrLf
    Catch ex As Exception
        OutputText &= "Error: " & ex.Message & vbCrLf
    End Try
End Function
```

**Critical Points:**
- Loop condition: `While Not _cancellationToken.IsCancellationRequested`
- Every async call passes `_cancellationToken` parameter
- `Task.Delay()` calls MUST pass token: `Task.Delay(ms, _cancellationToken)`
- Catch `OperationCanceledException` to clean up gracefully

### C. **Track Monitoring Task for Proper Cleanup**

Store reference to monitoring task so it can be awaited on Stop:

```vb
Private _monitoringTask As Task

' In ExecuteStart()
Private Sub ExecuteStart()
    IsRunning = True
    _monitoringTask = StartMonitoringAsync()  ' Store reference
    ' Don't await here; let it run in background
End Sub

' In ExecuteStop()
Private Sub ExecuteStop()
    IsRunning = False

    ' Cancel token
    _cancellationTokenSource.Cancel()

    ' Wait for task to finish (with timeout)
    If _monitoringTask IsNot Nothing AndAlso Not _monitoringTask.IsCompleted Then
        Try
            _monitoringTask.Wait(TimeSpan.FromSeconds(5))  ' Max 5 second wait
        Catch ex As AggregateException
            ' Task was cancelled; this is expected
        End Try
    End If

    _monitoringTask = Nothing
End Sub
```

### D. **Create New CancellationTokenSource on Each Start**

Ensure fresh cancellation token for each monitoring session:

```vb
' In ExecuteStart()
Private Sub ExecuteStart()
    ' Create fresh token for new monitoring session
    _cancellationTokenSource = New CancellationTokenSource()
    _cancellationToken = _cancellationTokenSource.Token

    IsRunning = True
    _monitoringTask = StartMonitoringAsync()
End Sub

' In ExecuteStop()
Private Sub ExecuteStop()
    IsRunning = False

    ' Cancel current session
    _cancellationTokenSource?.Cancel()

    ' Wait for completion, then dispose
    If _monitoringTask?.IsCompleted = False Then
        Try
            _monitoringTask.Wait(TimeSpan.FromSeconds(5))
        Catch
        End Try
    End If

    ' Dispose old token source
    _cancellationTokenSource?.Dispose()
    _cancellationTokenSource = Nothing
End Sub
```

---

## Implementation Plan

### Phase 1: Investigation & Diagnosis (2 tokens)
1. Locate `ExecuteStop()` method in `AiTradingViewModel.vb`
2. Verify if `_cancellationTokenSource.Cancel()` is called
3. Inspect monitoring loop: does it check `CancellationToken.IsCancellationRequested`?
4. Check all `Task.Delay()` calls: do they pass `_cancellationToken`?
5. Verify task tracking: is `_monitoringTask` stored?

### Phase 2: Design & Architecture (1 token)
1. Decide: wait for task completion in `ExecuteStop()` or just cancel and detach?
   - **Option A:** Wait with 5s timeout (safer, ensures cleanup)
   - **Option B:** Just cancel token (faster UI response, but task may linger)
   - **Recommendation:** Option A (clean shutdown)
2. Design exception handling for `OperationCanceledException`

### Phase 3: Implementation (3-4 tokens)
1. **AiTradingViewModel.vb:**
   - Add `_monitoringTask` field to track monitoring
   - Modify `ExecuteStart()`: Create new `CancellationTokenSource`, store task
   - Modify `ExecuteStop()`: Cancel token, wait for task, dispose resources
   - Ensure `StartMonitoringAsync()` loop checks token in every iteration
   - Ensure ALL async calls pass `_cancellationToken`

2. **BarCollectionService.vb** (if separate):
   - Ensure `FetchBarsAsync()` accepts and respects `CancellationToken`

3. **StrategyExecutionEngine.vb** (if separate):
   - Ensure `EvaluateStrategyAsync()` accepts and respects `CancellationToken`

### Phase 4: Testing & Validation (2-3 tokens)
1. Unit tests:
   - Test `ExecuteStop()` cancels token
   - Test `CancellationToken.IsCancellationRequested` prevents loop iteration

2. Integration tests:
   - Click Start → Immediate Window shows bar logs
   - Click Stop → Immediate Window stops logging within 1 second
   - Click Start again → new logs appear (token was fresh)
   - Rapid start/stop: 5 cycles → verify no memory leak

3. Performance tests:
   - Monitor CPU during monitoring
   - Verify CPU drops to idle < 100ms after Stop
   - Check task count returns to baseline

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add `_monitoringTask` field, fix `ExecuteStart()` and `ExecuteStop()`, verify loop token checks |
| `src/TopStepTrader.Services/BarCollectionService.vb` | Ensure `FetchBarsAsync()` accepts `CancellationToken` parameter |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Ensure all async methods accept and respect `CancellationToken` |

---

## Acceptance Criteria

- [ ] Clicking "Stop" button cancels the CancellationToken
- [ ] Monitoring loop checks `_cancellationToken.IsCancellationRequested` in every iteration
- [ ] All async calls in monitoring loop pass `_cancellationToken` parameter
- [ ] `Task.Delay()` calls use `Task.Delay(ms, _cancellationToken)` (not just `Task.Delay(ms)`)
- [ ] Immediate Window shows clean stop (no logs) within 1 second of clicking Stop
- [ ] Task is properly awaited and awaited with timeout in `ExecuteStop()`
- [ ] `OperationCanceledException` is caught and handled gracefully
- [ ] Start monitoring again after Stop works correctly (fresh token)
- [ ] Rapid start/stop cycling (5x) doesn't leak tasks or memory
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: other monitoring features still work

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Wait for task on Stop (vs immediate cancel)** | Ensures cleanup ✅ | Slight UI lag ❌ |
| **Check token in loop (vs IsRunning flag only)** | Responsive to cancellation ✅ | Requires token refactoring ❌ |
| **Pass token to every async call (vs selective)** | Comprehensive cancellation ✅ | More parameter passing ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **CancellationTokenSource already disposed** → ObjectDisposedException | Wrap `Cancel()` in try/catch for `ObjectDisposedException` |
| **Task doesn't respect token** → Infinite wait | Use `Wait(TimeSpan)` with timeout (e.g., 5 seconds) |
| **Race condition: Stop before Start completes** → Double-cancel error | Guard with `_cancellationTokenSource?.Cancel()` (null-safe) |
| **User clicks Stop multiple times** → Multiple cancel attempts | Idempotent design; `Cancel()` is safe to call multiple times |

---

## Related Tickets

- **TICKET-016:** Fix Tab Switching Process Persistence — related orphaned task issue
- **TICKET-018:** Cancel Monitoring on Rejected Trade — related state cleanup
- **TICKET-012:** Fix 30-Second Bar Check vs 5-Minute Strategy — affects polling timeout handling

---

## Next Steps

### Immediate (After Approval):
1. **Reproduce the bug** in Immediate Window
   - Start monitoring, click Stop, watch for continued logs
2. **Inspect ExecuteStop()** in AiTradingViewModel.vb
   - Verify `_cancellationTokenSource.Cancel()` is called
3. **Audit StartMonitoringAsync()** monitoring loop
   - Check token condition and async call parameters

### During Implementation:
1. Start with Phase 1 (Investigation) — 30 minutes
2. Phase 2 (Design) — 15 minutes
3. Phase 3 (Implementation) — 1-2 hours (depends on scope)
4. Phase 4 (Testing) — 30-45 minutes

### Post-Implementation:
1. **Live testing:** Start monitoring for 1 min, click Stop, verify clean stop
2. **Stress testing:** Rapid start/stop cycling (50x) with memory profiler
3. **Integration testing:** Verify TICKET-016 (tab switching) works correctly with this fix

---

## Progress Tracking

### Phase 1: Investigation & Diagnosis
- [ ] ExecuteStop() method audited
- [ ] CancellationToken usage verified
- [ ] Task.Delay() calls checked for token parameter
- [ ] Monitoring loop token checks confirmed

### Phase 2: Design & Architecture
- [ ] Cancellation strategy designed
- [ ] Task tracking mechanism planned
- [ ] Exception handling for OperationCanceledException documented

### Phase 3: Implementation
- [ ] _monitoringTask field added to track monitoring
- [ ] ExecuteStart() creates new CancellationTokenSource
- [ ] ExecuteStop() cancels token and waits for task
- [ ] All Task.Delay() calls updated with token parameter
- [ ] Loop condition updated to check token

### Phase 4: Testing & Validation
- [ ] Unit tests for cancellation logic
- [ ] Integration tests (start/stop/start cycles)
- [ ] Performance tests (rapid start/stop 5x)
- [ ] Memory profiler checks (no leaks)

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (pending assignment)
**Blocker:** None
**Next Concrete Action:** Verify CancellationTokenSource.Cancel() is called in ExecuteStop()

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Claude Sonnet 4.6
**Token Estimate:** 10-12 (increased from 8 due to comprehensive cancellation audit)
**Severity:** 🔴 CRITICAL (orphaned background tasks)
