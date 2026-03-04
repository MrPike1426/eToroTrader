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
        Public Property StopLossTicks As Integer = 20
        Public Property TakeProfitTicks As Integer = 40

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
    End Class

End Namespace
