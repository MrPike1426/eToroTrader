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
        Public Shared ReadOnly Defaults As IReadOnlyDictionary(Of String, StrategyParameterSet) =
            New Dictionary(Of String, StrategyParameterSet)(StringComparer.OrdinalIgnoreCase) From {
                {"EMA/RSI Combined", New StrategyParameterSet("50000", "1", "40", "20")},
                {"3-EMA Cascade (Sniper)", New StrategyParameterSet("50000", "1", "10", "5")}
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
    ''' </summary>
    Public NotInheritable Class StrategyParameterSet

        Public ReadOnly Property Capital As String
        Public ReadOnly Property Qty As String
        Public ReadOnly Property TakeProfitTicks As String
        Public ReadOnly Property StopLossTicks As String

        Public Sub New(capital As String, qty As String, tp As String, sl As String)
            Me.Capital = capital
            Me.Qty = qty
            Me.TakeProfitTicks = tp
            Me.StopLossTicks = sl
        End Sub

    End Class

End Namespace
