Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Fetches OHLCV candle history from the eToro market data API.
    ''' GET /api/v1/market-data/instruments/{instrumentId}/history/candles/{direction}/{interval}/{count}
    ''' Max 1000 candles per request.
    ''' </summary>
    Public Class HistoryClient
        Inherits EToroHttpClientBase

        Private ReadOnly _settings As ApiSettings

        ''' <summary>Maps BarTimeframe integer codes to eToro interval strings.</summary>
        Public Shared ReadOnly BarUnitMap As New Dictionary(Of Integer, String) From {
            {1, "OneMinute"},
            {2, "FiveMinutes"},
            {3, "FifteenMinutes"},
            {4, "ThirtyMinutes"},
            {5, "OneHour"},
            {6, "OneDay"}
        }

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       credentials As EToroCredentialsProvider,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of HistoryClient))
            MyBase.New(httpClientFactory, credentials, rateLimiter, logger)
            _settings = options.Value
        End Sub

        ''' <summary>
        ''' Retrieves historical candles for an instrument and maps them to the legacy BarResponse shape.
        ''' </summary>
        ''' <param name="contractId">
        '''   Ticker symbol (e.g. "AAPL") or numeric instrumentId as string (e.g. "1001").
        '''   Must be parseable as an integer for the eToro endpoint.
        ''' </param>
        ''' <param name="unit">Bar timeframe code: 1=1min, 2=5min, 3=15min, 4=30min, 5=1hr, 6=1day</param>
        ''' <param name="unitNumber">Unused — kept for interface compatibility.</param>
        ''' <param name="unitsBack">Number of candles to fetch (max 1000).</param>
        Public Async Function RetrieveBarsAsync(
            contractId As String,
            unit As Integer,
            unitNumber As Integer,
            unitsBack As Integer,
            Optional startTime As DateTimeOffset? = Nothing,
            Optional endTime As DateTimeOffset? = Nothing,
            Optional cancel As CancellationToken = Nothing) As Task(Of BarResponse)

            Dim instrumentId As Integer
            If Not Integer.TryParse(contractId, instrumentId) Then
                ' Try resolving symbol via FavouriteContracts (no network call needed for known instruments)
                Dim fav = TopStepTrader.Core.Trading.FavouriteContracts.TryGetBySymbol(contractId)
                If fav IsNot Nothing Then
                    instrumentId = fav.InstrumentId
                Else
                    Logger.LogWarning("HistoryClient: '{Id}' is not a numeric instrumentId and not a known favourite — cannot fetch candles.", contractId)
                    Return New BarResponse With {.Success = False, .ErrorMessage = $"'{contractId}' is not a valid eToro instrumentId."}
                End If
            End If

            Dim interval = If(BarUnitMap.ContainsKey(unit), BarUnitMap(unit), "FiveMinutes")
            Dim count = Math.Min(Math.Max(unitsBack, 1), 1000)
            Dim endpoint = $"{_settings.BaseUrl}/api/v1/market-data/instruments/{instrumentId}/history/candles/desc/{interval}/{count}"

            Logger.LogInformation("Fetching {Count} {Interval} candles for instrument {Id}", count, interval, instrumentId)
            Dim candlesResp = Await GetAsync(Of CandlesResponse)(endpoint, cancel)

            ' Flatten the nested candle structure into the legacy BarDto list
            Dim bars As New List(Of BarDto)
            If candlesResp?.Candles IsNot Nothing Then
                For Each instrGroup In candlesResp.Candles
                    For Each c In instrGroup.Candles
                        bars.Add(New BarDto With {
                            .Timestamp = c.FromDate,
                            .Open = c.Open,
                            .High = c.High,
                            .Low = c.Low,
                            .Close = c.Close,
                            .Volume = CLng(c.Volume.GetValueOrDefault(0))
                        })
                    Next
                Next
                ' eToro returns newest-first (desc); reverse so oldest bar is first
                bars.Reverse()
            End If

            Return New BarResponse With {
                .Success = True,
                .Bars = bars
            }
        End Function

    End Class

End Namespace
