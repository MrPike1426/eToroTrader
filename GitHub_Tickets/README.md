# GitHub Tickets System

This folder contains the structured ticket system for TopStepTrader development.

## Files

- **TICKETS.csv** — Main ticket database (all active, backlog, and completed tickets)
- **README.md** — This file

## How to Use

### 1. View All Tickets

Open `TICKETS.csv` in:
- **Excel** — Visual editing with sorting/filtering
- **VS Code** — Quick text editing
- **Any text editor** — Raw CSV format

### 2. Workflow: Adding & Tracking Work

```
You:  Add new row with Status="Ready"
      ↓
You:  "TICKET-XXX is ready for implementation"
      ↓
Me:   Read TICKETS.csv → parse ticket
      ↓
Me:   Implement the feature
      ↓
You:  Update CSV: Status="Complete"
      ↓
You:  Commit: "feat: [title] (TICKET-XXX)"
```

### 3. Status Values

| Status | Meaning |
|--------|---------|
| **Ready** | Approved, ready for implementation |
| **In Progress** | Currently being worked on |
| **Complete** | ✅ Finished and tested |
| **Backlog** | Future work (not yet prioritized) |

### 4. Priority Levels

| Priority | Meaning |
|----------|---------|
| **High** | Urgent, blocker, or critical bug |
| **Medium** | Important but not blocking |
| **Low** | Nice to have, cosmetic |

### 5. Adding a New Ticket

Add a row to TICKETS.csv:

```csv
TICKET-XXX,Ready,High,"Feature Title","Detailed description. What? Why? Acceptance criteria.",Copilot,YYYY-MM-DD,~20K-Sonnet,"label1,label2","Any additional notes",0,YYYY-MM-DDTHH:MM:SS
```

**Required fields:**
- `TicketID` — Unique identifier (TICKET-001, TICKET-002, etc.)
- `Status` — Ready | In Progress | Complete | Backlog
- `Priority` — High | Medium | Low
- `Title` — One-line feature/bug summary
- `Description` — 1-3 sentences with context and acceptance criteria
- `DueDate` — YYYY-MM-DD format (e.g., 2026-04-15)
- `TokenEstimate` — AI token burn estimate + recommended model (e.g., `~5K-Haiku`, `~20K-Sonnet`, `~80K-Opus`)
- `Labels` — Comma-separated tags (e.g., "feature,ui,ai-trade")
- `Notes` — Additional context (optional)
- `Attempts` — Number of implementation attempts (0 = not started, 1+ = tried)
- `LastUpdated` — ISO 8601 timestamp (e.g., 2026-02-27T15:30:00)

### 6. Tracking Progress

**Attempts Column:**
- `0` = Not started yet
- `1` = First attempt (most tickets)
- `2+` = Multiple attempts required (complex bugs, debugging)

**LastUpdated Column:**
- Auto-update this timestamp whenever ticket status changes
- Format: `YYYY-MM-DDTHH:MM:SS` (ISO 8601)
- Example: `2026-02-27T15:30:00`

**When to increment Attempts:**
- ✅ Each time you start implementing a different approach
- ✅ When a build succeeds but fix doesn't work (like TICKET-019)
- ❌ Don't increment for minor tweaks to same approach

### 7. Updating Status After Completion

When work is done:

```
Before:  TICKET-XXX,Ready,High,...
After:   TICKET-XXX,Complete,High,...
```

Commit message: `git commit -m "feat: [Feature Title] (TICKET-XXX)"`

---

## Ticket Categories

### 🔴 Critical Bugs (Do First)
- TICKET-010: Stop Loss Hardcoding
- TICKET-016: Tab Switching Process Persistence
- TICKET-017: Complete Stop Button

### 🟡 Important Features (Next)
- TICKET-005: Backtest Strategy Mismatch
- TICKET-006: Backtest Complete Rewrite
- TICKET-009: Trailing Take Profit
- TICKET-011: Confidence Selector
- TICKET-013: Balance & P&L Display

### 🟢 Polish & Optimization (Later)
- TICKET-012: Bar Check Polling
- TICKET-014: Layout Redesign
- TICKET-015: Sound Alert

### 📋 Backlog (Future)
- TICKET-007: Multi-Strategy Trading (design review needed)
- TICKET-008: Strategy Confirmation Filters
- TICKET-003: Market Data Streaming
- TICKET-004: Risk Guard Module

---

## Key Notes

### Current Build State
- ✅ TICKET-001: Test Trade EMA/RSI (Complete)
- ✅ TICKET-002: Dashboard Balance History (Complete)

### Quick Wins (~5K–15K tokens — Haiku/Sonnet)
- TICKET-012: Fix 30s polling → 5min alignment
- TICKET-015: Add trade sound alert

### Medium Effort (~20K–50K tokens — Sonnet)
- TICKET-011: Confidence selector UI
- TICKET-013: Balance display
- TICKET-014: Layout redesign
- TICKET-016: Tab persistence fix

### High Effort (~50K–150K tokens — Sonnet/Opus)
- TICKET-006: Backtest complete rewrite
- TICKET-009: Trailing TP research + impl
- TICKET-007: Multi-strategy design (design review first)

---

## Communication

### Intake Template (paste into chat to create a ticket)

```
Create TICKET: [One-line title]
Priority: High / Medium / Low
Description: [1-2 sentence problem statement + acceptance criteria]
Labels: [relevant tags, comma-separated]
Notes: [optional — blockers, dependencies, known prior attempts]
```

Claude will respond with:
- **Token estimate** — approximate burn for the task (e.g., ~20K tokens)
- **Model recommendation** — Haiku / Sonnet / Opus
- **CSV row** — ready to paste into TICKETS.csv

### Implementation Workflow

When implementing a ticket:

1. **Post message:** "TICKET-XXX is ready" (or specific ticket ID)
2. **I read:** The CSV row for that ticket
3. **I estimate:** Token burn + model before starting
4. **I implement:** Based on description and acceptance criteria
5. **You verify:** Check the code changes
6. **You update:** Status="Complete" in CSV
7. **You commit:** With TICKET reference in commit message

---

## Tips

- **Use labels for filtering:** All "ai-trade" items, all "bug" items, etc.
- **Token estimates:** Guide model choice — Haiku for simple UI tweaks, Sonnet for most features, Opus for research-heavy or architectural work
- **DueDate:** Soft targets, can adjust as needed
- **Keep notes updated:** Add blockers, dependencies, or implementation notes as you go
- **Legacy TokenEstimate values:** Tickets created before 2026-03-01 have hour estimates in this column (e.g., `8`). New tickets use the `~XK-Model` format.

---

Generated: 2026-02-27
Last Updated: When CSV is modified
Format: CSV (compatible with Excel, VS Code, Git)
