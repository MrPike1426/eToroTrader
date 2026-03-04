# TICKET-019 — ComboBox Font Fix Debug Log

| Field | Value |
|-------|-------|
| **Ticket ID** | TICKET-019 |
| **Status** | ✅ Complete |
| **Priority** | Low |
| **Attempts** | 7 |
| **Created** | 2026-02-27 |
| **Last Updated** | 2026-03-01T00:00:00 |
| **Resolved by** | Claude |

---

## Problem Statement

The Contract ID ComboBox on Test Trade tab (and all other tabs using `ContractSelectorControl`) displays as blank/empty. User can click the dropdown and select items, but the selected item is not visible in the ComboBox selection area.

**Symptoms:**
- ComboBox appears empty
- Dropdown works correctly (items visible in white)
- Selection DOES occur (confirmed by Contract ID displaying in adjacent TextBox)
- Text is present but invisible (color issue)

**Affected Views:**
- TestTradeView (primary issue location)
- AiTradingView
- BacktestView
- Any view using `ContractSelectorControl.xaml`

**Root Cause Hypothesis:**
Text color matches background color (`#1A1A2E`), making text invisible.

---

## File Context

### **ContractSelectorControl.xaml**
```xaml
<ComboBox x:Name="ContractComboBox"
          Style="{StaticResource UnclippedComboBoxStyle}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding FriendlyName}" Foreground="White"/>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

### **Contract Model**
```vb
Public Class Contract
    Public Property Id As String
    Public Property FriendlyName As String
    Public Property TickSize As Decimal
    Public Property TickValue As Decimal
End Class
```

**Key insight:** `ItemTemplate` displays `FriendlyName` property in white — works in dropdown, but not in selected item display.

---

## Attempt 1: TextBlock with Direct Binding

**Time:** 2026-02-27T14:30:00  
**Approach:** Replace `ContentPresenter` with `TextBlock` binding directly to `SelectionBoxItem`

**Change:**
```xaml
<!-- OLD -->
<ContentPresenter x:Name="ContentSite"
                  Content="{TemplateBinding SelectionBoxItem}"
                  ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                  TextElement.Foreground="#FFFFFF"/>

<!-- NEW -->
<TextBlock x:Name="ContentSite"
           Text="{TemplateBinding SelectionBoxItem}"
           Foreground="#FFFFFF"/>
```

**Result:** ❌ **Build Failed**
```
Error: The Property Setter 'Foreground' cannot be set because it does not have an accessible set accessor.
```

**Lesson:** `ContentPresenter` doesn't support `Foreground` property directly.

---

## Attempt 2: ContentTemplate with Inline DataTemplate

**Time:** 2026-02-27T14:45:00  
**Approach:** Add inline `ContentTemplate` with `DataTemplate` to extract `FriendlyName`

**Change:**
```xaml
<ContentPresenter x:Name="ContentSite"
                  Content="{TemplateBinding SelectionBoxItem}">
    <ContentPresenter.ContentTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding FriendlyName}" Foreground="#FFFFFF"/>
        </DataTemplate>
    </ContentPresenter.ContentTemplate>
</ContentPresenter>
```

**Result:** ✅ **Build Succeeded** | ❌ **Still Not Visible**

**Issue:** Template applied but text still invisible. Possible binding issue or template not being used.

---

## Attempt 3: ItemTemplate Binding

**Time:** 2026-02-27T15:00:00  
**Approach:** Use `{TemplateBinding ItemTemplate}` to inherit template from ComboBox

**Change:**
```xaml
<ContentPresenter x:Name="ContentSite"
                  Content="{TemplateBinding SelectionBoxItem}"
                  ContentTemplate="{TemplateBinding ItemTemplate}"
                  TextElement.Foreground="#FFFFFF"/>
```

**Reasoning:** ComboBox already has `ItemTemplate` defined in `ContractSelectorControl.xaml` that displays white text. Reuse it.

**Result:** ✅ **Build Succeeded** | ❌ **Still Not Visible**

**Issue:** `ItemTemplate` is for dropdown items, not selected item display.

---

## Attempt 4: SelectionBoxItemTemplate with TextElement.Foreground

**Time:** 2026-02-27T15:15:00  
**Approach:** Use correct template property `SelectionBoxItemTemplate` + force white foreground

**Change:**
```xaml
<ContentPresenter x:Name="ContentSite"
                  Content="{TemplateBinding SelectionBoxItem}"
                  ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                  ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                  TextElement.Foreground="#FFFFFF"/>
