Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Models

Namespace TopStepTrader.Core.Events

    Public Class SignalGeneratedEventArgs
        Inherits EventArgs
        Public ReadOnly Property Signal As TradeSignal
        Public Sub New(signal As TradeSignal)
            Me.Signal = signal
        End Sub
    End Class

    Public Class QuoteEventArgs
        Inherits EventArgs
        Public ReadOnly Property Quote As Quote
        Public Sub New(quote As Quote)
            Me.Quote = quote
        End Sub
    End Class

    Public Class BarEventArgs
        Inherits EventArgs
        Public ReadOnly Property Bar As MarketBar
        Public Sub New(bar As MarketBar)
            Me.Bar = bar
        End Sub
    End Class

    Public Class OrderFilledEventArgs
        Inherits EventArgs
        Public ReadOnly Property Order As Order
        Public Sub New(order As Order)
            Me.Order = order
        End Sub
    End Class

    Public Class OrderRejectedEventArgs
        Inherits EventArgs
        Public ReadOnly Property Order As Order
        Public ReadOnly Property Reason As String
        Public Sub New(order As Order, reason As String)
            Me.Order = order
            Me.Reason = reason
        End Sub
    End Class

    Public Class RiskHaltEventArgs
        Inherits EventArgs
        Public ReadOnly Property Reason As RiskHaltReason
        Public ReadOnly Property DailyPnL As Decimal
        Public ReadOnly Property Drawdown As Decimal
        Public Sub New(reason As RiskHaltReason, dailyPnL As Decimal, drawdown As Decimal)
            Me.Reason = reason
            Me.DailyPnL = dailyPnL
            Me.Drawdown = drawdown
        End Sub
    End Class

    ''' <summary>Raised by StrategyExecutionEngine when a trade entry order is placed.</summary>
    Public Class TradeOpenedEventArgs
        Inherits EventArgs
        Public ReadOnly Property Side As Core.Enums.OrderSide
        Public ReadOnly Property ContractId As String
        Public ReadOnly Property ConfidencePct As Integer
        Public ReadOnly Property EntryTime As DateTimeOffset
        ''' <summary>TopStepX external order ID for the entry order. Nothing if placement failed.</summary>
        Public ReadOnly Property ExternalOrderId As Long?
        Public Sub New(side As Core.Enums.OrderSide, contractId As String, confidencePct As Integer,
                       entryTime As DateTimeOffset, Optional externalOrderId As Long? = Nothing)
            Me.Side = side
            Me.ContractId = contractId
            Me.ConfidencePct = confidencePct
            Me.EntryTime = entryTime
            Me.ExternalOrderId = externalOrderId
        End Sub
    End Class

    ''' <summary>Raised by StrategyExecutionEngine when the bracket position closes (TP or SL filled).</summary>
    Public Class TradeClosedEventArgs
        Inherits EventArgs
        Public ReadOnly Property ExitReason As String   ' "TP", "SL", or "Closed"
        Public ReadOnly Property PnL As Decimal
        Public Sub New(exitReason As String, pnl As Decimal)
            Me.ExitReason = exitReason
            Me.PnL = pnl
        End Sub
    End Class

    Public Class BacktestProgressEventArgs
        Inherits EventArgs
        Public ReadOnly Property PercentComplete As Integer
        Public ReadOnly Property CurrentDate As Date
        Public ReadOnly Property TradesExecuted As Integer
        Public Sub New(pct As Integer, currentDate As Date, trades As Integer)
            PercentComplete = pct
            Me.CurrentDate = currentDate
            TradesExecuted = trades
        End Sub
    End Class

End Namespace
