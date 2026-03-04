# Ticket Workflow Quick Reference Card
**Print this or pin it to your desk!**

---

## The 6-Stage Pipeline

```
📦 Backlog  →  🎯 For Dev  →  🔨 In Dev  →  ✅ SIT Test  →  🚀 Complete
                                                    ↘ ❌ Cancelled
```

---

## Status Definitions (One Sentence)

| Status | You Need To Know |
|--------|------------------|
| **Backlog** | Planned but can't start yet (waiting for something) |
| **For Development** | Ready to code — queue it for your next sprint |
| **In Development** | Someone is actively working on it right now |
| **SIT Testing** | Code is done, waiting for you to test it |
| **Complete** | ✅ Tested, approved, integrated |
| **Cancelled** | ❌ Not doing this one (see Notes for why) |

---

## Decision Trees

### "What status should this ticket be?"

**Ticket is planned but has open blockers?**
→ **Backlog**

**Ticket is planned, ready to code, no blockers?**
→ **For Development**

**A developer is actively coding this right now?**
→ **In Development** (+ set StartDate)

**Developer says "ready for testing"?**
→ **SIT Testing** (you test it now)

**You tested it and it works?**
→ **Complete** (add approval note)

**You found a bug?**
→ **Back to In Development** (add bug notes)

**You're not doing this ticket?**
→ **Cancelled** (explain why in Notes)

---

## Your Weekly Checklist

```
MONDAY (Planning)
□ Check SIT Testing column — anything waiting on me?
□ Review CRITICAL tickets (severity = Critical)
□ Look at BlockedBy column — any dependencies finished?
□ Queue next 2-3 tickets: Backlog → For Development (set Priority if needed)

WEDNESDAY (Mid-week)
□ Any tickets stuck in In Development too long?
□ Are we on pace to hit DueDate targets?

FRIDAY (Review)
□ Update Status for completed tickets
□ Calculate velocity: Sum(TokensBurned) this week
□ Plan next week's queue from For Development
```

---

## Developer Checklist (When Starting Work)

**Before you start coding:**

```
□ Status = "For Development"?
□ BlockedBy column is empty or all are "Complete"?
□ StartDate is set (or about to be)
□ AssignedTo is me
□ TokenEstimate provided
```

**When you finish:**

```
□ Status change: In Development → SIT Testing
□ TokensBurned = actual tokens used
□ Notes updated with: "✅ [What was done]. Build: 0 errors/warnings"
□ Ready for user testing
```

---

## Key Columns at a Glance

| Column | What It Is | When To Use |
|--------|-----------|------------|
| **Status** | Current phase | Every day |
| **Priority** | Order of work (High/Med/Low) | Planning day |
| **Severity** | Impact level (Critical/High/Med/Low) | When prioritizing |
| **StartDate** | When work actually began | When moving to In Dev |
| **DueDate** | Hard deadline | Set once, rarely change |
| **TargetCompletionDate** | When you estimate it'll be done | Developer estimates |
| **TokenEstimate** | Planned effort (hours/complexity) | Planning |
| **TokensBurned** | Actual effort used | At completion |
| **BlockedBy** | What must finish first | If you're stuck |
| **Blocks** | What's waiting on you | If you're critical path |
| **Labels** | Category tags (bug, feature, etc.) | Filtering & reporting |

---

## Dependency Detective

### "Who's blocking who?"

**To find blockers:**
1. Open TICKETS.csv
2. Sort by BlockedBy column (not empty)
3. Check if those tickets are Complete
4. If not → that's why this ticket is stuck

**Example:**
```
TICKET-011 (Confidence Selector)
  BlockedBy = TICKET-006 (Backtest Rewrite)
  Status = For Development (can't start, Backtest not done)

→ FIX: Finish TICKET-006 first, then TICKET-011 can start
```

---

## CRITICAL Tickets (Do These First!)

Check this column in TICKETS.csv:
```
Severity = Critical  AND  Status = For Development
```

Currently (2026-03-01):
- ❌ TICKET-010: Stop Loss Bug (due 10/03) — trades at risk!
- ❌ TICKET-016: Tab Switch Leak (due 06/03) — resource waste
- ❌ TICKET-017: Stop Button (due 08/04) — incomplete shutdown
- ❌ TICKET-018: Trade Rejection (due 06/03) — stale monitoring

