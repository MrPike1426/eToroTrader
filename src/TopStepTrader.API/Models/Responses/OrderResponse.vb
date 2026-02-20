Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    Public Class PlaceOrderResponse
        <JsonPropertyName("orderId")>
        Public Property OrderId As Long

        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

    Public Class CancelOrderResponse
        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

    Public Class OrderSearchResponse
        <JsonPropertyName("orders")>
        Public Property Orders As List(Of OrderDto) = New List(Of OrderDto)()

        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

    Public Class OrderDto
        <JsonPropertyName("id")>
        Public Property Id As Long

        <JsonPropertyName("accountId")>
        Public Property AccountId As Long

        <JsonPropertyName("contractId")>
        Public Property ContractId As String = String.Empty

        <JsonPropertyName("creationTimestamp")>
        Public Property CreationTimestamp As Long

        <JsonPropertyName("type")>
        Public Property OrderType As Integer

        <JsonPropertyName("side")>
        Public Property Side As Integer

        <JsonPropertyName("size")>
        Public Property Size As Integer

        <JsonPropertyName("limitPrice")>
        Public Property LimitPrice As Double?

        <JsonPropertyName("stopPrice")>
        Public Property StopPrice As Double?

        <JsonPropertyName("status")>
        Public Property Status As Integer

        <JsonPropertyName("avgFillPrice")>
        Public Property AvgFillPrice As Double?
    End Class

End Namespace
