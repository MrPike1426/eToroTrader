Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    Public Class CancelOrderRequest
        <JsonPropertyName("orderId")>
        Public Property OrderId As Long
    End Class

    Public Class SearchOrderRequest
        <JsonPropertyName("accountId")>
        Public Property AccountId As Long

        <JsonPropertyName("startTimestamp")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property StartTimestamp As Long?

        <JsonPropertyName("endTimestamp")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property EndTimestamp As Long?
    End Class

End Namespace