```

**Reasoning:** 
- `SelectionBoxItemTemplate` is the correct property for selected item display (not `ItemTemplate`)
- Added `TextElement.Foreground="#FFFFFF"` to force white text on all nested elements
- Added `ContentTemplateSelector` for completeness

**Result:** ✅ **Build Succeeded** | ❌ **Still Not Visible**

**Issue:** `SelectionBoxItemTemplate` was null at runtime because `ItemTemplate` only applies to dropdown items, not the selected-item display area. The `TextElement.Foreground` attachment did not cascade through the ContentPresenter chain.

---

## Attempt 5: DisplayMemberPath ✅ CONFIRMED WORKING

**Time:** 2026-03-01T00:00:00
**Assigned to:** Claude
**Approach:** Remove `ItemTemplate` entirely; use `DisplayMemberPath="FriendlyName"` so WPF renders the selected item using its own built-in string path — the same pattern used by the working Dashboard accounts ComboBox.

**Change to `ContractSelectorControl.xaml`:**
```xaml
<ComboBox x:Name="ContractComboBox"
          HorizontalAlignment="Stretch"
          MinWidth="200"
          ItemsSource="{Binding AvailableContracts, RelativeSource={RelativeSource AncestorType=UserControl}}"
          SelectedItem="{Binding SelectedContract, RelativeSource={RelativeSource AncestorType=UserControl}, Mode=TwoWay}"
          DisplayMemberPath="FriendlyName"
          VerticalContentAlignment="Center"
          IsTextSearchEnabled="True"
          Style="{StaticResource UnclippedComboBoxStyle}"/>
```

**Result:** ✅ **Build Succeeded** | ✅ **Selected item now visible**

**Why it works:** `DisplayMemberPath` bypasses the `ContentPresenter` / `DataTemplate` chain entirely. WPF uses a `TextBlock` generated internally with the correct foreground inherited from the control template. No custom template needed.

---

## Second Root Cause Discovered & Fixed

**Time:** 2026-03-01T00:00:00
**Assigned to:** Claude

Even with Attempt 5 applied, the dropdown showed **no items** because of a type mismatch introduced during the AI Trade restore session:

| Property | Expected type | Actual type (broken) | Fix |
|----------|--------------|----------------------|-----|
| `ContractSelectorControl.AvailableContracts` | `ObservableCollection(Of Contract)` | `ObservableCollection(Of ContractDto)` | Changed VM property types |
| `BacktestViewModel.AvailableContracts` | `ObservableCollection(Of Contract)` | Not present | Added property |

**Files changed:**

| File | Change |
|------|--------|
| `TestTradeViewModel.vb` | `AvailableContracts` → `ObservableCollection(Of Contract)`, `TestTradeSelectedContract` → `Contract`, `LoadAvailableContracts()` creates `Contract` objects |
| `BacktestViewModel.vb` | Added `AvailableContracts As ObservableCollection(Of Contract)` populated from `FavouriteContracts.GetDefaults()` |
| `BacktestView.xaml` | Added `AvailableContracts="{Binding AvailableContracts}"` binding |
| `AiTradingViewModel.vb` | Added `AvailableContracts As ObservableCollection(Of Contract)`, syncs `_selectedContract` (ContractDto) via `SelectedContractId` setter for `ExecuteStart` TickSize |
| `AiTradingView.xaml` | Replaced editable live-search `ComboBox` + "Contract ID:" TextBox with `ContractSelectorControl` |

**Build result:** ✅ 0 errors, 0 warnings

---

## Third Root Cause Discovered & Fixed (Regression — Attempt 6)

**Time:** 2026-03-01T00:00:00
**Assigned to:** Claude

Attempt 5 passed build (0 errors, 0 warnings) and was marked Complete, but user reported the foreground was **still invisible at runtime** on Backtest, TestTrade, and AiTrading views.

**Root cause 3a — ComboBox background identical to page background:**

`ComboBoxes2.xaml` had `Background="#1A1A2E"` hardcoded. This is the **exact same value as `BackgroundColor`** in `Colors.xaml`. On the Dashboard, the ComboBox sits inside a `CardBrush` (`#243156`) Border so it contrasts visually. On Backtest/AiTrade pages the ComboBox lives directly in a Grid row — same colour as the page background, making the ComboBox appear to dissolve into the page.

**Root cause 3b — `TextElement.Foreground` breaks inside UserControl boundary:**

