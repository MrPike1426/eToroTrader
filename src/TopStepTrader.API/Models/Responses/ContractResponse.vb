Imports System.Text.Json
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
        ''' <summary>
        ''' Internal API id — declared as JsonElement because the ProjectX API returns it
        ''' inconsistently (Int32, Int64, float, or quoted string) depending on endpoint version.
        ''' Use IdText for a safe string; use ContractId for placing orders.
        ''' </summary>
        <JsonPropertyName("id")>
        Public Property Id As JsonElement

        ''' <summary>String contract code e.g. "CON.F.US.MES.H26".</summary>
        <JsonPropertyName("contractId")>
        Public Property ContractId As String = String.Empty

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("description")>
        Public Property Description As String = String.Empty

        <JsonPropertyName("tickSize")>
        <JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)>
        Public Property TickSize As Decimal

        <JsonPropertyName("tickValue")>
        <JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)>
        Public Property TickValue As Decimal

        <JsonPropertyName("activeContract")>
        Public Property ActiveContract As Boolean

        <JsonPropertyName("expirationDate")>
        Public Property ExpirationDate As String

        ''' <summary>Safe string of the numeric Id regardless of JSON token type.</summary>
        Public ReadOnly Property IdText As String
            Get
                Select Case Id.ValueKind
                    Case JsonValueKind.Number : Return Id.GetRawText()
                    Case JsonValueKind.String : Return Id.GetString()
                    Case Else : Return String.Empty
                End Select
            End Get
        End Property

        ''' <summary>Display label used in contract dropdowns.</summary>
        Public ReadOnly Property DisplayLabel As String
            Get
                Dim code = If(String.IsNullOrWhiteSpace(ContractId), $"#{IdText}", ContractId)
                Return $"{Name}  [{code}]"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return DisplayLabel
        End Function
    End Class

End Namespace
