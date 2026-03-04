# TICKET-021 — Default Practice Account Displayed on Load in Account ComboBox

| Field | Value |
|-------|-------|
| **Ticket ID** | TICKET-021 |
| **Status** | Ready |
| **Priority** | Low |
| **Attempts** | 0 |
| **Created** | 2026-03-01 |
| **Last Updated** | 2026-03-01 |
| **Assigned To** | Copilot |
| **Proposed Model** | Claude Haiku 4.5 |

---

## Problem Statement

The Practice (PRAC) account is pre-selected as the default in the Account ID ComboBox on both the
Dashboard and AI Trade tab. However, on first load the ComboBox appears visually blank even though
the correct account is already selected internally. Users see an empty selector and may not realise
a valid account is active.

Additionally, the Account ComboBox text is small relative to other UI elements — increasing the
font size to 16 would make the selected account more prominent, especially during live trading when
a quick glance at the account indicator matters.

---

## Acceptance Criteria

- [ ] On page load, the Account ID ComboBox clearly shows the name of the pre-selected Practice account (e.g. `PRAC-XXXXXXX`) — it must not appear blank.
- [ ] Account ComboBox font size increased to **16** on Dashboard and AI Trade tab.
- [ ] No regression to white-text rendering (must continue to use `ItemTemplate` with `TextBlock Foreground="White"`).
- [ ] Behaviour is consistent across Dashboard and AI Trade tab.

---

## Likely Root Cause

The `SelectedAccount` property is set before `Accounts` is populated in the `ObservableCollection`.
WPF cannot match the binding value to an item that does not yet exist in the list, so the
`SelectionBoxItem` renders empty. When accounts are later added via `LoadDataAsync()` the
`SelectedAccount` reference may no longer match the newly-loaded objects by reference equality.

**Probable fix**: After populating `Accounts`, re-assign `SelectedAccount` from the loaded list
(matching by `Id`) so WPF sees a reference that exists in `ItemsSource`. This is already partially
done in `LoadDataAsync()` but may need a `NotifyPropertyChanged` nudge or a post-populate
`Dispatcher.Invoke` to force the binding to refresh.

---

## Files Likely Affected

| File | Change |
|------|--------|
| `src/TopStepTrader.UI/ViewModels/DashboardViewModel.vb` | Re-assign SelectedAccount after Accounts collection is populated |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Same fix in `LoadDataAsync()` |
| `src/TopStepTrader.UI/Views/DashboardView.xaml` | Increase Account ComboBox `FontSize` to 16 |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Increase Account ComboBox `FontSize` to 16 |

---

## Notes

- Keep `ItemTemplate` + `TextBlock Foreground="White"` pattern (TICKET-019 fix must not regress).
- Do not change the font size of the Contract ComboBox or other fields — Account only.
- If the PRAC account is not returned by the API on this load, fall back to showing the first account in the list (existing behaviour).

---

## Progress Tracking

### Implementation (Completed)
- [x] Root cause identified: SelectedAccount reference mismatch
- [x] DashboardViewModel.vb: Re-assign SelectedAccount from _accounts collection
- [x] AiTradingViewModel.vb: Same fix if needed (check code)
- [x] DashboardView.xaml: Increase Account ComboBox FontSize to 16
- [x] AiTradingView.xaml: Increase Account ComboBox FontSize to 16
- [x] Build verified: 0 errors, 0 warnings

### Unit Testing (Completed)
- [x] Code review of ViewModel fix
- [x] XAML syntax verified (FontSize binding)
- [x] No regression to white text rendering (ItemTemplate preserved)

### System Integration Testing (In Progress)
- [ ] User tests in running app: Account displays on load
- [ ] Dashboard tab: PRAC account visible
- [ ] AI Trade tab: PRAC account visible
- [ ] Font size verified (16pt readable)
- [ ] No regressions detected

**Last Updated:** 2026-03-01 19:00 UTC
**Current Status:** SIT Testing (awaiting user sign-off)
**Blocker:** None
**Next Concrete Action:** User performs SIT and approves/rejects for sign-off
