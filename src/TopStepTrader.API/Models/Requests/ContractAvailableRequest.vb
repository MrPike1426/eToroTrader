Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    Public Class ContractAvailableRequest
        <JsonPropertyName("live")>
        Public Property Live As Boolean = True

        <JsonPropertyName("searchText")>
        Public Property SearchText As String = String.Empty
    End Class

End Namespace
