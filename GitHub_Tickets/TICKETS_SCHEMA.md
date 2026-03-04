# TICKETS.csv Schema Documentation

## Overview
This document defines the structure and best practices for managing the TICKETS.csv file as a production-grade issue tracking system.

---

## Column Definitions

### **Core Identity Columns**

| Column | Type | Format | Purpose |
|--------|------|--------|---------|
| **TicketID** | Text | `TICKET-NNN` | Unique identifier (immutable) |
| **Status** | Enum | See below | Current workflow state |
| **Priority** | Enum | `High`, `Medium`, `Low` | Work queue order (user sets) |
| **Severity** | Enum | `Critical`, `High`, `Medium`, `Low` | Impact level (user sets) |

### **Status Values (Workflow States)**

```
Backlog        → Not ready to start (blockers/dependencies exist)
For Development→ Ready to code (spec complete, no blockers)
In Development → Currently being worked on
SIT Testing    → Unit testing complete, awaiting user system testing
Complete       → Delivered and user-approved
Cancelled      → Intentionally skipped (see Notes for reason)
```

**Expected Flow:**
```
Backlog → For Development → In Development → SIT Testing → Complete
         ↓ (if abandoned)
      Cancelled
```

---

### **Work Description Columns**

| Column | Type | Purpose |
|--------|------|---------|
| **Title** | Text | Concise ticket name (< 80 chars) |
| **Description** | Text (CSV-quoted) | Full requirement/spec (multiline allowed) |
| **Labels** | CSV-quoted list | Categorization: `feature`, `bug`, `optimization`, `regression`, etc. |
| **Notes** | CSV-quoted text | Implementation details, assumptions, risks (updated as work progresses) |

### **Assignment & Team Columns**

| Column | Type | Purpose |
|--------|------|---------|
| **AssignedTo** | Text | Developer/agent assigned (e.g., "Claude Sonnet 4.6", "Copilot") |
| **Attempts** | Integer | Number of implementation attempts (0 = untouched, increments on restart) |

---

### **Timeline & Planning Columns**

| Column | Type | Format | Purpose |
|--------|------|--------|---------|
| **StartDate** | Date | `YYYY-MM-DD` | When development actually began (null if not started) |
| **DueDate** | Date | `DD/MM/YYYY` | Hard deadline for completion (user sets) |
| **TargetCompletionDate** | Date | `YYYY-MM-DD` | Estimated completion (dev team sets, may differ from DueDate) |

**When to Fill:**
- **StartDate**: Populate when status changes to "In Development"
- **DueDate**: Set by user during ticket creation/refinement
- **TargetCompletionDate**: Set by developer based on token estimate and sprint velocity

---

### **Effort Tracking Columns**

| Column | Type | Purpose | Notes |
|--------|------|---------|-------|
| **TokenEstimate** | Integer or "~NNN-Range" | Estimated tokens to complete | From Claude token planning (8-12 tokens typical) |
| **TokensBurned** | Integer | Actual tokens consumed | Track for velocity learning. Start at 0, increment as work completes. |

**Token Tracking Formula:**
```
Velocity = Average(TokensBurned / TokenEstimate) over last 5 completed tickets
Burndown = Sum(TokensBurned) per sprint
Accuracy = TokenEstimate vs. TokensBurned variance
```

---

### **Dependency Tracking Columns**

| Column | Type | Format | Purpose |
|--------|------|--------|---------|
| **BlockedBy** | CSV-quoted list | `TICKET-NNN,TICKET-NNN` | Tickets that must complete first (comma-separated) |
| **Blocks** | CSV-quoted list | `TICKET-NNN,TICKET-NNN` | Tickets waiting on this one (comma-separated) |

**Why Both Directions?**
- **BlockedBy**: Tells you "why is this ticket red?" (prerequisite not done)
- **Blocks**: Tells you "what will unblock?" (downstream impact)

**Example:**
```
TICKET-011: BlockedBy = TICKET-006   (can't code confidence selector until backtest is done)
TICKET-006: Blocks = TICKET-005      (fixing backtest unblocks strategy mismatch investigation)
```

---

### **Metadata Columns**

| Column | Type | Purpose |
|--------|------|---------|
| **LastUpdated** | ISO 8601 DateTime | Last modification timestamp (auto-update) |

---

## Status Transition Rules

### **Backlog → For Development**
**Entry Criteria:**
- ✅ Specification complete (no open questions)
- ✅ Dependencies resolved (BlockedBy is empty or all are Complete)
- ✅ Token estimate provided
- ✅ Assigned to a developer
- ✅ Due date set

**Action:** User moves ticket to "For Development" when queuing for implementation.

---

### **For Development → In Development**
**Entry Criteria:**
- ✅ Developer starts work
- ✅ StartDate set to current date

**Action:** Developer sets status + StartDate when beginning work.

---

