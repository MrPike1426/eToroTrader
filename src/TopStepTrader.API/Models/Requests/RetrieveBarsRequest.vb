Namespace TopStepTrader.API.Models.Requests

    ''' <summary>
    ''' eToro historical candles are fetched via GET with path parameters — no POST body needed.
    ''' Endpoint: GET /api/v1/market-data/instruments/{instrumentId}/history/candles/{direction}/{interval}/{candlesCount}
    ''' Kept as a stub for project file compatibility.
    ''' </summary>
    Public Class RetrieveBarsRequest
        Public Property InstrumentId As Integer
        Public Property Direction As String = "desc"
        Public Property Interval As String = "FiveMinutes"
        Public Property CandlesCount As Integer = 500
    End Class

End Namespace
