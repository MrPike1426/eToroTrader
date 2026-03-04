# TICKET-007: Multi-Strategy Concurrent Trading

**Status:** Backlog
**Priority:** Medium
**Severity:** Medium
**Assigned To:** Copilot
**Due Date:** 01/05/2026
**Tokens:** 20
**Labels:** `feature,ai-trade,multi-strategy,requires-design-review`

---

## Problem Statement

Currently, users can only run **one strategy at a time** in AI Trade tab. This limits trading opportunity — different strategies work best on different contracts and conditions.

**Ideal Workflow:**
1. Backtest all 8 strategies on MESH26 (using TICKET-006)
2. Identify top 3 strategies (e.g., EMA/RSI: 62%, Double Bottom: 65%, RSI Reversal: 58%)
3. Click "Trade" button in backtest results
4. **All 3 strategies run simultaneously** on MESH26
5. Each generates independent signals, but position management is unified
6. App coordinates entry/exit across all strategies

**Current Limitations:**
- ❌ Can't run multiple strategies on same contract
- ❌ No coordination between strategies (entry/exit conflicts)
- ❌ Can't diversify signal sources (one bad strategy tanks the session)
- ❌ Manual switching between strategies tedious

**Business Value:**
- **Diversified signals:** Multiple perspectives reduce false signals
- **Better risk management:** If one strategy fails, others may succeed
- **Higher trade frequency:** Multiple signals = more opportunities
- **Professional approach:** Many pro traders use multiple systems concurrently

**Risk Considerations:**
- ⚠️ What if strategies generate conflicting signals (both LONG and SHORT)?
- ⚠️ How to manage position overlap (don't open multiple positions)?
- ⚠️ How to handle stop losses and exits across strategies?

---

## Requirements

### A. Design Phase (Requires Stakeholder Review)

**Critical Questions to Answer:**

1. **Conflict Resolution:**
   - If Strategy A says LONG and Strategy B says SHORT, what happens?
   - Options:
     - A) Don't trade (require consensus, e.g., 2/3 agree)
     - B) Take strongest signal (Strategy A has higher confidence)
     - C) Trade both (separate accounts? separate positions?)
     - D) Vote (whichever strategy has higher historical win rate)

2. **Position Management:**
   - Can we have multiple overlapping positions from different strategies?
   - Or must we limit to 1 position at a time (first strategy wins)?
   - What if Strategy A wants to exit but Strategy B wants to hold?

3. **Risk Management:**
   - How to calculate position size for multi-strategy setup?
   - If total risk = 2% × 3 strategies = 6%, is that acceptable?
   - Or should risk be shared: 2% ÷ 3 = 0.67% per strategy?

4. **Exit Logic:**
   - Individual exits? (Each strategy manages its own exit)
   - Consensus exits? (Exit only if X strategies agree)
   - Time-based? (Exit if any strategy is in trade > N minutes)

**Recommendation:** Schedule 30-minute design review with user before proceeding.

### B. Proposed Implementation (After Design Approved)

Once design decisions made:

```
Multi-Strategy Trade Flow:
═════════════════════════════════════════════════════════

1. USER CONFIGURATION
   └─→ Select 2-3 strategies to trade concurrently
   └─→ Set "conflict resolution" mode (consensus, strongest, etc.)
   └─→ Set risk per strategy or total risk

2. SIGNAL GENERATION (Parallel)
   └─→ Strategy A evaluates → Signal LONG (confidence 70%)
   └─→ Strategy B evaluates → Signal SHORT (confidence 55%)
   └─→ Strategy C evaluates → Signal NONE (no signal)

3. CONFLICT RESOLUTION
   └─→ Strategies = [LONG, SHORT, NONE]
   └─→ Consensus needed? → 2 out of 3 agree → SKIP TRADE
   └─→ Strongest signal? → LONG (70%) wins → BUY

4. POSITION MANAGEMENT
   └─→ If mode = "individual positions":
       - Open Position A (Strategy A's entry)
       - Skip Position B (Strategy B disagreed)
       - Wait for Position C (no signal)
   └─→ If mode = "single position":
       - Only 1 position open at a time
       - Strategies manage independently (separate SL/TP)

5. MONITORING & EXIT
   └─→ Each strategy monitors its own position
   └─→ Strategy A's SL = 4532.75 (10 points below entry)
   └─→ Strategy B's SL = 4530.50 (15 points below entry)
   └─→ If Strategy A hits SL → Strategy A exits (Strategy B may hold)
   └─→ If both hit SL → Position closed

6. LOGGING
   └─→ "14:35:22 [Strategy A] LONG signal (70% confidence)"
   └─→ "14:35:22 [Strategy B] SHORT signal (55% confidence)"
   └─→ "14:35:22 [CONSENSUS] Skipped trade (conflicting signals)"
```

