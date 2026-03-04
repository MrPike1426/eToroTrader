# TICKET-010: Fix Stop Loss Hardcoding Bug

**Status:** For Development
**Priority:** High
**Severity:** 🔴 CRITICAL
**Assigned To:** Claude Sonnet 4.6
**Due Date:** 10/03/2026
**Tokens:** 18 (increased from 10 due to risk assessment)
**Labels:** `bug,critical,risk-management,stop-loss`

---

## Problem Statement

**CRITICAL RISK:** Stop Loss (SL) and Take Profit (TP) are **hardcoded in API calls** instead of being applied once post-trade. If the app crashes or reconnects, trades continue running **without stop loss protection**.

### Documented Evidence

**Case 1: Double Bottom RSI Trade (2026-02-25 11:50:16 UTC)**
```
Entry: 4532.75 (MESH26)
Expected SL: 4522.75 (10 points below entry)
Expected TP: 4542.75 (10 points above entry)

Actual API Call (from logs):
{
  "symbol": "MESH26",
  "quantity": 4,
  "entry_price": 4532.75,
  "stop_loss": 0.00,    ← ❌ ZERO! Should be 4522.75
  "take_profit": 0.00   ← ❌ ZERO! Should be 4542.75
}

Result: Trade opened at 4532.75, ran NAKED (no protection)
         If price dropped to 4520, no exit → catastrophic loss
```

### Root Cause Hypothesis

SL/TP are hardcoded in the trade execution API call:

```vb
' WRONG APPROACH:
Private Async Function ExecuteTradeAsync(signal As TradeSignal) As Task
    Dim request = New TradeRequest With {
        .Symbol = _contractId,
        .Quantity = _quantity,
        .EntryPrice = signal.EntryPrice,
        .StopLoss = 0.00,      ← ❌ Hardcoded to zero
        .TakeProfit = 0.00     ← ❌ Hardcoded to zero
    }

    Await _api.PlaceTrade(request)
End Function
```

**Correct Approach:**
```vb
' CORRECT APPROACH:
Private Async Function ExecuteTradeAsync(signal As TradeSignal) As Task
    ' Step 1: Place trade WITHOUT SL/TP
    Dim tradeId = Await _api.PlaceTrade(New TradeRequest With {
        .Symbol = _contractId,
        .Quantity = _quantity,
        .EntryPrice = signal.EntryPrice
        ' Note: NO SL/TP in request
    })

    ' Step 2: Apply SL/TP AFTER trade opens (in separate call)
    Await _api.SetStopLoss(tradeId, signal.StopLossPrice)
    Await _api.SetTakeProfit(tradeId, signal.TakeProfitPrice)

    ' Step 3: Persist to database (so if we crash, we can reapply on restart)
    Await _database.SaveTradeProtection(tradeId, signal.StopLossPrice, signal.TakeProfitPrice)
End Function
```

### Impact

- 🔴 **CRITICAL:** Trades without SL = unlimited downside risk
- 🔴 **Cascading Risk:** One unprotected trade can blow account
- 🔴 **Daily Loss Limit Useless:** Risk Guard can't stop losses if trade has no SL
- 🔴 **App Crash:** If app reconnects, SL/TP might not be reapplied
- 🔴 **User Liability:** User thinks they're protected but they're not

### User Impact

Scenario:
```
User starts AI Trade with:
  - Account: $100,000
  - Daily Loss Limit: $10,000
  - Risk per Trade: 2% ($2,000)

Trade 1: Enters LONG at 4532.75, SL should be 4522.75
         API call: SL = 0.00 (BUG)
         Market drops to 4510 → Trade loses $9,100 (unprotected!)
         Daily loss limit exceeded, but trade still open

User is now at risk of account wipeout.
```

---

## Root Cause Analysis

### Why This Happened

1. **Incomplete Implementation:**
   - Initial API integration assumed SL/TP in trade request
   - TopStep API doesn't accept SL/TP in initial trade call
   - Code never updated to handle two-phase approach

2. **Missing Error Handling:**
   - No validation that SL/TP were actually applied
   - No database persistence for risk parameters

