# Ticket Folder Structure & Organization

**Created:** 2026-03-01
**Purpose:** Define how to organize completed vs. open ticket markdown files

---

## Recommended Folder Structure

```
GitHub_Tickets/
├── TICKET-003_Market_Data_Real_Time_Streaming.md         (Open - Backlog)
├── TICKET-004_Risk_Guard_Position_Sizing.md              (Open - Backlog)
├── TICKET-005_Fix_Backtest_Strategy_Mismatch.md          (Open - Backlog)
├── TICKET-006_Backtest_Page_Complete_Rewrite.md          (Open - Backlog)
├── TICKET-007_Multi_Strategy_Concurrent_Trading.md       (Open - Backlog)
├── TICKET-009_Trailing_Take_Profit_Implementation.md     (Open - For Dev)
├── TICKET-010_Fix_Stop_Loss_Hardcoding_Bug.md            (Open - For Dev, CRITICAL)
├── TICKET-011_AI_Trade_Confidence_Selector.md            (Open - For Dev)
├── TICKET-016_Fix_Tab_Switching_Process_Persistence.md   (Open - For Dev)
├── TICKET-017_Implement_Complete_Stop_on_Stop_Button.md  (Open - For Dev)
├── TICKET-018_Cancel_Monitoring_on_Rejected_Trade.md     (Open - For Dev)
├── TICKET-020_Economic_Calendar_Filter.md                (Open - Backlog)
├── TICKET-021_Default_Practice_Account_Display.md        (Open - SIT Testing)
├── TICKET-022_AI_Trade_Tab_UI_Consolidation.md           (Open - For Dev)
│
├── Completed Tickets/
│   ├── TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md     (Complete)
│   ├── TICKET-002_Dashboard_Balance_History.md            (Complete)
│   ├── TICKET-013_Add_Balance_Daily_PnL_Display.md        (Complete)
│   ├── TICKET-014_Redesign_AI_Trade_Layout.md             (Complete)
│   └── TICKET-019_Fix_Contract_ID_Combo_Box_Font.md       (Complete)
│
├── TICKETS_SCHEMA.md                                      (Reference)
├── TICKET_PIPELINE_SUMMARY.md                             (Reference)
├── TICKET_QUICK_REFERENCE.md                              (Reference)
├── TICKET_SYSTEM_DELIVERY.md                              (Reference)
├── MARKDOWN_DELIVERY_SUMMARY.md                           (Reference)
├── FOLDER_STRUCTURE.md                                    (This file)
│
└── TICKETS.csv                                            (Master data)
```

---

## How to Use This Structure

### **Active Work**