### C. UI Integration

**Backtest Tab (TICKET-006):**
```
Results Table → Click Row → "Trade This Strategy" or "Trade Top 3"

Example:
  │ EMA/RSI (62%, SHARPE 1.85) │ [Trade] [Trade Top 3]
  │ Double Bottom (65%, SHARPE 2.10) │
  │ RSI Reversal (58%, SHARPE 1.23) │
```

**AI Trade Tab:**
```
Active Strategies:
  ☑ EMA/RSI Combined
  ☑ Double Bottom
  ☑ RSI Reversal

Conflict Resolution Mode: [Consensus ▼]
  (Require 2/3 agree before trading)

Status: Running (3 strategies active)
  [Stop]
```

### D. Data Structures

```vb
Public Class ConcurrentStrategyConfig
    Public Property SelectedStrategies As List(Of String)
    Public Property ConflictResolutionMode As ConflictMode
    Public Property RiskPerStrategy As Decimal
    Public Property RequiredConsensus As Integer  ' 2 of 3, 3 of 4, etc.
End Class

Public Enum ConflictMode
    Consensus      ' Require 2+ strategies to agree
    StrongestSignal ' Take highest confidence
    VotingPower    ' Weight by historical win rate
    SinglePosition ' First strategy wins, others wait
End Enum

Public Class MultiStrategySignal
    Public Property Signals As Dictionary(Of String, TradeSignal)
    Public Property ResolvedSignal As TradeSignal
    Public Property ConflictDescription As String  ' Why it was resolved this way
End Class
```

---

## Implementation Plan

### Phase 1: Design Review & Architecture (2 tokens)

**Duration:** 1 day

1. **Stakeholder Meeting:**
   - Review design questions (conflict resolution, position management)
   - Document decisions
   - Identify risks and mitigations

2. **Architecture Design:**
   - Design multi-strategy orchestration service
   - Define conflict resolution algorithms
   - Plan signal coordination logic

3. **Create Design Document:**
   - Document all design decisions
   - Create sequence diagrams
   - Define state machine for multi-strategy trading

### Phase 2: Core Implementation (12 tokens)

**Duration:** 4-5 days

1. **MultiStrategyCoordinator.vb:**
   ```vb
   Public Class MultiStrategyCoordinator
       Function EvaluateAllStrategies() As MultiStrategySignal
           ' Get signals from all active strategies
           ' Apply conflict resolution
           ' Return unified signal (or no-trade)
       End Function

       Function ResolveConflict(signals As List(Of TradeSignal)) As TradeSignal
           Select Case _conflictMode
               Case ConflictMode.Consensus
                   Return ResolveByConsensus(signals)
               Case ConflictMode.StrongestSignal
                   Return ResolveByConfidence(signals)
               Case Else
                   Return TradeSignal.None
           End Select
       End Function
   End Class
   ```

2. **MultiStrategyPositionManager.vb:**
   - Track positions per strategy
   - Coordinate exits
   - Handle overlapping positions

3. **AiTradingViewModel Updates:**
   - Add multi-strategy mode toggle
   - Add active strategy list
   - Add conflict resolution mode selector

