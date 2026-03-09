Namespace TopStepTrader.Core.Trading

    ''' <summary>
    ''' Canonical parameter defaults for each supported backtest/live-trade strategy.
    '''
    ''' Design rule (TICKET-006): only combined multi-indicator strategies are registered here.
    ''' Single-indicator strategies (pure RSI, pure EMA, Double Bottom, etc.) are excluded —
    ''' backtesting a single-indicator strategy does not produce reliable live trading signals.
    ''' </summary>
    Public NotInheritable Class StrategyDefaults

        Private Sub New()
        End Sub

        ''' <summary>
        ''' All registered strategies and their default parameters.
        ''' Key lookup is case-insensitive.
        ''' </summary>
        ' eToro AI Trading path: Capital (USD cash), Qty, TakeProfitPct (%), StopLossPct (%)
        ' Values are percentage-based, NOT tick offsets (TICKET-022).
        ' TP 4% / SL 1.5% -> 2.67:1 R:R (documentation minimum: 2:1).
        Public Shared ReadOnly Defaults As IReadOnlyDictionary(Of String, StrategyParameterSet) =
            New Dictionary(Of String, StrategyParameterSet)(StringComparer.OrdinalIgnoreCase) From {
                {"EMA/RSI Combined", New StrategyParameterSet("200", "1", "4.0", "1.5")},
                {"Multi-Confluence Engine", New StrategyParameterSet("200", "1", "0", "0")}
            }

        ''' <summary>
        ''' Look up the default parameters for <paramref name="strategyName"/>.
        ''' Returns Nothing when the strategy is not registered or the name is null/empty.
        ''' </summary>
        Public Shared Function TryGet(strategyName As String) As StrategyParameterSet
            If String.IsNullOrEmpty(strategyName) Then Return Nothing
            Dim result As StrategyParameterSet = Nothing
            Defaults.TryGetValue(strategyName, result)
            Return result
        End Function

    End Class

    ''' <summary>
    ''' Immutable set of capital/quantity/TP/SL defaults for a strategy.
    ''' Values are stored as strings to match the ViewModel's text-bound input fields.
    ''' TakeProfitPct and StopLossPct hold percentage values for the eToro AI Trading path.
    ''' </summary>
    Public NotInheritable Class StrategyParameterSet

        Public ReadOnly Property Capital As String
        Public ReadOnly Property Qty As String
        ''' <summary>Take-profit as a percentage of entry price (e.g. "4.0" = 4.0%).</summary>
        Public ReadOnly Property TakeProfitPct As String
        ''' <summary>Stop-loss as a percentage of entry price (e.g. "1.5" = 1.5%).</summary>
        Public ReadOnly Property StopLossPct As String

        Public Sub New(capital As String, qty As String, tp As String, sl As String)
            Me.Capital = capital
            Me.Qty = qty
            Me.TakeProfitPct = tp
            Me.StopLossPct = sl
        End Sub

    End Class

End Namespace