3. **No Recovery Logic:**
   - App crashes → trades lose SL/TP
   - No way to reapply SL/TP on reconnection

---

## Requirements

### A. Two-Phase Trade Execution

**Phase 1: Place Trade (Entry Only)**
```vb
' Place order WITHOUT SL/TP
Dim tradeRequest = New TradeRequest With {
    .Symbol = _contractId,
    .Quantity = _quantity,
    .Direction = IIf(_signal = LONG, "BUY", "SELL")
}

Dim tradeResponse = Await _topStepApi.PlaceTrade(tradeRequest)
Dim tradeId = tradeResponse.TradeId
```

**Phase 2: Set Risk Protection (Post-Trade)**
```vb
' Apply SL and TP in separate calls (after entry confirmed)
Try
    Await _topStepApi.SetStopLoss(tradeId, _signal.StopLossPrice)
    Await _topStepApi.SetTakeProfit(tradeId, _signal.TakeProfitPrice)
Catch ex As Exception
    ' Critical: if SL/TP fail, we must cancel the trade
    Await _topStepApi.CancelTrade(tradeId)
    Throw  ' Re-throw to user
End Try
```

### B. Database Persistence

Store SL/TP in database so they can be reapplied if app crashes:

```vb
' Schema:
CREATE TABLE TradeProtection (
    TradeId VARCHAR(50) PRIMARY KEY,
    ContractId VARCHAR(50),
    StopLossPrice DECIMAL(10, 2),
    TakeProfitPrice DECIMAL(10, 2),
    CreatedAt DATETIME
)

' Save after setting SL/TP:
Await _database.SaveTradeProtection(New TradeProtection With {
    .TradeId = tradeId,
    .ContractId = _contractId,
    .StopLossPrice = _signal.StopLossPrice,
    .TakeProfitPrice = _signal.TakeProfitPrice,
    .CreatedAt = DateTime.UtcNow
})
```

### C. Recovery on Startup

On app startup, reapply SL/TP for any open trades:

```vb
' On AppBootstrapper initialization:
Private Async Sub RecoverTradeProtection()
    ' Get all open trades from API
    Dim openTrades = Await _topStepApi.GetOpenTrades()

    ' Get their protections from database
    For Each trade In openTrades
        Dim protection = Await _database.GetTradeProtection(trade.Id)

        If protection Is Nothing Then
            ' Protection missing → critical issue
            Await _alertService.SendAlert(
                $"Trade {trade.Id} has NO stop loss! Setting immediately.")

            ' Attempt to set SL/TP
            Await _topStepApi.SetStopLoss(trade.Id, protection.StopLossPrice)
            Await _topStepApi.SetTakeProfit(trade.Id, protection.TakeProfitPrice)
        End If
    Next
End Sub
```

### D. Validation & Guards

Add validation everywhere SL/TP are used:

```vb
' Guard: Prevent trade execution without valid SL/TP
Private Function ValidateRiskParameters() As Boolean
    If _signal.StopLossPrice = 0 Then
        Throw New InvalidOperationException("Stop Loss not set!")
    End If

    If _signal.TakeProfitPrice = 0 Then
        Throw New InvalidOperationException("Take Profit not set!")
    End If

    Return True
End Function

' Guard: Ensure SL is beyond entry (not inside)
Private Function IsValidStopLoss(entryPrice As Decimal, slPrice As Decimal, direction As String) As Boolean
    If direction = "BUY" Then
        Return slPrice < entryPrice  ' SL must be below entry for LONG
    Else
        Return slPrice > entryPrice  ' SL must be above entry for SHORT
    End If
End Function
```

### E. Logging & Audit Trail

Log every SL/TP operation for debugging:

```
[14:35:22] Trade-001: Placing order LONG 4 contracts MESH26
[14:35:23] Trade-001: Order confirmed, ID = TOPSTEP-12345
[14:35:23] Trade-001: Setting SL = 4522.75
[14:35:24] Trade-001: SL set successfully
[14:35:24] Trade-001: Setting TP = 4542.75
[14:35:25] Trade-001: TP set successfully
[14:35:25] Trade-001: Persisting to database
[14:35:25] Trade-001: ✓ Trade protection COMPLETE (SL=4522.75, TP=4542.75)
```