**Action:** Start with TICKET-010 (highest risk).

---

## Token Tracking (Learn What Takes Time)

### Quick Formula:
```
TokenEstimate = 8    (what we predicted)
TokensBurned = 12    (what it actually took)
Variance = 50%       (we underestimated)

👉 Next similar ticket: estimate 12, not 8
```

### Velocity Insight:
```
Sum(TokensBurned) last 5 tickets = 55 tokens
= ~11 tokens per ticket average
= 1-2 tickets per week per developer
```

---

## Dates Format Clarification

| Column | Format | Example | Meaning |
|--------|--------|---------|---------|
| StartDate | YYYY-MM-DD | 2026-03-01 | Today's date when starting |
| DueDate | DD/MM/YYYY | 01/03/2026 | Hard deadline (existing format) |
| TargetCompletionDate | YYYY-MM-DD | 2026-03-05 | Estimated done date |
| LastUpdated | ISO 8601 | 2026-03-01T14:30:00 | Last change (auto-tracked) |

---

## Status Transition Flowchart

```
START: New Ticket Idea
       ↓
   [ Create in Backlog ]
       ↓
   Spec complete?  ← NO → Wait (still gathering requirements)
   Blockers gone?  ← NO → Wait (dependencies not met)
   Ready to queue? ← NO → Keep in Backlog
       ↓ YES
   [ Move to For Development ]
       ↓
   Ready to code now?  ← NO → Wait in queue
       ↓ YES
   [ Move to In Development + set StartDate ]
       ↓
   [ Developer codes... ]
       ↓
   Code done + Build passes?
       ↓ YES
   [ Move to SIT Testing ]
       ↓
   [ You test the feature... ]
       ↓
   ✅ Works perfect?  → [ Move to Complete ] ✅
   ❌ Has bugs?      → [ Back to In Development + describe bugs ]
   🚫 Wrong feature?  → [ Move to Cancelled + explain ]
```

---

## Common Mistakes (Don't Do These!)

| ❌ Mistake | ✅ What To Do Instead |
|-----------|----------------------|
| Leave a ticket in "For Dev" for weeks without starting | Move to "In Development" with StartDate when actually starting |
| Finish coding but leave status as "In Development" | Move to "SIT Testing" immediately so you know it needs testing |
| Approve a ticket for Complete without testing | Always move SIT Testing → user tests → user moves to Complete |
| Skip TokensBurned when finishing | Always fill TokensBurned — this is gold data for velocity |
| Create ticket without token estimate | Estimate upfront (even rough is better than nothing) |
| Leave BlockedBy/Blocks out of sync | If A blocks B, then B must list A in BlockedBy |

---

## Slack-Style Status Updates

**What to say when reporting:**

```
✅ COMPLETED: TICKET-021 (Default Account Display)
   Moved: SIT Testing → Complete (user tested & approved)

🔨 IN DEV: TICKET-010 (Stop Loss Bug)
   Status: 40% done, TokensBurned: 7/18
   Blocker: None
   ETA: 2026-03-08

⏳ QUEUED: TICKET-012, 016, 017, 018, 022
   (For Development — waiting for assignment)

🚨 CRITICAL: 4 high-severity tickets in queue
   (Recommend prioritizing TICKET-010 first)
```

---

## Quick Links

| Need | File |
|------|------|
| **Full column definitions** | `TICKETS_SCHEMA.md` |
| **Workflow overview** | `TICKET_PIPELINE_SUMMARY.md` |
| **Ticket details** | `GitHub_Tickets/TICKET-XXX.md` |
| **Data source** | `TICKETS.csv` |

---

## The Golden Rule

> **Move the status WHEN the state changes, not after.**

```
❌ BAD: Code is done for 3 days before moving SIT Testing
✅ GOOD: Code finishes → immediately move to SIT Testing same day
```

This keeps TICKETS.csv a **real-time source of truth**, not a history file.

---

**Print this card. Keep it visible. Update TICKETS.csv when status changes. ✅**

