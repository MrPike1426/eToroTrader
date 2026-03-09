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
    End Class

End Namespace