### **In Development → SIT Testing**
**Entry Criteria:**
- ✅ Code complete
- ✅ Build passes: 0 errors, 0 warnings
- ✅ Unit tests pass
- ✅ TokensBurned populated
- ✅ Notes updated with implementation summary

**Action:** Developer moves to SIT Testing and awaits user approval.

---

### **SIT Testing → Complete**
**Entry Criteria:**
- ✅ User has conducted full System Integration Testing
- ✅ All functionality verified in running app
- ✅ User approves ticket (via Notes addition)

**Action:** User moves to Complete after SIT sign-off.

---

### **Any → Cancelled**
**Entry Criteria:**
- ✅ Reason documented in Notes (e.g., "DUPLICATE", "BLOCKED_BY_DEPENDENCY", "OUT_OF_SCOPE")

**Action:** User cancels with explanation. TargetCompletionDate may be cleared.

---

## Example Entries

### **Backlog Ticket (Not Ready)**
```
TICKET-003,Backlog,Low,Low,Market Data Real-Time Streaming,...,Copilot,,15/05/2026,2026-05-15,12,0,feature,future,market-data,streaming,,,Placeholder. Awaiting TopStep API capability confirmation.,0,2026-02-27T15:30:00
```
- No StartDate (not started)
- BlockedBy is empty (but has API dependency documented in Notes)
- TokensBurned = 0

---

### **For Development Ticket (Queued)**
```
TICKET-010,For Development,High,Critical,Fix Stop Loss Hardcoding Bug,...,Claude Sonnet 4.6,,10/03/2026,2026-03-10,18,0,bug,critical,risk-management,stop-loss,,,🔴 CRITICAL RISK. Data shows SL/TP sometimes zero...,0,2026-02-27T15:30:00
```
- No StartDate yet (awaiting assignment pickup)
- TokenEstimate = 18 (increased from initial 10)
- TokensBurned = 0

---

### **SIT Testing Ticket (Awaiting User Approval)**
```
TICKET-021,SIT Testing,Low,Medium,Default Practice Account Displayed on Load,...,Claude Haiku 4.5,2026-03-01,15/04/2026,2026-03-01,3,3,bug,ui,combobox,dashboard,ai-trade,,,✅ UNIT TESTING COMPLETE: (1) Fixed DashboardViewModel.vb...,1,2026-03-01T00:00:00
```
- StartDate = 2026-03-01 (when work began)
- TokenEstimate = 3, TokensBurned = 3 (accurate estimate!)
- Status = "SIT Testing" (waiting for user to validate)

---

### **Complete Ticket (Delivered)**
```
TICKET-001,Complete,High,Medium,Test Trade EMA/RSI Analysis,...,Copilot,2026-02-20,26/02/2026,2026-02-26,8,8,feature,completed,test-trade,trend-analysis,,,✅ Delivered. See TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md,1,2026-02-26T18:00:00
```
- StartDate & TargetCompletionDate populated (full cycle visibility)
- TokenEstimate = TokensBurned (perfect estimate)
- Status = "Complete" (user approved)

---

## Metrics & Queries

### **Velocity Calculation**
```sql
SELECT
  ROUND(AVG(TokensBurned / TokenEstimate), 2) as AvgVelocity,
  COUNT(*) as CompletedTickets
FROM TICKETS
WHERE Status = 'Complete'
AND LastUpdated > DATE_SUB(NOW(), INTERVAL 30 DAY)
```

### **Cycle Time**
```sql
SELECT
  TicketID,
  DATEDIFF(DAY, StartDate, TargetCompletionDate) as CycleTimeDays
FROM TICKETS
WHERE Status = 'Complete'
ORDER BY TicketID DESC
```

### **Blocker Status**
```sql
SELECT
  TicketID, BlockedBy, Status
FROM TICKETS
WHERE BlockedBy IS NOT NULL
  AND BlockedBy != ''
ORDER BY Priority DESC
```

### **Upcoming Deadlines**
```sql
SELECT
  TicketID, Title, DueDate, Status
FROM TICKETS
WHERE DueDate <= DATE_ADD(NOW(), INTERVAL 7 DAY)
  AND Status != 'Complete'
  AND Status != 'Cancelled'
ORDER BY DueDate ASC
```

---

## Best Practices

### **Do's ✅**
1. **Update StartDate immediately** when moving to "In Development"
2. **Keep BlockedBy/Blocks synchronized** — if A blocks B, then B is blocked by A
3. **Document assumptions in Notes** before starting work
4. **Track TokensBurned** at completion to improve future estimates
5. **Use Labels consistently** for reporting and filtering
6. **Review cycle time trends** to improve planning

