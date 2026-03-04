# TICKET-014 — Redesign AI Trade Layout

| Field | Value |
|-------|-------|
| **Ticket ID** | TICKET-014 |
| **Status** | Complete |
| **Priority** | Medium |
| **Attempts** | 1 |
| **Created** | 2026-03-01 |
| **Last Updated** | 2026-03-01 |
| **Resolved by** | Claude Sonnet 4.6 |

---

## Problem Statement

On the AI Trade tab there are 8 strategies which can be employed, however analysis of strategy
documents suggests these are suboptimal when deployed separately. The Test Trade tab uses a
weighted multi-indicator approach to analyse the current trend. Additionally, the strategies are
too rigid.

**Issues identified:**
- Individual technical indicators are suboptimal in isolation.
- Different strategies require different timescales and TP/SL settings.
- Some strategies were incorrectly labelled (e.g., Bollinger Breakout said "5-minute candles" but used 480-min bars).
- Economic calendar filters needed (separate TICKET-020).
- Capital risk model too rigid — future enhancement.

---

## What Was Implemented

### 1. New Enum Values (Core layer)

**`src/TopStepTrader.Core/Enums/StrategyIndicatorType.vb`**
- Added `EmaRsiCombined = 4` — identifies the combined EMA/RSI strategy type.

**`src/TopStepTrader.Core/Enums/StrategyConditionType.vb`**
- Added `EmaRsiWeightedScore = 6` — six-signal weighted score condition with XML doc comment.

### 2. EMA/RSI Execution Logic (Services layer)

**`src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb`**

Added `Case StrategyConditionType.EmaRsiWeightedScore` to the existing `Select Case` in
`DoCheckAsync()`. Uses `TechnicalIndicators` directly (same as the rest of the engine):

| Signal | Weight | Logic |
|--------|--------|-------|
| EMA21 vs EMA50 crossover | 25% | EMA21 > EMA50 = bullish |
| Price vs EMA21 | 20% | Close > EMA21 = bullish |
| Price vs EMA50 | 15% | Close > EMA50 = bullish |
| RSI14 gradient | 20% | ≤30 = fully bullish (20pts), ≥70 = bearish (0pts), linear between |
| EMA21 momentum | 10% | EMA21 rising vs previous = bullish |
| Recent 3 candles | 10% | ≥2 green candles = bullish |

- **Long signal**: UP score ≥ 60%
- **Short signal**: DOWN score ≥ 60% (i.e., UP < 40%)
- **No signal**: 40–59% UP — logged with full detail, no order placed

### 3. Confidence Check (Services layer)

**`src/TopStepTrader.Services/AI/ClaudeReviewService.vb`**
- Added `ConfidenceSystemPrompt` const with a prompt tuned for market context assessment.
- Added `ConfidenceCheckAsync(contractId, cancel)` method — calls Anthropic API with the
  confidence prompt asking for macro factors, session windows, structural tendencies, and bias.
- Error handling matches the existing `ReviewStrategyAsync` pattern.

### 4. ViewModel Redesign

**`src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb`**

Removed:
- 8 old strategy card command declarations (`SelectBollingerBreakoutCommand` etc.)
- 8 old command wirings in constructor.

Added:
- `SelectEmaRsiCombinedCommand` — one-click activate (calls `ApplyEmaRsiCombined()`).
- `GetConfidenceCommand` — calls `ExecuteGetConfidence()`.
- `ConfidenceText` property (bound to Confidence Check TextBox).
- `IsCheckingConfidence` property (bound to ProgressBar visibility).
- `ApplyEmaRsiCombined()` — creates `StrategyDefinition` directly (no parse step):
  - `Indicator = EmaRsiCombined`
  - `Condition = EmaRsiWeightedScore`
  - `IndicatorPeriod = 50` (drives min-bar guard: 50+5=55, engine fetches 60)
  - `TimeframeMinutes = 5` (5-min bars — optimal for micro-futures intraday)
  - `DurationHours = 8` (covers London open + NY session)
  - Sets `HasParsedStrategy = True` → enables `▶ Start Monitoring` immediately.
- `ExecuteGetConfidence()` — fires `_reviewService.ConfidenceCheckAsync()` on background thread.

Preserved (code retained for future move to Backtest page):
- `ParseCommand` / `ExecuteParse()`
- `GetAiReviewCommand` / `ExecuteGetAiReview()`
- `AiReviewText`, `IsReviewing` properties
- `ApplyPreloadedByIndex()` / `PreloadedStrategies`

### 5. View Redesign

**`src/TopStepTrader.UI/Views/AiTradingView.xaml`**

**Panel 2 (Strategy Selection)**:
- Removed 8-button 4×2 UniformGrid.
- Removed natural-language strategy textarea and Parse row.
- Replaced with single-row 4-button UniformGrid:
  - Button 1 `🎯 EMA/RSI Combined` — active, bound to `SelectEmaRsiCombinedCommand`.
  - Buttons 2–4 `⚙ Strategy N — In Development` — `IsEnabled="False"` placeholders.
- Active strategy indicator card retained (shows `ActiveStrategyText`).

**Panel 3 (Confidence Check — replaces AI Review)**:
- Heading changed to `🎯 CONFIDENCE CHECK (Claude Haiku)`.
- Button changed to `🔍 Check Confidence`, bound to `GetConfidenceCommand`.
- ProgressBar bound to `IsCheckingConfidence`.
- TextBox bound to `ConfidenceText` (max height 100px, up from 80px).

---

## User Workflow (After Change)

1. Navigate to **AI Trade** tab.
2. Select **Account** (defaults to PRACTICE) and **Contract** from the dropdowns (Panel 1).
3. Set **Capital / Qty / TP / SL** (Panel 1 Row 2).
4. Click **🎯 EMA/RSI Combined** → strategy activates instantly, log shows briefing.
5. Optionally click **🔍 Check Confidence** → Claude returns market context for the contract.
6. Click **▶ Start Monitoring** → engine polls 5-min bars every 30 seconds, fires entry when score ≥ 60%.
7. Click **■ Stop** to halt at any time.

---

## Files Changed

| File | Action |
|------|--------|
| `src/TopStepTrader.Core/Enums/StrategyIndicatorType.vb` | Modified — added `EmaRsiCombined = 4` |
| `src/TopStepTrader.Core/Enums/StrategyConditionType.vb` | Modified — added `EmaRsiWeightedScore = 6` |
| `src/TopStepTrader.Services/Trading/StrategyExecutionEngine.vb` | Modified — added EmaRsiWeightedScore case |
| `src/TopStepTrader.Services/AI/ClaudeReviewService.vb` | Modified — added `ConfidenceCheckAsync` + `ConfidenceSystemPrompt` |
| `src/TopStepTrader.UI/ViewModels/AiTradingViewModel.vb` | Modified — replaced 8 strategy commands with 1 + confidence check |
| `src/TopStepTrader.UI/Views/AiTradingView.xaml` | Modified — Panel 2 and Panel 3 rewritten |
| `GitHub_Tickets/TICKET-014_.Redesign_AI_Trade_Layout.md` | This file |

---

## Known Limitations / Follow-on Tickets

- **TICKET-020**: Economic calendar filter — skip entry 30 mins before NFP/CPI/Fed decisions and
  avoid 2–4 AM UTC (lowest volume). Not implemented here.
- Strategies 2–4 are placeholder buttons pending research and design review.
- Optimal TP/SL tick values for each micro-futures contract are not hardcoded — user sets manually.

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