---

## Implementation Plan

### Phase 1: Diagnosis & Validation (2 tokens)

**Duration:** 1 day

1. **Code Audit:**
   - Find where `SetStopLoss()` and `SetTakeProfit()` are called
   - Verify they're being called post-trade (not in initial request)
   - Check error handling

2. **API Verification:**
   - Confirm TopStep API structure
   - Does it support SL/TP in initial request? (probably not)
   - What's the correct two-phase approach?

3. **Log Analysis:**
   - Pull logs from production
   - Find other trades with SL/TP = 0.00
   - Determine how widespread the issue is

### Phase 2: Fix Trade Execution Flow (4 tokens)

**Duration:** 2-3 days

1. **TradeExecutionService.vb Refactor:**
   - Split into two phases: PlaceTrade() + SetRiskProtection()
   - Add error handling (if SL/TP fail, cancel trade)
   - Add validation guards

2. **Update AiTradingViewModel:**
   - Ensure SL/TP are calculated before execution
   - Log all risk parameters before sending

3. **Database Integration:**
   - Create TradeProtection table
   - Implement SaveTradeProtection()
   - Implement GetTradeProtection()

4. **Error Handling:**
   - Catch exceptions in SetStopLoss()
   - If SL/TP fails, cancel trade immediately
   - Notify user with clear error message

### Phase 3: Recovery Mechanism (3 tokens)

**Duration:** 1-2 days

1. **AppBootstrapper Startup Logic:**
   - On app start, call RecoverTradeProtection()
   - Check all open trades for missing SL/TP
   - Reapply if missing

2. **Recovery Logging:**
   - Log which trades had missing protection
   - Log successful reapplication
   - Log failures (need manual intervention)

3. **Alert System:**
   - If trade missing SL/TP, alert user immediately
   - Prevent user from opening new trades until recovered

### Phase 4: Validation & Testing (4 tokens)

**Duration:** 2-3 days

1. **Unit Tests:**
   - Test two-phase execution logic
   - Test validation guards
   - Test database persistence

2. **Integration Tests:**
   - Place trade → verify SL/TP are set (check API)
   - Simulate app crash → verify recovery on restart
   - Test with all 4 contracts (MES, MNQ, MGC, MCL)

3. **Stress Tests:**
   - Open 10 trades rapidly → verify all have SL/TP
   - Monitor trade execution performance
   - Check database write throughput

4. **Audit Tests:**
   - Verify logs show complete audit trail
   - Compare database records vs API records
   - Ensure no orphaned trades

### Phase 5: Documentation & Deployment (2 tokens)

**Duration:** 1 day

1. **Code Comments:**
   - Document why two-phase approach is needed
   - Explain recovery logic

2. **Runbook:**
   - Document what to do if recovery fails
   - Step-by-step manual fix procedure

