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
        ' eToro AI Trading path: Capital (USD cash), Qty, InitialTpAmount ($), InitialSlAmount ($).
        ' Dollar-based Turtle bracket — engine converts to absolute prices using ATR and leverage.
        Public Shared ReadOnly Defaults As IReadOnlyDictionary(Of String, StrategyParameterSet) =
            New Dictionary(Of String, StrategyParameterSet)(StringComparer.OrdinalIgnoreCase) From {
                {"EMA/RSI Combined", New StrategyParameterSet("200", "1", "20", "10")},
                {"Multi-Confluence Engine", New StrategyParameterSet("200", "1", "20", "10")},
                {"BB Squeeze Scalper", New StrategyParameterSet("300", "1", "8", "4")}
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
    ''' InitialTpAmount and InitialSlAmount are dollar P&amp;L amounts for the Turtle bracket.
    ''' </summary>
    Public NotInheritable Class StrategyParameterSet

        Public ReadOnly Property Capital As String
        Public ReadOnly Property Qty As String
        ''' <summary>Initial take-profit in dollars (e.g. "20" = $20). First Turtle bracket target.</summary>
        Public ReadOnly Property InitialTpAmount As String
        ''' <summary>Initial stop-loss in dollars (e.g. "10" = $10). Hard stop for Bracket 0.</summary>
        Public ReadOnly Property InitialSlAmount As String

        Public Sub New(capital As String, qty As String, tp As String, sl As String)
            Me.Capital = capital
            Me.Qty = qty
            Me.InitialTpAmount = tp
            Me.InitialSlAmount = sl
        End Sub

    End Class

End Namespace
