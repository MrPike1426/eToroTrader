# 📋 Ticket Pipeline
**Project:** eToroTrader  **DB:** `Tickets/tickets.db`  **Tool:** `dotnet run --project Tickets/DbQuery -- <cmd>`

---

## The 6-Stage Pipeline

```
📦 Backlog  →  🎯 For Dev  →  🔨 In Dev  →  ✅ SIT Test  →  🚀 Complete
                                                   ↘  ❌ Cancelled
```

| Emoji | Status | Meaning |
|---|---|---|
| 📦 | **Backlog** | Planned but blocked or not yet prioritised |
| 🎯 | **For Development** | Spec complete, no blockers — ready to code |
| 🔨 | **In Development** | Actively being worked on right now |
| ✅ | **SIT Testing** | Code complete — awaiting user sign-off |
| 🚀 | **Complete** | Tested, approved, merged |
| ❌ | **Cancelled** | Won't do (reason in Notes) |

---

## Common Commands

```powershell
# See current pipeline grouped by status
dotnet run --project Tickets/DbQuery -- pipeline

# List all active tickets
dotnet run --project Tickets/DbQuery -- list

# List by specific status
dotnet run --project Tickets/DbQuery -- list "For Development"

# Create a new ticket
dotnet run --project Tickets/DbQuery -- new

# Show full ticket details
dotnet run --project Tickets/DbQuery -- show TICKET-001

# Update a field
dotnet run --project Tickets/DbQuery -- update TICKET-001 status "In Development"
dotnet run --project Tickets/DbQuery -- update TICKET-001 tokens-burned 8
dotnet run --project Tickets/DbQuery -- update TICKET-001 assignee "Damo"

# Add a comment
dotnet run --project Tickets/DbQuery -- comment TICKET-001 "Resolved by updating auth headers"

# Export to CSV
dotnet run --project Tickets/DbQuery -- export
```

---

## Your Weekly Checklist

**Monday (Planning)**
- [ ] `pipeline` — what's in SIT Testing needing sign-off?
- [ ] Review CRITICAL severity tickets
- [ ] Check BlockedBy — any blockers now resolved?
- [ ] Move 2–3 tickets: Backlog → For Development

**Friday (Review)**
- [ ] Update status on completed work
- [ ] Log `tokens-burned` on finished tickets
- [ ] Plan next week from For Development queue

---

## Velocity Tracking

| Field | Purpose |
|---|---|
| `TokenEstimate` | Planned effort (set at creation) |
| `TokensBurned` | Actual effort (update at completion) |

If estimates consistently differ from actual, adjust future estimates accordingly.

---

## Dependency Tracking

- `BlockedBy` — ticket IDs this ticket is waiting on, comma-separated
- `Blocks`    — ticket IDs that are waiting on this one

**Example:**
```
TICKET-005  BlockedBy = TICKET-003
TICKET-003  Blocks    = TICKET-005
```

Check for dependency chains before re-prioritising tickets.
