Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    ''' <summary>
    ''' Request body for POST /api/v1/trading/execution/demo/market-open-orders/by-units
    ''' Opens a market position using a specific number of units (shares/contracts).
    ''' SL and TP are set at open time — no separate bracket orders needed.
    ''' </summary>
    Public Class OpenMarketOrderByUnitsRequest
        <JsonPropertyName("InstrumentID")>
        Public Property InstrumentId As Integer

        ''' <summary>True = Long (buy), False = Short (sell).</summary>
        <JsonPropertyName("IsBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("Leverage")>
        Public Property Leverage As Integer = 1

        ''' <summary>Number of units/shares to trade. Maps to the eToro AmountInUnits field.</summary>
        <JsonPropertyName("AmountInUnits")>
        Public Property AmountInUnits As Double

        ''' <summary>Stop-loss trigger price. Must be worse than current price.</summary>
        <JsonPropertyName("StopLossRate")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property StopLossRate As Double?

        ''' <summary>Take-profit trigger price. Must be better than current price.</summary>
        <JsonPropertyName("TakeProfitRate")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property TakeProfitRate As Double?

        ''' <summary>Trailing stop loss — SL auto-adjusts as price improves.</summary>
        <JsonPropertyName("IsTslEnabled")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property IsTslEnabled As Boolean?
    End Class

    ''' <summary>
    ''' Request body for POST /api/v1/trading/execution/demo/market-open-orders/by-amount
    ''' Opens a market position using a USD cash amount.
    ''' </summary>
    Public Class OpenMarketOrderByAmountRequest
        <JsonPropertyName("InstrumentID")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("IsBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("Leverage")>
        Public Property Leverage As Integer = 1

        ''' <summary>USD amount to invest.</summary>
        <JsonPropertyName("Amount")>
        Public Property Amount As Double

        <JsonPropertyName("StopLossRate")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property StopLossRate As Double?

        <JsonPropertyName("TakeProfitRate")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property TakeProfitRate As Double?

        <JsonPropertyName("IsTslEnabled")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property IsTslEnabled As Boolean?
    End Class

    ''' <summary>
    ''' Optional body for POST /api/v1/trading/execution/demo/market-close-orders/positions/{positionId}
    ''' Omit UnitsToDeduct (or set to null) to close the full position.
    ''' </summary>
    Public Class ClosePositionRequest
        <JsonPropertyName("UnitsToDeduct")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property UnitsToDeduct As Double?
    End Class

End Namespace
