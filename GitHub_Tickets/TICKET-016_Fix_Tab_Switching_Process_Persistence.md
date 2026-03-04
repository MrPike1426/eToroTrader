# TICKET-016: Fix Tab Switching Process Persistence

**Status:** Ready
**Priority:** High
**Assigned To:** Claude Sonnet 4.6
**Due Date:** 06/03/2026 (Friday)
**Tokens:** 10 (recommend increase to 13)
**Labels:** `bug,ai-trade,process-lifecycle,state-management`

---

## Problem Statement

**Critical Bug:** When user clicks away from the AI Trade tab to another tab (Dashboard, Backtest, etc.), the **UI resets to default state** (idle, no contract loaded, buttons disabled) **but the monitoring process continues running in the background**.

**Observable Symptoms:**
1. Click "Start Monitoring" in AI Trade tab → monitoring begins
2. Click away to Dashboard tab → UI shows no activity
3. Return to Immediate Window (Debug output) → still sees bar collection logs, strategy evaluations happening
4. Return to AI Trade tab → UI still shows idle state (doesn't resume)
5. Click Stop → no effect (monitoring already orphaned)

**Impact:**
- **Resource leak:** Background threads/tasks consuming CPU/memory while UI is inactive
- **API call leakage:** Continued API calls to fetch bars/evaluate strategy even when user isn't monitoring
- **Confusing UX:** User thinks monitoring stopped, but it's still running
- **Cascading bugs:** If monitoring continues and trade executes, but UI is gone, trade confirmation shows nowhere

---

## Root Cause Analysis

### Likely Causes:

1. **ViewModel Not Unloading**
   - `AiTradingViewModel` may be registered as **Singleton** in `AppBootstrapper`
   - When tab switches, ViewModel doesn't unload; same instance persists
   - No `OnUnloaded()` event fires to trigger cleanup

2. **Missing CancellationToken Check**
   - Monitoring loop may not check `_cancellationToken.IsCancellationRequested`
   - Loop continues indefinitely even if token is cancelled

3. **No Cleanup on Tab Away**
   - No event handler for tab switching (e.g., `View.Unloaded` or `ViewModel.Cleanup()`)
   - Background tasks never receive stop signal

4. **Event Subscription Leaks**
   - ViewModel subscribes to bar collection events but never unsubscribes
   - Event handlers keep ViewModel alive and running in memory

---

## Requirements

### A. **ViewModel Lifecycle Cleanup**

Add explicit cleanup on tab away. Two approaches:

**Option 1: Clean Shutdown (Recommended)**
```vb
Public Sub OnUnloaded()
    ' Called when tab is switched away
    ' Immediately stop monitoring, cancel tasks, reset state
    If IsRunning Then
        ExecuteStop()  ' Cancel monitoring
    End If

    ' Unsubscribe from all events
    ' Dispose CancellationTokenSource
    ' Clear collections
End Sub
```

**Option 2: Maintain State (Complex)**
- Pause (don't stop) monitoring on tab away
- Preserve `_selectedContractId`, `StrategyDefinition`, monitoring state
- Resume when returning to tab
- **Trade-off:** More complex, more memory used, but smoother UX

**Recommendation: Use Option 1 (Clean Shutdown)**
- Simpler to implement
- Safer (no orphaned background tasks)
- User can restart monitoring when returning to AI Trade tab
- Aligns with "stop on tab away" pattern in most trading apps

### B. **CancellationToken Audit**

Verify monitoring loop respects cancellation:

```vb
' In monitoring loop (StartMonitoringAsync or similar)
While Not _cancellationToken.IsCancellationRequested
    ' Fetch bars, evaluate strategy, etc.

    If _cancellationToken.IsCancellationRequested Then
        Exit While
    End If

    ' Polling delay
    Await Task.Delay(30000, _cancellationToken)
End While
```

**Critical:** Every async call in loop must pass `_cancellationToken` parameter:
- `Task.Delay(ms, _cancellationToken)` ✅
- `FetchBarsAsync(_cancellationToken)` ✅
- `EvaluateStrategyAsync(_cancellationToken)` ✅
- **Not** `Task.Delay(ms)` ❌ (ignores cancellation)

### C. **ViewModel Registration in AppBootstrapper**

Check if `AiTradingViewModel` is registered as Singleton or Transient:

```vb
' In AppBootstrapper.ConfigureServiceLocator()
_serviceLocator.RegisterSingleton(Of AiTradingViewModel)()  ' ❌ BAD
' vs
_serviceLocator.RegisterTransient(Of AiTradingViewModel)()  ' ✅ BETTER
```

**If Singleton:**
- Must add manual `OnUnloaded()` method (see above)
- Method wires to `AiTradingView.Unloaded` event

**If Transient:**
- New instance created each tab visit
- Old instance is garbage collected
- But still need explicit cleanup before GC (unsubscribe from events)

### D. **Event Subscription Cleanup**

If `AiTradingViewModel` subscribes to events, unsubscribe in `OnUnloaded()`:

```vb
Private Sub OnUnloaded()
    ' Unsubscribe from all custom events
    RemoveHandler _barCollectionService.BarsUpdated, AddressOf OnBarsUpdated
    RemoveHandler _tradeService.TradeExecuted, AddressOf OnTradeExecuted

    ' Cancel monitoring
    _cancellationTokenSource.Cancel()
    _cancellationTokenSource.Dispose()

    ' Clear state
    IsRunning = False
    SelectedContractId = Nothing
End Sub
```

---

## Implementation Plan

### Phase 1: Investigation & Diagnosis (3 tokens)
1. Reproduce tab switching issue reliably
2. Add logging to confirm:
   - When `OnUnloaded()` fires (or if it never fires)
   - When `_cancellationToken.IsCancellationRequested` is checked
   - What ViewModel registration is (Singleton vs Transient)
3. Inspect `AppBootstrapper` and `ViewModelLocator`
4. Review monitoring loop in `AiTradingViewModel.StartMonitoringAsync()`

### Phase 2: Design & Architecture (2 tokens)
1. Decide: Clean shutdown (Option 1) vs Maintain state (Option 2)
2. Design cleanup sequence:
   - Stop monitoring task
   - Cancel CancellationToken
   - Unsubscribe from events
   - Reset UI state
3. Determine where to wire `OnUnloaded()` (ViewModel or View codebehind?)

### Phase 3: Implementation (3 tokens)
1. **AiTradingViewModel.vb:**
   - Add `OnUnloaded()` method with full cleanup
   - Ensure all async loops check `_cancellationToken.IsCancellationRequested`
   - Wire cleanup events

2. **AiTradingView.xaml.vb** (codebehind):
   - Subscribe to `View.Unloaded` event
   - Call `ViewModel.OnUnloaded()` when fired

3. **AppBootstrapper.vb:**
   - Check and adjust ViewModel registration if needed

### Phase 4: Testing & Validation (2-3 tokens)
1. Unit tests:
   - Test `OnUnloaded()` cancels monitoring
   - Test CancellationToken propagates to all async calls

2. Integration tests:
   - Switch from AI Trade to Dashboard → monitoring stops
   - Switch back to AI Trade → UI resets, ready for new monitoring
   - Repeat 5 times → verify no memory leaks (Immediate Window clean)
   - Check task count: `System.Diagnostics.Process.GetCurrentProcess().Threads.Count`

3. Performance test:
   - Monitor CPU/memory while switching tabs rapidly
   - Verify CPU returns to idle after tab away

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Add `OnUnloaded()` method, audit `_cancellationToken` checks in monitoring loop |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml.vb` | Subscribe to `Unloaded` event, call ViewModel cleanup |
| `src/TopStepTrader.UI/AppBootstrapper.vb` | Verify ViewModel registration; may adjust Singleton→Transient |
| `src/TopStepTrader.Services/Trading/BarCollectionService.vb` | Ensure all `FetchBarsAsync()` calls pass `CancellationToken` |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Ensure all async calls respect `CancellationToken` |

---

## Acceptance Criteria

- [ ] Tab switching from AI Trade → any other tab → back to AI Trade succeeds 5 times without errors
- [ ] Immediate Window shows no lingering bar collection logs after tab away
- [ ] Process task count returns to baseline after tab away (no orphaned tasks)
- [ ] CPU usage returns to idle (< 5%) after tab away
- [ ] Memory profiler shows no event subscription leaks
- [ ] `OnUnloaded()` is called when View unloads (add logging to verify)
- [ ] `_cancellationToken.IsCancellationRequested` is checked in every loop iteration
- [ ] All async method calls in monitoring loop pass `_cancellationToken` parameter
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] No regression: Existing monitoring functionality (start/stop/evaluate) still works

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Clean Shutdown (vs Maintain State)** | Simpler ✅ | Loses user state ❌ |
| **Manual `OnUnloaded()` call (vs event auto-wire)** | Explicit control ✅ | More boilerplate ❌ |
| **Check CancellationToken in loop (vs async cancellation only)** | More responsive ✅ | Slight overhead ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **ViewModel is Singleton** → No auto-cleanup | Add manual `OnUnloaded()` method wired to View.Unloaded event |
| **Event subscription leaks** → Memory grows | Audit all `.AddHandler` calls; ensure all have corresponding `.RemoveHandler` |
| **CancellationToken not awaited** → Task continues | Pass token to ALL async calls; test with rapid tab switching |
| **Cleanup called twice** → Double-dispose error | Add guard: `If _isUnloading Then Return` |

---

## Next Steps

### Immediate (After Approval):
1. **Reproduce the issue** in Immediate Window
   - Start monitoring, switch tabs, watch for continued logging
2. **Inspect current registration**
   - Open `AppBootstrapper.vb` → check `AiTradingViewModel` registration
3. **Review monitoring loop**
   - Open `AiTradingViewModel.StartMonitoringAsync()` → verify CancellationToken checks

### During Implementation:
1. Start with Phase 1 (Investigation) — low risk
2. Implement Phase 2 (Design) — document the cleanup sequence
3. Phase 3 (Implementation) — modify ViewModel + View
4. Phase 4 (Testing) — comprehensive tab switching tests

### Post-Implementation:
1. **Live testing:** Run AI Trade with monitoring, switch tabs multiple times, verify no background activity
2. **Performance profiling:** Use Task Manager or Visual Studio Diagnostics to confirm no lingering tasks
3. **Consider related tickets:** TICKET-017 (Stop Button) and TICKET-018 (Trade Rejection) may have similar cleanup needs

---

## Related Documentation

- **TICKET-017**: Implement Complete Stop on Stop Button — related cleanup issue
- **TICKET-018**: Cancel Monitoring on Rejected Trade — related state management
- **AppBootstrapper pattern:** Check `src/TopStepTrader.UI/AppBootstrapper.vb` for ViewModel lifecycle
- **WPF Lifecycle:** Understand View.Unloaded event and DataContext cleanup timing

---

## Progress Tracking

### Phase 1: Investigation & Diagnosis
- [ ] OnUnloaded event wiring verified
- [ ] ViewModel registration (Singleton vs Transient) confirmed
- [ ] CancellationToken checks audited
- [ ] Logs added to track cleanup

### Phase 2: Design & Architecture
- [ ] Cleanup sequence designed
- [ ] OnUnloaded method spec completed
- [ ] ViewModel lifecycle documented

### Phase 3: Implementation
- [ ] AiTradingViewModel.OnUnloaded() implemented
- [ ] AiTradingView.xaml.vb Unloaded event wired
- [ ] All async loops audited for token checks
- [ ] Event unsubscription implemented

### Phase 4: Testing & Validation
- [ ] Unit tests for cleanup logic
- [ ] Integration tests (tab switching 5x)
- [ ] Performance tests (memory/CPU checks)
- [ ] Task count validated (no orphaned tasks)

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (pending assignment)
**Blocker:** None
**Next Concrete Action:** Reproduce tab switching bug and add logging

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Claude Sonnet 4.6
**Token Estimate:** 13 (increased from 10 due to lifecycle complexity)
