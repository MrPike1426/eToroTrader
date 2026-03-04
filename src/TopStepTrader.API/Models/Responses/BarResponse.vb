Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    ''' <summary>
    ''' Response from GET /api/v1/market-data/instruments/{instrumentId}/history/candles/{direction}/{interval}/{count}
    ''' eToro groups candles per instrument inside a wrapper array.
    ''' </summary>
    Public Class CandlesResponse
        ''' <summary>Time interval string matching the request (e.g. "OneMinute", "FiveMinutes").</summary>
        <JsonPropertyName("interval")>
        Public Property Interval As String = String.Empty

        ''' <summary>One entry per requested instrument, each containing its own candle array.</summary>
        <JsonPropertyName("candles")>
        Public Property Candles As List(Of InstrumentCandlesDto) = New List(Of InstrumentCandlesDto)()

        Public Property Success As Boolean = True
    End Class

    Public Class InstrumentCandlesDto
        <JsonPropertyName("instrumentId")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("candles")>
        Public Property Candles As List(Of CandleDto) = New List(Of CandleDto)()
    End Class

    Public Class CandleDto
        <JsonPropertyName("instrumentID")>
        Public Property InstrumentId As Integer

        ''' <summary>Start of the candle period in ISO 8601 format.</summary>
        <JsonPropertyName("fromDate")>
        Public Property FromDate As String = String.Empty

        <JsonPropertyName("open")>
        Public Property Open As Double

        <JsonPropertyName("high")>
        Public Property High As Double

        <JsonPropertyName("low")>
        Public Property Low As Double

        <JsonPropertyName("close")>
        Public Property Close As Double

        <JsonPropertyName("volume")>
        Public Property Volume As Double
    End Class

    ' ── Legacy alias so BarIngestionService compiles with minimal changes ──

    Public Class BarResponse
        Public Property Success As Boolean = True
        Public Property ErrorMessage As String = String.Empty
        Public Property Bars As List(Of BarDto) = New List(Of BarDto)()
    End Class

    Public Class BarDto
        Public Property Timestamp As String = String.Empty
        Public Property Open As Double
        Public Property High As Double
        Public Property Low As Double
        Public Property Close As Double
        Public Property Volume As Long
    End Class

End Namespace
