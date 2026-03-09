Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    ''' <summary>
    ''' Envelope object inside the by-amount / by-units POST response.
    ''' eToro wraps all order fields under the "orderForOpen" key.
    ''' </summary>
    Public Class OrderForOpenDto
        ''' <summary>eToro order ID — note JSON key is "orderID" (capital D).</summary>
        <JsonPropertyName("orderID")>
        Public Property OrderId As Long

        <JsonPropertyName("instrumentID")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("isBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("amount")>
        Public Property Amount As Double

        <JsonPropertyName("leverage")>
        Public Property Leverage As Integer

        <JsonPropertyName("statusID")>
        Public Property StatusId As Integer

        <JsonPropertyName("stopLossRate")>
        Public Property StopLossRate As Double

        <JsonPropertyName("takeProfitRate")>
        Public Property TakeProfitRate As Double

        <JsonPropertyName("openDateTime")>
        Public Property OpenDateTime As String = String.Empty
    End Class

    ''' <summary>
    ''' Position record returned inside GET /trading/info/demo/orders/{orderId}.
    ''' Uses "positionID" and "rate" (not "positionId" / "openRate" used by the portfolio endpoint).
    ''' </summary>
    Public Class OrderPositionDto
        <JsonPropertyName("positionID")>
        Public Property PositionId As Long

        ''' <summary>Fill price (entry rate) of the position.</summary>
        <JsonPropertyName("rate")>
        Public Property Rate As Double

        <JsonPropertyName("units")>
        Public Property Units As Double

        <JsonPropertyName("amount")>
        Public Property Amount As Double

        <JsonPropertyName("isOpen")>
        Public Property IsOpen As Boolean
    End Class

    ''' <summary>
    ''' Response from POST /api/v1/trading/execution/demo/market-open-orders/by-units (or by-amount).
    ''' eToro wraps the order fields inside an "orderForOpen" envelope; orderId is NOT at root.
    ''' </summary>
    Public Class PlaceOrderResponse
        <JsonPropertyName("orderForOpen")>
        Public Property OrderForOpen As OrderForOpenDto

        <JsonPropertyName("token")>
        Public Property Token As String = String.Empty

        ''' <summary>True unless an exception is thrown. Set to False by the client on error.</summary>
        Public Property Success As Boolean = True
        Public Property ErrorMessage As String = String.Empty

        ''' <summary>Convenience accessor — reads orderID from the nested orderForOpen envelope.</summary>
        Public ReadOnly Property OrderId As Long
            Get
                Return If(OrderForOpen IsNot Nothing, OrderForOpen.OrderId, 0L)
            End Get
        End Property
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
        <JsonPropertyName("orderID")>
        Public Property OrderId As Long

        <JsonPropertyName("instrumentID")>
        Public Property InstrumentId As Integer

        ''' <summary>eToro order status: 1 = Working, 4 = Error/Rejected.</summary>
        <JsonPropertyName("statusID")>
        Public Property StatusId As Integer

        ''' <summary>Non-zero when eToro rejects the order (e.g. 720 = below minimum amount).</summary>
        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        ''' <summary>Human-readable rejection reason supplied by eToro.</summary>
        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String = String.Empty

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

        ''' <summary>
        ''' Positions opened by this order. Uses OrderPositionDto because the orders endpoint
        ''' returns "positionID" and "rate" rather than "positionId" and "openRate".
        ''' </summary>
        <JsonPropertyName("positions")>
        Public Property Positions As List(Of OrderPositionDto) = New List(Of OrderPositionDto)()
    End Class

    ''' <summary>Used by OrderService.SearchOrdersAsync
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
