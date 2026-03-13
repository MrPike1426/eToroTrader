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

        ' ── Exit strategy — Turtle Bracket (all execution paths) ─────────────────
        ''' <summary>
        ''' Initial stop-loss in dollars (e.g. 10 = $10 hard stop for Bracket 0).
        ''' Turtle bracket SL only ever advances in the favourable direction; never retreats.
        ''' Engine converts to an absolute price: Entry ± (InitialSlAmount / DollarPerPoint).
        ''' </summary>
        Public Property InitialSlAmount As Decimal = 10D

        ''' <summary>
        ''' Initial take-profit target in dollars (e.g. 20 = $20 triggers first bracket advance).
        ''' Once hit, SL steps to the TP level and a new TP is set at TP + 0.5×N (ATR in $).
        ''' Engine converts to an absolute price: Entry ± (InitialTpAmount / DollarPerPoint).
        ''' </summary>
        Public Property InitialTpAmount As Decimal = 20D

        ''' <summary>
        ''' Minimum price increment for the selected contract (e.g. 0.25 for MES/MNQ).
        ''' Used by TopStep engines for tick-count conversion via TurtleBracketManager.DollarsToTicks.
        ''' </summary>
        Public Property TickSize As Decimal = 1D

        ''' <summary>Dollar value of one tick move (e.g. MES = $1.25, MGC = $1.00). Used for P&amp;L display.</summary>
        Public Property TickValue As Decimal = 1D

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

                Dim tp = If(InitialTpAmount > 0, $"TP:${InitialTpAmount:F0}", "No TP")
                Dim sl = If(InitialSlAmount > 0, $"SL:${InitialSlAmount:F0}", "No SL")

                Return $"{indicator} | {TimeframeMinutes}-min | {DurationHours}hrs | {directions} | {tp} {sl}"
            End Get
        End Property

    End Class

End Namespace
