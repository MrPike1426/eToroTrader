# ⚡ Quick Reference

## Pipeline at a Glance
```
📦 Backlog → 🎯 For Dev → 🔨 In Dev → ✅ SIT Test → 🚀 Complete
```

## Status — One Line Each
| Status | When to use |
|---|---|
| **Backlog** | Has blockers, waiting for input, or not yet prioritised |
| **For Development** | Ready to code — no blockers |
| **In Development** | Being actively coded right now (set StartDate) |
| **SIT Testing** | Dev done — needs user testing |
| **Complete** | User-approved ✅ |
| **Cancelled** | Not doing it — document why in Notes |

## Priority vs Severity
- **Priority** = order to work on it (High first)
- **Severity** = impact if it breaks (Critical = data loss / money at risk)

## Field Cheatsheet
| Field | Format | Notes |
|---|---|---|
| TicketId | TICKET-NNN | Increment from last |
| DueDate | YYYY-MM-DD | Hard deadline |
| StartDate | YYYY-MM-DD | Set when moving to In Dev |
| TargetCompletionDate | YYYY-MM-DD | Dev's estimate |
| TokenEstimate | integer | Set at creation |
| TokensBurned | integer | Update at completion |
| Labels | comma-separated | bug, feature, api, ui, risk, trading |
| BlockedBy | TICKET-NNN,… | Dependencies |

## Update Commands
```powershell
# Most common updates
dotnet run --project Tickets/DbQuery -- update TICKET-001 status "In Development"
dotnet run --project Tickets/DbQuery -- update TICKET-001 status "SIT Testing"
dotnet run --project Tickets/DbQuery -- update TICKET-001 status "Complete"
dotnet run --project Tickets/DbQuery -- update TICKET-001 tokens-burned 10
dotnet run --project Tickets/DbQuery -- update TICKET-001 notes "Fixed in commit abc123"
```

## Starting a Ticket (Checklist)
```
□ Status = "For Development" (no blockers)
□ Set: update TICKET-NNN status "In Development"
□ Set: update TICKET-NNN start YYYY-MM-DD
□ Set: update TICKET-NNN assignee "Your name"
```

## Finishing a Ticket (Checklist)
```
□ Build: 0 errors, 0 warnings
□ Set: update TICKET-NNN status "SIT Testing"
□ Set: update TICKET-NNN tokens-burned <actual>
□ Add: comment TICKET-NNN "Done — <what was done>"
□ Wait for user sign-off → then: status "Complete"
```