4. **AiTradingView.xaml Updates:**
   - Add "Active Strategies" panel
   - Add conflict resolution dropdown
   - Show coordinated signals in output

5. **Error Handling:**
   - Handle strategy failures gracefully
   - Log all signal decisions
   - Alert user if strategies conflict

### Phase 3: Integration & Testing (4 tokens)

**Duration:** 2-3 days

1. **Unit Tests:**
   - Test consensus logic (2 of 3 agree)
   - Test strongest signal logic
   - Test conflict detection

2. **Integration Tests:**
   - Run 2 strategies concurrently
   - Run 3 strategies concurrently
   - Verify positions don't overlap unexpectedly
   - Verify exits coordinate correctly

3. **Stress Tests:**
   - Run for 4-8 hours with multiple strategies
   - Monitor for memory leaks
   - Check signal generation rate

4. **User Acceptance:**
   - Test with real market data
   - Verify outputs match expectations
   - Identify edge cases

### Phase 4: Documentation & Deployment (2 tokens)

**Duration:** 1 day

1. **Update Help Text:**
   - Document multi-strategy workflow
   - Explain conflict resolution modes
   - Provide best practices

2. **Release Notes:**
   - Document feature
   - Note limitations
   - Suggest use cases

---

## Affected Files

| File | Change |
|------|--------|
| `src/TopStepTrader.Services/Trading/MultiStrategyCoordinator.vb` | NEW |
| `src/TopStepTrader.Services/Trading/MultiStrategyPositionManager.vb` | NEW |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | UPDATE - Add multi-strategy mode |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | UPDATE - Add strategy selector |
| `src/TopStepTrader.UI/Views/BacktestView.xaml` | UPDATE - Add "Trade Multiple" buttons |
| `src/TopStepTrader.Core/Models/ConcurrentStrategyConfig.vb` | NEW |

---

## Acceptance Criteria

- [ ] Multiple strategies can run concurrently on same contract
- [ ] Conflict resolution works (consensus, strongest signal, etc.)
- [ ] Position management prevents unexpected overlaps
- [ ] Each strategy has independent entry/exit logic
- [ ] Logging shows all signals and resolution decisions
- [ ] No memory leaks during 8-hour concurrent session
- [ ] UI clearly shows which strategies are active
- [ ] Backtest tab integration (select multiple strategies to trade)
- [ ] Build succeeds: 0 errors, 0 warnings
- [ ] User documentation complete and clear

---

## Blocking Notes

⏳ **This ticket requires completion of:**
- **TICKET-006:** Backtest Page Rewrite — Need backtest results to select strategies

🚀 **This ticket unblocks:**
- (None currently, but enables advanced multi-system trading)

---

## Design Decisions & Trade-offs

| Decision | Trade-off |
|----------|-----------|
| **Consensus (vs strongest signal)** | Safer ✅ | Fewer trades ❌ |
| **Individual positions (vs single)** | More trades ✅ | Complex management ❌ |
| **Per-strategy risk (vs shared)** | Flexible ✅ | Harder to predict total risk ❌ |
| **Implement now (vs MVP later)** | Full feature ✅ | More complex ❌ |

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Conflicting signals (A=LONG, B=SHORT)** | Consensus mode prevents uncertain trades |
| **Position overlap bugs** | Careful position tracking, thorough testing |
| **Total risk exceeds limits** | Calculate total risk before opening position |
| **One strategy fails, breaks others** | Try/catch around each strategy evaluation |
| **Users confused by multi-strategy mode** | Clear documentation and sensible defaults |

---

## Related Tickets

- **TICKET-006:** Backtest Page — Feeds strategies to trade
- **TICKET-007 (this):** Multi-Strategy Concurrent Trading
- **TICKET-011:** Confidence Selector — Helps refine signals

---

## Future Enhancements (Phase 2)

- [ ] Machine learning to learn best conflict resolution per contract
- [ ] Separate account management (different risk per strategy)
- [ ] Strategy weighting (give more weight to historically better strategies)
- [ ] Correlated signal detection (avoid strategies that generate similar signals)
- [ ] Walk-forward testing (optimize strategy selection over time)

