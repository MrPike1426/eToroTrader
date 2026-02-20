Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    Public Class AccountSearchRequest
        <JsonPropertyName("onlyActiveAccounts")>
        Public Property OnlyActiveAccounts As Boolean = True
    End Class

End Namespace
