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
        ''' <summary>eToro external order ID for the entry order. Nothing if placement failed.</summary>
        Public ReadOnly Property ExternalOrderId As Long?
        ''' <summary>eToro positionId resolved after the order fills. Nothing if not yet resolved.</summary>
        Public ReadOnly Property EtoroPositionId As Long?
        ''' <summary>UTC timestamp recorded when the position was opened by the engine.</summary>
        Public ReadOnly Property OpenedAtUtc As DateTimeOffset
        ''' <summary>Cash amount invested (after min-notional clamp).</summary>
        Public ReadOnly Property Amount As Decimal
        ''' <summary>Leverage applied to the order.</summary>
        Public ReadOnly Property Leverage As Integer
        ''' <summary>Entry price used for order computation (last bar close at signal time).</summary>
        Public ReadOnly Property EntryPrice As Decimal
        Public Sub New(side As Core.Enums.OrderSide, contractId As String, confidencePct As Integer,
                       entryTime As DateTimeOffset, Optional externalOrderId As Long? = Nothing,
                       Optional etoroPositionId As Long? = Nothing,
                       Optional openedAtUtc As DateTimeOffset = Nothing,
                       Optional amount As Decimal = 0D,
                       Optional leverage As Integer = 1,
                       Optional entryPrice As Decimal = 0D)
            Me.Side = side
            Me.ContractId = contractId
            Me.ConfidencePct = confidencePct
            Me.EntryTime = entryTime
            Me.ExternalOrderId = externalOrderId
            Me.EtoroPositionId = etoroPositionId
            Me.OpenedAtUtc = If(openedAtUtc = DateTimeOffset.MinValue, entryTime, openedAtUtc)
            Me.Amount = amount
            Me.Leverage = If(leverage > 0, leverage, 1)
            Me.EntryPrice = entryPrice
        End Sub
    End Class

    ''' <summary>Raised by StrategyExecutionEngine when the bracket position closes (TP or SL filled).</summary>
    Public Class TradeClosedEventArgs
        Inherits EventArgs
        Public ReadOnly Property ExitReason As String   ' "TP", "SL", "Reversal", or "Closed"
        Public ReadOnly Property PnL As Decimal
        Public Sub New(exitReason As String, pnl As Decimal)
            Me.ExitReason = exitReason
            Me.PnL = pnl
        End Sub
    End Class

    ''' <summary>
    ''' Raised by StrategyExecutionEngine on every 30-second tick while a position is open.
    ''' Carries API-authoritative P&amp;L and the eToro positionId (which may have been resolved
    ''' after order placement if the broker API had a propagation delay).
    ''' </summary>
    Public Class PositionSyncedEventArgs
        Inherits EventArgs
        ''' <summary>eToro positionId confirmed by the portfolio API.</summary>
        Public ReadOnly Property PositionId As Long
        ''' <summary>Unrealised P&amp;L in USD as reported by the broker.</summary>
        Public ReadOnly Property UnrealizedPnlUsd As Decimal
        ''' <summary>UTC timestamp the position was opened, from the broker.</summary>
        Public ReadOnly Property OpenedAtUtc As DateTimeOffset
        Public Sub New(positionId As Long, unrealizedPnlUsd As Decimal, openedAtUtc As DateTimeOffset)
            Me.PositionId = positionId
            Me.UnrealizedPnlUsd = unrealizedPnlUsd
            Me.OpenedAtUtc = openedAtUtc
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

    ''' <summary>
    ''' Raised by StrategyExecutionEngine after every bar check with the live EMA/RSI score.
    ''' Fires on every 30-second tick regardless of whether a trade signal is generated,
    ''' giving the UI a continuous confidence telemetry feed.
    ''' </summary>
    Public Class ConfidenceUpdatedEventArgs
        Inherits EventArgs
        ''' <summary>Bull score 0–100 (percentage of max weighted score that is bullish).</summary>
        Public ReadOnly Property UpPct As Integer
        ''' <summary>Bear score = 100 - UpPct.</summary>
        Public ReadOnly Property DownPct As Integer
        ''' <summary>
        ''' True when the ADX trend-strength gate passed (ADX ≥ 25).
        ''' False when the raw score is high but the signal is suppressed because ADX &lt; 25
        ''' (ranging market). Always True for strategies that embed ADX as a confluence
        ''' condition (MultiConfluence), where no separate gate exists.
        ''' </summary>
        Public ReadOnly Property AdxGatePassed As Boolean
        ''' <summary>Actual ADX value at bar-check time. 0 when not applicable (e.g. MultiConfluence, LULT).</summary>
        Public ReadOnly Property AdxValue As Single
        ''' <summary>Last bar close price at the time the event was raised. 0 when not provided.</summary>
        Public ReadOnly Property LastClose As Decimal
        Public Sub New(upPct As Integer, downPct As Integer,
                       Optional adxGatePassed As Boolean = True,
                       Optional adxValue As Single = 0,
                       Optional lastClose As Decimal = 0D)
            Me.UpPct = upPct
            Me.DownPct = downPct
            Me.AdxGatePassed = adxGatePassed
            Me.AdxValue = adxValue
            Me.LastClose = lastClose
        End Sub

        ' ── Extended multi-confluence indicator snapshot ─────────────────────────
        ' Set via object initialiser after construction; default to 0 / "not available".
        Public Property Cloud1 As Decimal = 0D
        Public Property Cloud2 As Decimal = 0D
        Public Property Tenkan As Decimal = 0D
        Public Property Kijun As Decimal = 0D
        Public Property Ema21 As Decimal = 0D
        Public Property Ema50 As Decimal = 0D
        Public Property PlusDI As Single = 0F
        Public Property MinusDI As Single = 0F
        Public Property MacdHist As Single = 0F
        Public Property MacdHistPrev As Single = 0F
        Public Property StochRsiK As Single = 0F
        Public Property LongCount As Integer = 0
        Public Property ShortCount As Integer = 0
        ''' <summary>Total number of conditions evaluated (7 for MultiConfluence; 6 for EmaRsiCombined; 0 for other strategies).</summary>
        Public Property TotalConditions As Integer = 0

        ' ── EMA/RSI Combined extended snapshot ──────────────────────────────────
        ''' <summary>RSI14 value at bar-check time (EMA/RSI Combined). 0 when not applicable.</summary>
        Public Property Rsi14 As Single = 0F
        ''' <summary>True when EMA21 is higher than its previous-bar value (EMA/RSI Combined condition 5). False when not applicable.</summary>
        Public Property Ema21Rising As Boolean = False
        ''' <summary>True when the majority of the last 3 candles closed above their open (EMA/RSI Combined condition 6). False when not applicable.</summary>
        Public Property RecentCandlesBullish As Boolean = False
    End Class

    ''' <summary>
    ''' Raised by StrategyExecutionEngine when the Turtle bracket is first placed or advances a step.
    ''' </summary>
    Public Class TurtleBracketChangedEventArgs
        Inherits EventArgs
        Public ReadOnly Property BracketNumber As Integer
        Public ReadOnly Property SlPrice As Decimal
        Public ReadOnly Property TpPrice As Decimal
        ''' <summary>
        ''' True when this event represents a bracket advance triggered by a price level being
        ''' hit (TP reached → SL steps up).  False for initial bracket placement on order entry
        ''' or bracket reattachment on engine restart.
        ''' The UI uses this flag to decide whether to display the "Turtle Applied" status
        ''' message — only a genuine advance warrants user-visible confirmation.
        ''' </summary>
        Public ReadOnly Property IsAdvance As Boolean
        Public Sub New(bracketNumber As Integer, slPrice As Decimal, tpPrice As Decimal,
                       isAdvance As Boolean)
            Me.BracketNumber = bracketNumber
            Me.SlPrice = slPrice
            Me.TpPrice = tpPrice
            Me.IsAdvance = isAdvance
        End Sub
    End Class

End Namespace
