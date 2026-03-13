Imports System.Threading
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Models

Namespace TopStepTrader.Core.Interfaces

    Public Interface IBacktestService
        Event ProgressUpdated As EventHandler(Of BacktestProgressEventArgs)
        Function RunBacktestAsync(config As BacktestConfiguration, cancel As CancellationToken) As Task(Of BacktestResult)
        Function GetBacktestRunsAsync() As Task(Of IEnumerable(Of BacktestResult))
    End Interface

    Public Class BacktestConfiguration
        Public Property RunName As String = String.Empty
        Public Property ContractId As String = String.Empty
        Public Property Timeframe As Integer = 5
        Public Property StartDate As Date
        Public Property EndDate As Date
        Public Property InitialCapital As Decimal = 50000D
        Public Property MinSignalConfidence As Single = 0.65F

        ''' <summary>
        ''' Initial hard stop in dollars (e.g. 10 = $10 max loss on entry).
        ''' Turtle bracket uses this as the first SL level; SL only advances thereafter.
        ''' </summary>
        Public Property InitialSlAmount As Decimal = 10D

        ''' <summary>
        ''' Initial take-profit target in dollars (e.g. 20 = $20 gain triggers first bracket advance).
        ''' Subsequent bracket steps advance by 0.5 × N (ATR in dollar terms).
        ''' </summary>
        Public Property InitialTpAmount As Decimal = 20D

        ' ── Per-contract execution parameters ──────────────────────────────────
        ''' <summary>Number of contracts per trade entry.</summary>
        Public Property Quantity As Integer = 1

        ''' <summary>
        ''' Price units per tick for the selected contract.
        ''' Used by BacktestMetrics to convert tick counts into price deltas.
        ''' Defaults to 0.25 (MES/MNQ convention — quarter-point ticks).
        ''' Contract overrides: MGC = 0.10, MCL = 0.01.
        ''' </summary>
        Public Property TickSize As Decimal = 0.25D

        ''' <summary>
        ''' Dollar value per one full price-unit (1.0 point) for the selected contract.
        ''' Used by BacktestMetrics.CalculatePnL to convert price movement into dollar P&amp;L.
        ''' Defaults to $5 (MES correct value).
        ''' Contract overrides: MNQ = $2, MGC = $10, MCL = $100.
        ''' </summary>
        Public Property PointValue As Decimal = 5.0D

        ''' <summary>
        ''' Which entry condition to evaluate during backtest replay.
        ''' Defaults to EmaRsiWeightedScore to preserve existing behaviour.
        ''' Set to TripleEmaCascade for Sniper backtests.
        ''' </summary>
        Public Property StrategyCondition As StrategyConditionType = StrategyConditionType.EmaRsiWeightedScore

        ''' <summary>
        ''' Minimum ADX value required before an EmaRsiWeightedScore entry signal is acted on.
        ''' 0 (default) = gate disabled — every bar meeting the confidence threshold is traded,
        '''              regardless of trend strength. Useful for exploring raw signal frequency.
        ''' 25          = matches live StrategyExecutionEngine behaviour (strong-trend-only entries).
        ''' Ignored for TripleEmaCascade (which has no ADX gate).
        ''' </summary>
        Public Property MinAdxThreshold As Single = 0.0F

        ' ── EmaRsiWeightedScore / eToro by-amount fields ────────────────────────
        ''' <summary>USD cash per initial entry (EmaRsiWeightedScore). Default $200.</summary>
        Public Property EntryAmount As Decimal = 200D
        ''' <summary>Leverage for the initial entry (EmaRsiWeightedScore). Default 5.</summary>
        Public Property EntryLeverage As Integer = 5
        ''' <summary>USD cash per scale-in trade (EmaRsiWeightedScore). Default $200.</summary>
        Public Property ScaleInAmount As Decimal = 200D
        ''' <summary>Leverage for scale-in trades. Default 5.</summary>
        Public Property ScaleInLeverage As Integer = 5
    End Class

End Namespace
