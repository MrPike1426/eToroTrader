Imports TopStepTrader.Core.Enums

Namespace TopStepTrader.Core.Models

    ''' <summary>
    ''' Fully-parsed trading strategy ready for execution by StrategyExecutionEngine.
    ''' Created either from a pre-loaded template or from natural-language parsing.
    ''' </summary>
    Public Class StrategyDefinition

        ' ── Instrument & account ──────────────────────────────────────────
        Public Property Name As String = "Custom Strategy"
        Public Property ContractId As String = String.Empty
        Public Property AccountId As Long
        Public Property CapitalAtRisk As Decimal = 500D
        Public Property Quantity As Integer = 1

        ' ── Time parameters ───────────────────────────────────────────────
        Public Property TimeframeMinutes As Integer = 5
        Public Property DurationHours As Double = 8.0

        ' ── Indicator ─────────────────────────────────────────────────────
        Public Property Indicator As StrategyIndicatorType = StrategyIndicatorType.BollingerBands
        Public Property IndicatorPeriod As Integer = 20
        Public Property IndicatorMultiplier As Double = 2.0
        ''' <summary>Secondary EMA period (used for EMA Crossover strategies).</summary>
        Public Property SecondaryPeriod As Integer = 21

        ' ── Entry condition ───────────────────────────────────────────────
        Public Property Condition As StrategyConditionType = StrategyConditionType.FullCandleOutsideBands
        Public Property GoLongWhenBelowBands As Boolean = True
        Public Property GoShortWhenAboveBands As Boolean = True

        ' ── Exit strategy (bracket orders) ────────────────────────────────
        ''' <summary>Take-profit distance in ticks (0 = no TP). Non-eToro execution paths.</summary>
        Public Property TakeProfitTicks As Integer = 40
        ''' <summary>Stop-loss distance in ticks (0 = no SL). Non-eToro execution paths.</summary>
        Public Property StopLossTicks As Integer = 20
        ''' <summary>
        ''' Minimum price increment for the selected contract (e.g. 0.25 for ES, 5.0 for MBT).
        ''' Set by the ViewModel from ContractDto.TickSize before starting the engine.
        ''' Used to convert tick counts into price offsets for bracket orders.
        ''' </summary>
        Public Property TickSize As Decimal = 1D
        ''' <summary>Dollar value of one tick move (e.g. MES = $1.25, MGC = $1.00). Used for P&amp;L display.</summary>
        Public Property TickValue As Decimal = 1D

        ' ── Exit strategy — eToro percentage-based (AI Trading path) ────────────────
        ''' <summary>
        ''' Take-profit as a percentage of entry price (0 = no TP order placed).
        ''' E.g. 1.5 means close position when price rises 1.5% above entry (Long)
        ''' or falls 1.5% below entry (Short).
        ''' Used by the eToro AI Trading path; computed into an absolute StopLossRate.
        ''' </summary>
        Public Property TakeProfitPct As Decimal = 0D
        ''' <summary>
        ''' Stop-loss as a percentage of entry price (0 = no SL order placed).
        ''' E.g. 0.75 means protect position if price moves 0.75% against entry.
        ''' Used by the eToro AI Trading path; computed into an absolute StopLossRate.
        ''' </summary>
        Public Property StopLossPct As Decimal = 0D
        ''' <summary>Leverage multiplier sent to eToro (default 1 = no leverage).
        ''' Affects both the effective position size and the minimum cash required:
        ''' minCash = MinNotionalUsd / Leverage.</summary>
        Public Property Leverage As Integer = 1

        ' ── Signal filtering ──────────────────────────────────────────────
        ''' <summary>
        ''' Minimum weighted-score confidence required to fire a trade signal (0–100, default 75).
        ''' The EMA/RSI engine computes upPct/downPct in the range 0–100.
        ''' A trade is only placed when upPct >= MinConfidencePct (Long) or
        ''' downPct >= MinConfidencePct (Short). Set from UI by the user.
        ''' </summary>
        Public Property MinConfidencePct As Integer = 85
        ''' <summary>
        ''' Cash amount per scale-in trade (EmaRsiWeightedScore only). Default $200.
        ''' Set from the Scale-In panel in the UI before the engine starts.
        ''' </summary>
        Public Property ScaleInAmount As Decimal = 200D
        ''' <summary>
        ''' Leverage multiplier applied to each scale-in trade (default 5).
        ''' Set from the Scale-In panel in the UI before the engine starts.
        ''' </summary>
        Public Property ScaleInLeverage As Integer = 5

        ' ── Runtime state (set when engine starts) ────────────────────────
        Public Property ExpiresAt As DateTimeOffset

        ' ── Provenance ────────────────────────────────────────────────────
        ''' <summary>Original natural-language description (empty for pre-loaded templates).</summary>
        Public Property RawDescription As String = String.Empty

        ''' <summary>
        ''' Returns Name so WPF ComboBoxes display the strategy name as a fallback
        ''' when no DataTemplate / DisplayMemberPath is active.
        ''' </summary>
        Public Overrides Function ToString() As String
            Return Name
        End Function

        ''' <summary>Human-readable one-line summary for display in the parsed-parameters panel.</summary>
        Public ReadOnly Property Summary As String
            Get
                Dim indicator As String
                Select Case Me.Indicator
                    Case StrategyIndicatorType.BollingerBands : indicator = $"BB({IndicatorPeriod},{IndicatorMultiplier})"
                    Case StrategyIndicatorType.RSI : indicator = $"RSI({IndicatorPeriod})"
                    Case StrategyIndicatorType.EMA : indicator = $"EMA({IndicatorPeriod}/{SecondaryPeriod})"
                    Case Else : indicator = Me.Indicator.ToString()
                End Select

                Dim directions As String
                If GoLongWhenBelowBands AndAlso GoShortWhenAboveBands Then
                    directions = "Long+Short"
                ElseIf GoLongWhenBelowBands Then
                    directions = "Long only"
                Else
                    directions = "Short only"
                End If

                Dim tp = If(TakeProfitPct > 0, $"TP:{TakeProfitPct:F2}%",
                            If(TakeProfitTicks > 0, $"TP:{TakeProfitTicks}t", "No TP"))
                Dim sl = If(StopLossPct > 0, $"SL:{StopLossPct:F2}%",
                            If(StopLossTicks > 0, $"SL:{StopLossTicks}t", "No SL"))

                Return $"{indicator} | {TimeframeMinutes}-min | {DurationHours}hrs | {directions} | {tp} {sl}"
            End Get
        End Property

    End Class

End Namespace