All **open** ticket markdown files stay in the **main GitHub_Tickets/** folder:
- Status: `Backlog`, `For Development`, `In Development`, `SIT Testing`
- These are actively worked on or queued
- Easy to find and reference during development

### **Completed Work**

Move **completed** ticket markdown files to **GitHub_Tickets/Completed Tickets/** folder:
- Status: `Complete`
- Archived for reference/audit trail
- Keeps main folder focused on active work

### **When to Move a Ticket**

Move a ticket to `Completed Tickets/` when:
1. TICKETS.csv shows `Status = Complete`
2. Progress Tracking section shows all tasks checked `[x]`
3. Final sign-off completed
4. No longer needs to be in active work folder

**Process:**
```
1. User marks TICKET in TICKETS.csv: Status = Complete
2. Developer updates markdown Progress Tracking: all [x] complete
3. File is moved from GitHub_Tickets/ to GitHub_Tickets/Completed Tickets/
4. Reference copy optionally kept in TICKET_ARCHIVE.md (not needed, file is archive)
```

---

## Progress Tracking in Each Markdown

Every open ticket now has a `## Progress Tracking` section with:

```markdown
## Progress Tracking

### Phase 1: [Description]
- [ ] Task 1
- [ ] Task 2
- [x] Task 3 (completed)

### Phase 2: [Description]
- [ ] Task 4

**Last Updated:** 2026-03-01 14:30 UTC
**Current Status:** Phase 1 complete (75%), Phase 2 starting
**Blocker:** None (or description)
**Next Concrete Action:** [Specific next step]
```

**Purpose:**
- Track progress within the ticket
- Enable quick resumption if session disconnects
- Show user current work status
- Document next concrete action clearly

---

## Session Recovery Workflow

If a token timeout or session disconnect occurs:

1. **User (or returning Claude):**
   - Open the ticket markdown file
   - Read the `## Progress Tracking` section
   - Find `**Current Status**` and `**Next Concrete Action**`

2. **Resume Work:**
   - Start from next concrete action (don't re-do completed work)
   - Update Progress Tracking as you complete more tasks
   - Update `**Last Updated**` timestamp
   - Update `**Current Status**` summary

3. **Example:**
   ```
   Before (incomplete session):
   **Last Updated:** 2026-03-01 14:30 UTC
   **Current Status:** Phase 1 complete (75%), Phase 2 starting
   **Next Concrete Action:** Design BacktestViewModel structure

   After (resumed, more work done):
   **Last Updated:** 2026-03-01 16:45 UTC
   **Current Status:** Phase 1 complete, Phase 2 design done (50%)
   **Next Concrete Action:** Start implementing MultiStrategyBacktestService
   ```

---

## Completed Tickets Reference

To see all completed work, check the `Completed Tickets/` folder:

| Ticket | Title | Status | Completed Date |
|--------|-------|--------|-----------------|
| TICKET-001 | Test Trade EMA/RSI Analysis | Complete | 2026-02-26 |
| TICKET-002 | Dashboard Balance History | Complete | 2026-03-10 |
| TICKET-013 | Add Balance & Daily P&L Display | Complete | (date) |
| TICKET-014 | Redesign AI Trade Layout | Complete | 2026-03-01 |
| TICKET-019 | Fix Contract ID Combo Box Font | Complete | 2026-03-01 |

---

## Active Work Summary (as of 2026-03-01)

**Total Open Tickets:** 14

### By Status:
- **Backlog (6):** TICKET-003, 004, 005, 006, 007, 020
- **For Development (7):** TICKET-009, 010, 011, 016, 017, 018, 022
- **SIT Testing (1):** TICKET-021

### By Priority:
- **🔴 CRITICAL (1):** TICKET-010 (Stop Loss Bug)
- **🟠 HIGH (5):** TICKET-016, 017, 018, 006, 011
- **🟡 MEDIUM (6):** TICKET-004, 005, 007, 009, 022, 020
- **🟢 LOW (2):** TICKET-003, 021

---

## Instructions for Moving Completed Tickets

**Windows File Explorer:**

```
1. Right-click GitHub_Tickets/Completed Tickets/ folder
2. Ensure it exists; create if needed

3. When TICKET is ready to move:
   - Right-click TICKET-XXX_[Title].md
   - Select "Cut"
   - Navigate to Completed Tickets/ folder
   - Right-click → "Paste"

4. Verify move completed successfully
```

**Command Line (PowerShell):**

```powershell
# Move single ticket
Move-Item -Path "GitHub_Tickets\TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md" `
          -Destination "GitHub_Tickets\Completed Tickets\"

# Verify move
Get-ChildItem "GitHub_Tickets\Completed Tickets"
```

**Git (if using version control):**

```bash
# Stage the move
git mv GitHub_Tickets/TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md \
       GitHub_Tickets/Completed\ Tickets/

# Commit
git commit -m "Move TICKET-001 to Completed Tickets (Status: Complete)"
```

---

## Notes

- **Folder is optional but recommended** for organization
- If you prefer flat structure, that's fine—just keep naming convention clear
- **Progress Tracking sections are mandatory** for all open tickets (enables session recovery)
- **Completed Tickets folder is optional** but helps keep active work focused
- Consider periodic cleanup (monthly or quarterly) to archive completed work

---

## Next Steps

1. ✅ **Progress Tracking** sections added to all open ticket markdowns (DONE)
2. ✅ **TICKETS_SCHEMA.md** updated with requirements (DONE)
3. ⏳ **Create Completed Tickets folder** (when first ticket reaches Complete status)
4. ⏳ **Move completed tickets** (TICKET-001, 002, 013, 014, 019 when ready)

---

**Status:** Ready to use immediately
**Enables:** Session recovery, progress tracking, folder organization
**Maintenance:** Minimal (just move files when tickets complete)