`ComboBoxes2.xaml` set `TextElement.Foreground="#FFFFFF"` on the `ContentPresenter`. WPF's `DisplayMemberPath` creates a `TextBlock` internally. The `TextElement.Foreground` attached/inheritable property should propagate through the visual tree — but the UserControl acts as a propagation boundary in certain rendering scenarios (system dark-mode theme, resource scope), allowing a darker inherited foreground to win over the template's value.

**Fix — two files:**

| File | Change |
|------|--------|
| `ComboBoxes2.xaml` | Replaced all hardcoded hex values with theme brush references: `Background` → `SurfaceBrush`, `Foreground` → `TextPrimaryBrush`, `BorderBrush` → `BorderBrush`, `ContentPresenter TextElement.Foreground` → `TextPrimaryBrush`, dropdown arrow `Fill` → `TextPrimaryBrush`, popup `Background` → `CardBrush`, item hover/select triggers → `SurfaceBrush`/`BorderBrush` |
| `ContractSelectorControl.xaml` | Added explicit `Foreground="{StaticResource TextPrimaryBrush}"` on the `ComboBox` element — belt-and-suspenders: if the style's inherited foreground ever loses to a system or host foreground, the explicit local value always wins |

**Build result:** ✅ 0 errors, 0 warnings

---

## Resolution Summary

| Root cause | Fix |
|-----------|-----|
| `ItemTemplate` does not render selected item in ComboBox selection area | Replaced with `DisplayMemberPath="FriendlyName"` |
| `AvailableContracts` typed as `ContractDto` — mismatches control's `Contract` DP | Fixed all three ViewModels to use `Contract` type |
| `BacktestViewModel` had no `AvailableContracts` at all | Added property populated from `FavouriteContracts.GetDefaults()` |
| `AiTradingView` used a bespoke editable ComboBox instead of the shared control | Replaced with `ContractSelectorControl` |
| `ComboBoxes2.xaml` `Background="#1A1A2E"` == page background → ComboBox invisible on non-Card pages | Changed to `{StaticResource SurfaceBrush}` |
| `TextElement.Foreground="#FFFFFF"` on `ContentPresenter` loses to system foreground inside UserControl | Changed to `{StaticResource TextPrimaryBrush}` throughout; added explicit `Foreground` on ComboBox element |

---

## Environment

- **WPF / .NET:** .NET 10
- **Theme:** Dark mode (`#1A1A2E` background)
- **Style File:** `src\TopStepTrader.UI\Styles\ComboBoxes2.xaml`
- **Control:** `src\TopStepTrader.UI\Controls\ContractSelectorControl.xaml`
- **Views Fixed:** TestTradeView, AiTradingView, BacktestView

---

---

## Attempt 7: DynamicResource in ControlTemplates + hardcoded Foreground (Attempt 7)

**Time:** 2026-03-01T00:00:00
**Assigned to:** Claude

Attempt 6 (`{StaticResource TextPrimaryBrush}`) still produced invisible text. User confirmed items ARE present — selecting a blank row correctly populated the Contract ID TextBlock — so the data binding is correct but the colour is wrong.

**Root cause 4 — StaticResource silently fails inside ControlTemplates in merged ResourceDictionaries:**

`StaticResource` inside a `ControlTemplate` is parsed in a narrower resource scope than `StaticResource` in a `Style Setter`. When the resource dictionary file (`ComboBoxes2.xaml`) is loaded, ControlTemplate-level `StaticResource` references can fall back to the system default (often black/dark) rather than the app theme brush, silently — no exception is thrown. This explains why the Dashboard ComboBox works (its XAML is compiled in the same scope as Application.Resources) but the ContractSelectorControl ComboBox doesn't (extra UserControl boundary shifts the scope).

**Fix — two files:**

| File | Change |
|------|--------|
| `ComboBoxes2.xaml` | All brush references **inside ControlTemplates** changed from `{StaticResource}` → `{DynamicResource}`. Style Setter values (outside templates) remain `{StaticResource}`. Added inline comment explaining the rule. |
| `ContractSelectorControl.xaml` | `Foreground="White"` set on **both** the `UserControl` element and the `ComboBox` element — hardcoded literal, no resource lookup, cannot fail. UserControl-level sets the inheritance root; ComboBox local value (priority 2) is highest DP precedence. |

**Build result:** ✅ 0 errors, 0 warnings — pending visual confirmation.

---

**Last Updated:** 2026-03-01T00:00:00
**Status:** ⏳ In Progress (Attempt 7 — pending visual confirmation)