3. **Release Notes:**
   - Communicate fix to users
   - Explain new safety mechanisms

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/Trading/TradeExecutionService.vb` | CRITICAL - Refactor two-phase execution |
| `src/TopStepTrader.Services/Trading/RiskGuardService.vb` | UPDATE - Validate SL/TP |
| `src/TopStepTrader.Data/Repositories/TradeProtectionRepository.vb` | NEW - Database persistence |
| `src/TopStepTrader.Data/TradeProtection.sql` | NEW - Database schema |
| `src/TopStepTrader.UI/AppBootstrapper.vb` | UPDATE - Add recovery logic on startup |
| `src/TopStepTrader.API/Responses/TradeExecutionResult.vb` | UPDATE - Ensure SL/TP in response |

---

## Acceptance Criteria

- [ ] All new trades have SL/TP set correctly (verified in API)
- [ ] SL/TP persisted to database after every trade
- [ ] Recovery logic runs on app startup
- [ ] Audit logs show complete SL/TP trail
- [ ] No trade executes with SL/TP = 0.00
- [ ] If SL/TP setting fails, trade is cancelled
- [ ] Validation guards prevent invalid SL/TP values
- [ ] Integration test: place trade → crash → restart → SL/TP recovered
- [ ] Performance: trade execution completes in < 2 seconds (including SL/TP)
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] All existing trades audited and fixed if needed

---

## Blocking Notes

⏳ **This ticket must be completed ASAP:**
- Risk severity: CRITICAL
- Recommended: Start immediately, treat as P0

🚀 **This ticket unblocks:**
- Full confidence in Risk Guard feature
- Safe deployment of AI Trade to production

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Two-phase (vs embedded SL/TP)** | Safe ✅ | More API calls ❌ |
| **Database persistence (vs memory only)** | Survives crashes ✅ | More complexity ❌ |
| **Recovery on startup (vs manual fix)** | Automatic ✅ | Recovery might fail ❌ |
| **Cancel trade if SL/TP fail** | Prevents naked trades ✅ | User loses opportunity ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **API doesn't support post-trade SL/TP** | Verify with TopStep support in Phase 1 |
| **Recovery finds orphaned trades** | Manual audit, implement recovery logic carefully |
| **Database connection fails** | If DB down, still allow trading (SL/TP best effort) |
| **Race condition: trade exits before SL set** | Unlikely (< 1 second between entry and SL), but monitor |

---

## Related Tickets

- **TICKET-004:** Position Sizing — Uses SL in calculation
- **TICKET-010 (this):** Stop Loss Bug (CRITICAL)
- **TICKET-009:** Trailing TP — Complements SL protection
- **TICKET-003:** Risk Guard (related risk management)

---

## Production Impact Assessment

### If Fixed:
- ✅ Trades protected with stop losses
- ✅ Daily loss limits actually work
- ✅ User confidence restored

### If NOT Fixed:
- 🔴 **CRITICAL:** Unprotected trades (unlimited downside)
- 🔴 Risk Guard feature useless
- 🔴 Account blowup risk
- 🔴 Regulatory exposure (if this was disclosed to users)

**Recommendation:** Treat as blocker for production deployment.

---

## Success Metrics

- ✅ 100% of new trades have valid SL/TP
- ✅ Zero trades with SL/TP = 0.00
- ✅ Recovery succeeds 100% of the time
- ✅ Trade execution completes within 2 seconds
- ✅ User confidence: "I trust the stop losses now"

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Claude Sonnet 4.6
**Token Estimate:** 18 (diagnosis 2 + fix 4 + recovery 3 + validation 4 + deployment 2 + buffer 3 due to criticality)
**Severity:** 🔴 CRITICAL (trade risk)
**Status:** For Development. CRITICAL RISK. Data shows SL/TP sometimes zero. Affects all trades. Urgent fix required.

---

## Progress Tracking

### Phase 1: Diagnosis & Validation
- [ ] Code audit completed
- [ ] Root cause identified and documented
- [ ] API verification completed
- [ ] Log analysis completed

### Phase 2: Fix Trade Execution Flow
- [ ] TradeExecutionService.vb refactored (two-phase approach)
- [ ] Database persistence (TradeProtection table) implemented
- [ ] Error handling & validation guards added
- [ ] Audit logging implemented

### Phase 3: Recovery Mechanism
- [ ] AppBootstrapper recovery logic added
- [ ] Recovery on startup tested
- [ ] Alert system for orphaned trades integrated

### Phase 4: Validation & Testing
- [ ] Unit tests written and passing
- [ ] Integration tests completed
- [ ] Stress tests (rapid trading) passed
- [ ] Audit tests verified (logs match API + database)

### Phase 5: Documentation & Deployment
- [ ] Code comments documented
- [ ] Recovery runbook created
- [ ] Release notes prepared
- [ ] All trades verified with valid SL/TP

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (pending assignment)
**Blocker:** None
**Next Concrete Action:** Code audit to confirm bug scope and extent

---

## Immediate Action Required

1. **Today:** Code audit to confirm bug scope
2. **This week:** Implement fix (Phases 1-4)
3. **Acceptance criteria:** All trades tested with SL/TP validation
4. **Deployment:** Only deploy when 100% of trades have valid protections

This is the highest-priority bug in the system. Stop loss protection is non-negotiable for trading safety.
