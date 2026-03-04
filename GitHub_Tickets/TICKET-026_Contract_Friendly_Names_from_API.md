# TICKET-026: Contract Friendly Names from API

**Status:** Backlog
**Priority:** Low
**Severity:** Low
**Assigned To:** Claude Haiku 4.5
**StartDate:** (unscheduled)
**Due Date:** 2026-06-01
**Tokens:** 2
**Labels:** `feature,ai-trade,contracts,api-integration,low-priority`

---

## Problem Statement

Contract friendly display names (e.g. "M.Gold", "M.Nasdaq") are currently **hardcoded** in the
application as a simple string-match lookup:

```vb
If contractId.Contains("MGC") Then Return "M.Gold"
If contractId.Contains("MNQ") Then Return "M.Nasdaq"
If contractId.Contains("MCL") Then Return "M.Oil"
If contractId.Contains("MES") Then Return "M.S&P"
Return contractId.Substring(0, 6)  ' fallback
```

This works for the four known contracts but will break silently when:
- New contracts are added
- Contract codes change (e.g. futures roll from J26 → M26)
- User selects an unlisted contract

The ProjectX API provides contract metadata (name, description) via the contract search
endpoint — this data should be used instead of the hardcoded lookup.

---

## Desired Outcome

- Contract display names are fetched from the API once per session and cached
- The `ContractSelectorControl` and `TradeRowViewModel` both use the same cached name source
- Hardcoded fallback remains in place if the API is unavailable

---

## Requirements

### 1. Contract Metadata Cache

Create `IContractMetadataService` with a single method:
```vb
Function GetFriendlyNameAsync(contractId As String) As Task(Of String)
```

Implementation:
- On first call: fetch contract metadata from ProjectX API contract search endpoint
- Cache results in-memory (`Dictionary(Of String, String)`) for the session lifetime
- Return `Name` or `Description` field from the API response (whichever is shorter/friendlier)
- Fallback: if API unavailable or contract not found, use the existing hardcoded lookup

### 2. Integration Points

- `TradeRowViewModel.ContractDisplay` — use `IContractMetadataService` instead of static helper
- `ContractSelectorControl` — optionally display friendly name below the contract code
- Both should gracefully handle a null/empty API name (fallback to hardcoded)

### 3. No Breaking Changes

- The hardcoded `ToFriendlyName()` helper from TICKET-025 stays as the fallback
- This ticket only replaces it with an API-backed version when available
- If API call fails, behaviour is identical to TICKET-025 (hardcoded names)

---

## Acceptance Criteria

- [ ] `IContractMetadataService.GetFriendlyNameAsync("CON.F.US.MGC.J26")` returns `"Micro Gold"` (or equivalent from API)
- [ ] Result is cached — second call returns immediately without API hit
- [ ] If API unavailable, falls back to hardcoded name (no crash, no empty string)
- [ ] `TradeRowViewModel` uses the service
- [ ] Build: 0 errors, 0 warnings
- [ ] Existing tests still pass

---

## Blocked By

- **TICKET-025:** AI Trade Performance Panel *(must be delivered first — this replaces its hardcoded names)*

---

## Notes

- This is a polish/maintenance ticket. Do not start until TICKET-025 is Complete.
- The ProjectX contract search endpoint (`/api/Contract/search`) already exists and is used
  by `ContractSelectorControl` — reuse the same `ContractClient` HTTP client.
- Cache lifetime: session only (cleared on app restart). No persistence needed.

**Created:** 2026-03-02
**Last Updated:** 2026-03-02
**Model:** GitHub Copilot
