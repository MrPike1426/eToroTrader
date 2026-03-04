Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    ''' <summary>
    ''' Response from GET /api/v1/market-data/search?internalSymbolFull=TICKER
    ''' Returns a paged list of matching instruments.
    ''' </summary>
    Public Class InstrumentSearchResponse
        <JsonPropertyName("items")>
        Public Property Items As List(Of InstrumentDto) = New List(Of InstrumentDto)()

        Public Property Success As Boolean = True
    End Class

    Public Class InstrumentDto
        ''' <summary>eToro numeric instrument ID. Use this in all trading/history calls.</summary>
        <JsonPropertyName("instrumentId")>
        Public Property InstrumentId As Integer

        ''' <summary>Full ticker symbol, e.g. "AAPL".</summary>
        <JsonPropertyName("internalSymbolFull")>
        Public Property InternalSymbolFull As String = String.Empty

        <JsonPropertyName("internalInstrumentDisplayName")>
        Public Property DisplayName As String = String.Empty

        <JsonPropertyName("instrumentTypeId")>
        Public Property InstrumentTypeId As Integer

        <JsonPropertyName("exchangeId")>
        Public Property ExchangeId As Integer

        ''' <summary>Display label used in instrument dropdowns.</summary>
        Public ReadOnly Property DisplayLabel As String
            Get
                Return $"{InternalSymbolFull}  — {DisplayName}  [ID:{InstrumentId}]"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return DisplayLabel
        End Function
    End Class

    ' ── Legacy alias so existing ContractClient callers compile with minimal changes ──

    Public Class ContractAvailableResponse
        Public Property Success As Boolean = True
        Public Property Contracts As List(Of ContractDto) = New List(Of ContractDto)()
    End Class

    Public Class ContractDto
        Public Property ContractId As String = String.Empty   ' ticker symbol
        Public Property Name As String = String.Empty
        Public Property Description As String = String.Empty
        Public Property InstrumentId As Integer
        Public Property TickSize As Decimal
        Public Property TickValue As Decimal

        Public ReadOnly Property DisplayLabel As String
            Get
                Return $"{Name}  [{ContractId}]"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return DisplayLabel
        End Function
    End Class

End Namespace