### **Don'ts ❌**
1. ❌ Leave StartDate blank during active development
2. ❌ Skip unit testing before moving to SIT Testing
3. ❌ Create tickets without a token estimate
4. ❌ Leave blocked tickets in "For Development" status
5. ❌ Update DueDate during implementation (that's scope creep)
6. ❌ Merge multiple unrelated features into one ticket

---

## Integration with Workflow

### **Weekly Sprint Review**
```
1. Update TokensBurned for completed tickets
2. Move completed tickets Complete → remove from active list
3. Assess BlockedBy tickets — are dependencies actually complete?
4. Forecast next week's work based on velocity
5. Identify top 3 blockers and prioritize unblocking them
```

### **New Ticket Creation**
```
1. Create in Backlog status
2. Fill: Title, Description, AssignedTo, TokenEstimate, DueDate, Labels, BlockedBy
3. Get user approval before moving to "For Development"
4. Link related tickets in Notes
```

### **Status Promotion**
```
Backlog → For Development: User decision (ready to queue)
For Development → In Development: Dev decision (starting work now) + set StartDate
In Development → SIT Testing: Dev decision (code complete) + update Notes
SIT Testing → Complete: User decision (SIT approved)
Any → Cancelled: User decision (with reason in Notes)
```

---

## Ticket Markdown Specification Requirements

### **Mandatory Section: Progress Tracking**

Every open ticket markdown file MUST include a `## Progress Tracking` section to enable session continuity and allow resumption after token timeouts or disconnects.

**Purpose:**
- Track task completion within the ticket
- Enable quick context recovery if session drops
- Show user current status without asking

**Format:**

```markdown
## Progress Tracking

### Phase 1: [Phase Name]
- [x] Task 1 completed
- [x] Task 2 completed
- [ ] Task 3 pending
- [ ] Task 4 pending

### Phase 2: [Phase Name]
- [ ] Task 1 pending
- [ ] Task 2 pending

**Last Updated:** 2026-03-01 14:30 UTC
**Current Status:** Phase 1 complete (75%), Phase 2 starting
**Blocker:** None (or description if blocked)
**Next Concrete Action:** [Specific next step]
```

**Guidelines:**
- Use markdown checkboxes: `- [ ]` (unchecked) or `- [x]` (checked)
- Update after completing logical work chunks (not after every micro-task)
- Include timestamp in `Last Updated` (helps track when work was done)
- Include `Current Status` summary (% complete, what phase)
- Note any blockers that prevent progress
- State the next concrete action clearly
- Keep it concise (3-5 lines)

**Example from TICKET-010:**
```markdown
## Progress Tracking

### Phase 1: Diagnosis & Validation
- [x] Code audit completed
- [x] Root cause identified (SL/TP hardcoded)
- [ ] API verification in progress

### Phase 2: Fix Trade Execution Flow
- [ ] TradeExecutionService refactored
- [ ] Error handling added

**Last Updated:** 2026-03-01 16:45 UTC
**Current Status:** Phase 1 complete, Phase 2 design in progress
**Blocker:** None
**Next Concrete Action:** Start TradeExecutionService.vb refactor
```

### **Ticket Lifecycle & File Organization**

**Active Tickets (Open):**
- Status: `Backlog`, `For Development`, `In Development`, `SIT Testing`
- Location: `GitHub_Tickets/` folder
- Must have: Progress Tracking section
- Actively worked on: Updates progress section each session

**Completed Tickets:**
- Status: `Complete`
- Location: `GitHub_Tickets/Completed Tickets/` folder
- Progress Tracking: Finalized with final status
- Archived for reference

**Cancelled Tickets:**
- Status: `Cancelled`
- Location: `GitHub_Tickets/` folder
- Progress Tracking: Optional (reason for cancellation in Notes)

### **File Movement Process**

When a ticket reaches `Complete` status:

1. **Finalize Progress Tracking Section:**
   ```markdown
   ## Progress Tracking

   [All sections marked complete]

   **Last Updated:** 2026-03-01 18:00 UTC
   **Status:** ✅ COMPLETE
   **Final Notes:** [Brief summary of what was delivered]
   ```

2. **Move markdown file** to `GitHub_Tickets/Completed Tickets/` folder
   - Keeps active work folder clean
   - Completed tickets available for reference
   - Archive pattern: old tickets don't clutter current work

3. **Update TICKETS.csv** to reflect `Status: Complete`

4. **Optional:** Archive supporting files if they're large

---

## Future Enhancements

- [ ] Add `StoryPoints` column (for Scrum teams)
- [ ] Add `Component` column (group by Feature Area: AI Trade, Backtest, Dashboard)
- [ ] Add `RiskLevel` column (High, Medium, Low for technical risk)
- [ ] Add `TestCoverage` column (% unit test coverage required)
- [ ] Automate dependency graph generation (BlockedBy → visual pipeline)
- [ ] Track `CommitHash` (link to actual code changes)
- [x] Add `Progress Tracking` section (IMPLEMENTED 2026-03-01)
- [x] Establish Completed Tickets folder (IMPLEMENTED 2026-03-01)

---

**Version:** 1.1
**Last Updated:** 2026-03-01
**Owner:** Damia (User)
**Change Log:**
- 1.0 → 1.1: Added Progress Tracking requirement & Completed Tickets folder structure
