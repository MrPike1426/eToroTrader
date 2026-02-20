Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    Public Class AccountSearchResponse
        <JsonPropertyName("accounts")>
        Public Property Accounts As List(Of AccountDto) = New List(Of AccountDto)()

        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

    Public Class AccountDto
        <JsonPropertyName("id")>
        Public Property Id As Long

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("balance")>
        Public Property Balance As Decimal

        <JsonPropertyName("canTrade")>
        Public Property CanTrade As Boolean

        <JsonPropertyName("isVisible")>
        Public Property IsVisible As Boolean
    End Class

End Namespace
