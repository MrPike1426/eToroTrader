Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    ''' <summary>
    ''' Response from POST /api/v1/trading/execution/demo/market-open-orders/by-units (or by-amount).
    ''' Returns the orderId of the placed market order.
    ''' </summary>
    Public Class PlaceOrderResponse
        <JsonPropertyName("orderId")>
        Public Property OrderId As Long

        ''' <summary>True unless an exception is thrown. Set to False by the client on error.</summary>
        Public Property Success As Boolean = True
        Public Property ErrorMessage As String = String.Empty
    End Class

    ''' <summary>
    ''' Response from DELETE /api/v1/trading/execution/demo/market-open-orders/{orderId}
    ''' or DELETE /api/v1/trading/execution/demo/limit-orders/{orderId}.
    ''' </summary>
    Public Class CancelOrderResponse
        <JsonPropertyName("token")>
        Public Property Token As String = String.Empty

        Public Property Success As Boolean = True
        Public Property ErrorMessage As String = String.Empty
    End Class

    ''' <summary>
    ''' Response from GET /api/v1/trading/info/demo/orders/{orderId}.
    ''' Contains the order status and positions that were opened by this order.
    ''' </summary>
    Public Class OrderInfoResponse
        <JsonPropertyName("orderId")>
        Public Property OrderId As Long

        <JsonPropertyName("instrumentId")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("isBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("amount")>
        Public Property Amount As Double

        <JsonPropertyName("units")>
        Public Property Units As Double

        <JsonPropertyName("stopLossRate")>
        Public Property StopLossRate As Double

        <JsonPropertyName("takeProfitRate")>
        Public Property TakeProfitRate As Double

        <JsonPropertyName("openDateTime")>
        Public Property OpenDateTime As String = String.Empty

        <JsonPropertyName("positions")>
        Public Property Positions As List(Of EToroPositionDto) = New List(Of EToroPositionDto)()
    End Class

    ''' <summary>Used by OrderService.SearchOrdersAsync — wraps the portfolio response.</summary>
    Public Class OrderSearchResponse
        Public Property Success As Boolean = True
        Public Property ErrorMessage As String = String.Empty
        Public Property Orders As List(Of OrderDto) = New List(Of OrderDto)()
    End Class

    ''' <summary>Internal DTO mapping an eToro position to a legacy order shape for OrderService.</summary>
    Public Class OrderDto
        Public Property Id As Long              ' positionId
        Public Property AccountId As Long
        Public Property ContractId As String = String.Empty  ' instrumentId as string
        Public Property Status As Integer       ' 1 = Working (open position)
        Public Property AvgFillPrice As Double?
    End Class

End Namespace
