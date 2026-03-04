Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.Http
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Market

    ''' <summary>
    ''' Fetches historical bars from the ProjectX API and persists them to the database.
    ''' Maps API bar unit codes: 1=1min, 2=5min, 3=15min, 4=30min, 5=1hr, 6=1day.
    ''' </summary>
    Public Class BarIngestionService

        Private ReadOnly _historyClient As HistoryClient
        Private ReadOnly _barRepository As BarRepository
        Private ReadOnly _logger As ILogger(Of BarIngestionService)

        Public Sub New(historyClient As HistoryClient,
                       barRepository As BarRepository,
                       logger As ILogger(Of BarIngestionService))
            _historyClient = historyClient
            _barRepository = barRepository
            _logger = logger
        End Sub

        ''' <summary>
        ''' Fetch and store up to <paramref name="barsToFetch"/> bars for a contract.
        ''' Skips bars already in the database (based on latest stored timestamp).
        ''' </summary>
        Public Async Function IngestAsync(contractId As String,
                                          timeframe As BarTimeframe,
                                          Optional barsToFetch As Integer = 500,
                                          Optional cancel As CancellationToken = Nothing) As Task(Of Integer)

            ' Map timeframe into API unit + unitNumber (unit=1 == minutes, unitNumber == minutes)
            Dim apiUnit As Integer
            Dim apiUnitNumber As Integer
            TimeframeToApiParams(timeframe, apiUnit, apiUnitNumber)

            ' Find the latest bar we already have to avoid re-fetching
            Dim latestStored = Await _barRepository.GetLatestTimestampAsync(contractId, timeframe)
            Dim startTime As DateTimeOffset? = Nothing
            If latestStored.HasValue Then
                startTime = latestStored.Value.AddMinutes(1)
            End If

            _logger.LogInformation("Ingesting {N} bars for contract {Id}, timeframe {Tf}, since {Since}",
                                   barsToFetch, contractId, timeframe, If(startTime.HasValue, startTime.Value.ToString("g"), "recent"))

            ' Attempt sequence: try requested (with startTime if available), then fall back to
            ' requesting the most recent N bars (no startTime) with decreasing limits.
            Dim limitsToTry As Integer() = {barsToFetch, Math.Min(300, barsToFetch), 100, 50}
            Dim response As TopStepTrader.API.Models.Responses.BarResponse = Nothing
            Dim lastException As Exception = Nothing

            For i = 0 To limitsToTry.Length - 1
                Dim limit = limitsToTry(i)
                Try
                    If i = 0 Then
                        ' First attempt: use startTime if available, otherwise request most recent
                        response = Await _historyClient.RetrieveBarsAsync(contractId, apiUnit, apiUnitNumber, limit, startTime, Nothing, cancel)
                    Else
                        ' Fallback attempts: request most recent bars (no startTime)
                        response = Await _historyClient.RetrieveBarsAsync(contractId, apiUnit, apiUnitNumber, limit, Nothing, Nothing, cancel)
                    End If

                    If response IsNot Nothing AndAlso response.Success AndAlso response.Bars IsNot Nothing AndAlso response.Bars.Count > 0 Then
                        Exit For
                    End If

                    _logger.LogWarning("Attempt {I}: History API returned no bars or failure for {Id} (limit={Limit})", i + 1, contractId, limit)
                Catch ex As Exception
                    lastException = ex
                    _logger.LogWarning(ex, "Attempt {I}: Exception while retrieving bars for {Id} (limit={Limit})", i + 1, contractId, limit)
                End Try
            Next

            If response Is Nothing OrElse Not response.Success OrElse response.Bars Is Nothing OrElse response.Bars.Count = 0 Then
                _logger.LogWarning("Failed to retrieve bars for {Id} after retries. Last error: {Err}", contractId, If(lastException?.Message, "no response"))
                Return 0
            End If

            ' Map API BarDto → MarketBar (BulkInsertAsync expects domain objects)
            Dim bars = response.Bars.Select(Function(b) New MarketBar With {
                .ContractId = contractId,
                .Timeframe = CInt(timeframe),
                .Timestamp = DateTimeOffset.Parse(b.Timestamp, Nothing, System.Globalization.DateTimeStyles.RoundtripKind),
                .Open = CDec(b.Open),
                .High = CDec(b.High),
                .Low = CDec(b.Low),
                .Close = CDec(b.Close),
                .Volume = b.Volume,
                .VWAP = (CDec(b.Open) + CDec(b.Close)) / 2D   ' Approx until real VWAP available
            }).ToList()

            Dim inserted = Await _barRepository.BulkInsertAsync(bars, timeframe, cancel)
            _logger.LogInformation("Stored {N} new bars for contract {Id}", inserted, contractId)
            Return inserted
        End Function

        ''' <summary>
        ''' Returns the N most recent bars from the DB as domain objects for the ML engine.
        ''' </summary>
        Public Async Function GetBarsForMLAsync(contractId As String,
                                                 timeframe As BarTimeframe,
                                                 Optional maxBars As Integer = 200,
                                                 Optional cancel As CancellationToken = Nothing) As Task(Of IList(Of MarketBar))
            ' GetRecentBarsAsync already returns domain MarketBar objects sorted ascending
            Return Await _barRepository.GetRecentBarsAsync(contractId, timeframe, maxBars, cancel)
        End Function

        Private Shared Sub TimeframeToApiParams(tf As BarTimeframe, ByRef unit As Integer, ByRef unitNumber As Integer)
            ' Use discrete API unit codes that match the API's expectations
            ' (unit and unitNumber are the same for these predefined codes)
            Select Case tf
                Case BarTimeframe.OneMinute
                    unit = 1 : unitNumber = 1
                Case BarTimeframe.ThreeMinute
                    unit = 1 : unitNumber = 1  ' Fallback to 1-minute if 3-min not supported
                Case BarTimeframe.FiveMinute
                    unit = 2 : unitNumber = 2
                Case BarTimeframe.FifteenMinute
                    unit = 3 : unitNumber = 3
                Case BarTimeframe.ThirtyMinute
                    unit = 4 : unitNumber = 4
                Case BarTimeframe.OneHour
                    unit = 5 : unitNumber = 5
                Case BarTimeframe.Daily
                    unit = 6 : unitNumber = 6
                Case Else
                    unit = 2 : unitNumber = 2
            End Select
        End Sub

    End Class

End Namespace
