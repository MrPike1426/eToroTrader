Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    Public Class AuthResponse
        <JsonPropertyName("token")>
        Public Property Token As String = String.Empty

        <JsonPropertyName("success")>
        Public Property Success As Boolean

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As Integer

        <JsonPropertyName("errorMessage")>
        Public Property ErrorMessage As String
    End Class

End Namespace