---

## Success Metrics

- ✅ Multiple strategies generate signals correctly
- ✅ Conflict resolution prevents false signals
- ✅ Positions don't overlap unexpectedly
- ✅ Total risk stays within limits
- ✅ Users report "diversified signal sources improve results"

---

**Created:** 2026-03-01
**Last Updated:** 2026-03-01
**Model:** Copilot
**Token Estimate:** 20 (design 2 + implementation 12 + testing 4 + deployment 2)
**Severity:** Medium (enhances but not critical)
**Status:** Backlog. Needs stakeholder review before coding. Risk: competing orders. Opportunity: diversified signals.

---

## Progress Tracking

### Phase 1: Design Review & Architecture
- [ ] Stakeholder design review meeting completed
- [ ] Conflict resolution strategy decided (consensus/strongest/voting)
- [ ] Position management approach documented
- [ ] Risk allocation strategy finalized
- [ ] Architecture design document created

### Phase 2: Core Implementation
- [ ] MultiStrategyCoordinator.vb created
- [ ] Conflict resolution logic (consensus, strongest signal, voting) implemented
- [ ] MultiStrategyPositionManager.vb created
- [ ] Signal coordination and position tracking implemented
- [ ] Error handling for strategy failures added

### Phase 3: Integration & UI
- [ ] AiTradingViewModel updated (multi-strategy mode toggle)
- [ ] AiTradingView.xaml strategy selector added
- [ ] BacktestView.xaml "Trade Multiple" buttons added
- [ ] Coordinated signal display in output log

### Phase 4: Testing & Validation
- [ ] Unit tests (consensus logic, conflict detection)
- [ ] Integration tests (2-strategy, 3-strategy runs)
- [ ] Stress tests (8+ hours concurrent trading)
- [ ] Position overlap verification
- [ ] Memory leak checks

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** Not started (blocked on design review)
**Blocker:** Requires stakeholder design review meeting to finalize approach
**Next Concrete Action:** Schedule 30-minute design review with user

---

## Next Steps

### Immediate (After Approval):

1. **Schedule Design Review** (ASAP)
   - User availability: 30 minutes
   - Topics: Conflict resolution, position management, risk allocation
   - Document decisions in design document

2. **Technical Spike** (1 day)
   - Build minimal multi-strategy coordinator
   - Test parallel signal evaluation
   - Estimate true complexity

### During Implementation:

Phase 1 (Design Review) — 1 day
Phase 2 (Implementation) — 4-5 days
Phase 3 (Testing) — 2-3 days
Phase 4 (Documentation) — 1 day
**Total: ~1.5-2 weeks**

### Post-Implementation:

1. **Live testing** with real strategies
2. **User feedback** on conflict resolution
3. **Performance profiling** (parallel evaluation speed)
4. **Consider Phase 2 enhancements** (learning, weighting, etc.)

---

## Design Review Preparation

Before scheduling review, prepare:

```
Questions for Stakeholder:
═════════════════════════════════════════════════════════

1. CONFLICT RESOLUTION
   If Strategy A says LONG and Strategy B says SHORT:
   - Require consensus (need 2/3 agree)?
   - Take strongest signal (use confidence score)?
   - Use voting (weight by win rate)?

2. POSITION MANAGEMENT
   Can we have multiple positions from different strategies?
   Or must we limit to 1 position at a time?

3. RISK PER STRATEGY
   If trading 3 strategies with 2% risk each:
   - Total risk = 6%? (or reduce to 2% ÷ 3 = 0.67% each?)

4. EXIT LOGIC
   If Strategy A hits stop loss but Strategy B wants to hold:
   - Exit all positions?
   - Exit only Strategy A's position?
   - Let each strategy manage independently?

5. SIGNAL TRUST
   Do you trust all 8 strategies equally?
   Or should some be weighted higher (by historical performance)?
```
