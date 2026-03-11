Namespace TopStepTrader.Core.Models

    ''' <summary>
    ''' A lightweight snapshot of one live broker position, populated from the eToro portfolio API.
    ''' Used by the strategy engine to drive API-authoritative trade status and P&amp;L.
    ''' </summary>
    Public Class LivePositionSnapshot
        ''' <summary>eToro positionId.</summary>
        Public Property PositionId As Long
        ''' <summary>Unrealised P&amp;L in USD, as reported directly by the broker.</summary>
        Public Property UnrealizedPnlUsd As Decimal
        ''' <summary>UTC timestamp the position was opened, parsed from the API response.</summary>
        Public Property OpenedAtUtc As DateTimeOffset
        ''' <summary>True if long (buy), False if short (sell).</summary>
        Public Property IsBuy As Boolean
        ''' <summary>Cash amount invested, as returned by the API.</summary>
        Public Property Amount As Decimal
        ''' <summary>The rate (price) at which the position was opened, as returned by the broker.</summary>
        Public Property OpenRate As Decimal
        ''' <summary>
        ''' Total units across all open positions for this contract, aggregated by OrderService.
        ''' Used by the engine to calculate P&amp;L: (currentPrice − OpenRate) × Units × direction.
        ''' The eToro portfolio API does not return a pnL field, so P&amp;L must be derived this way.
        ''' </summary>
        Public Property Units As Decimal
        ''' <summary>Leverage of the representative (first) position.</summary>
        Public Property Leverage As Integer
        ''' <summary>Number of open positions aggregated into this snapshot.</summary>
        Public Property PositionCount As Integer
    End Class

End Namespace
