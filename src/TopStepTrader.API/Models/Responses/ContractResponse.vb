Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    Public Class ContractAvailableResponse
        <JsonPropertyName("contracts")>
        Public Property Contracts As List(Of ContractDto) = New List(Of ContractDto)()

        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

    Public Class ContractDto
        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("description")>
        Public Property Description As String = String.Empty

        <JsonPropertyName("tickSize")>
        Public Property TickSize As Decimal

        <JsonPropertyName("tickValue")>
        Public Property TickValue As Decimal

        <JsonPropertyName("activeContract")>
        Public Property ActiveContract As Boolean

        <JsonPropertyName("expirationDate")>
        Public Property ExpirationDate As String
    End Class

End Namespace
