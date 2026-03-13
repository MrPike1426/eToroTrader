Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.Http
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Market

    ''' <summary>
    ''' Implements <see cref="IBarCollectionService"/>.
    '''
    ''' Ensures bars of the requested timeframe exist in the local SQLite database for a given
    ''' contract and date range, downloading missing bars from the ProjectX API if required.
    '''
    ''' Algorithm:
    '''   1. Count bars already in SQLite for (contractId, timeframe, startDate–endDate).
    '''   2. If ≥ 50 and they span ≥ 80% of the requested range → return success (cache hit).
    '''   3. If insufficient → paginate API forward from startDate in 500-bar batches.
    '''      Each batch is stored immediately; progress is reported after each one.
    '''   4. Count final total and return success/failure result.
    '''
    ''' Supported timeframes: OneMinute (1), FiveMinute (2), FifteenMinute (3),
    '''                        ThirtyMinute (4), OneHour (5).
    ''' Rate limiting: handled transparently by HistoryClient (50 req/30 s).
    ''' Deduplication: handled by BarRepository.BulkInsertAsync (INSERT OR IGNORE).
    ''' </summary>
    Public Class BarCollectionService
        Implements IBarCollectionService

        ' Maximum bars returned per API call (~500 is the ProjectX limit)
        Private Const BatchSize As Integer = 500

        ' BacktestEngine requires at least 50 bars; reject below this threshold
        Private Const MinBarsForBacktest As Integer = 50

        Private ReadOnly _historyClient As HistoryClient
        Private ReadOnly _barRepository As BarRepository
        Private ReadOnly _logger As ILogger(Of BarCollectionService)

        Public Sub New(historyClient As HistoryClient,
                       barRepository As BarRepository,
                       logger As ILogger(Of BarCollectionService))
            _historyClient = historyClient
            _barRepository = barRepository
            _logger = logger
        End Sub

        ''' <inheritdoc/>
        Public Async Function EnsureBarsAsync(
                contractId As String,
                startDate As Date,
                endDate As Date,
                timeframe As BarTimeframe,
                Optional progress As IProgress(Of String) = Nothing,
                Optional cancel As CancellationToken = Nothing) As Task(Of BarEnsureResult) _
                Implements IBarCollectionService.EnsureBarsAsync

            If String.IsNullOrWhiteSpace(contractId) Then
                Return Fail(contractId, "Contract ID is required", progress)
            End If

            ' Resolve API unit codes and the per-bar minute width for cursor advancement
            Dim apiUnit As Integer
            Dim apiUnitNumber As Integer
            Dim barMinutes As Integer
            TimeframeToApiParams(timeframe, apiUnit, apiUnitNumber, barMinutes)

            Dim tfLabel = TimeframeLabel(timeframe)

            ' Date range as UTC DateTimeOffset  (endDate + 1 day makes end inclusive)
            Dim fromDt = New DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified), TimeSpan.Zero)
            Dim toDt = New DateTimeOffset(DateTime.SpecifyKind(endDate.AddDays(1), DateTimeKind.Unspecified), TimeSpan.Zero)

            ' ── Step 1: Check existing bars in SQLite ──────────────────────────────────
            progress?.Report($"⏳ Checking local {tfLabel} bars for {contractId}...")

            Dim existing As List(Of MarketBar)
            Try
                existing = Await _barRepository.GetBarsAsync(
                    contractId, timeframe, fromDt, toDt, cancel)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "EnsureBarsAsync: DB query failed for {Contract}", contractId)
                Return Fail(contractId, $"Database error: {ex.Message}", progress)
            End Try

            If existing.Count >= MinBarsForBacktest Then
                ' UAT-BUG-007: A simple count ≥ 50 cache hit is too permissive.
                ' Live-trading bar downloads only fetch recent bars (e.g. 1 day).
                ' If those bars happen to fall inside the requested range they satisfy
                ' the ≥ 50 count but the backtest then only sees 1 day of data.
                '
                ' Fix: also validate that the cached bars span the majority of the
                ' requested date range.  For short ranges (≤ 7 calendar days) the simple
                ' count check is sufficient; for longer ranges the bars must cover at
                ' least 80 % of the requested span, measured from earliest-to-latest bar.
                Dim rangeSpan = (toDt - fromDt).TotalDays

                Dim spanOk As Boolean
                If rangeSpan <= 7D Then
                    spanOk = True
                Else
                    Dim earliestBar = existing.Min(Function(b) b.Timestamp)
                    Dim latestBar = existing.Max(Function(b) b.Timestamp)
                    Dim coveredDays = (latestBar - earliestBar).TotalDays
                    spanOk = coveredDays >= rangeSpan * 0.8D
                End If

                If spanOk Then
                    Dim msg = $"✓ {existing.Count:N0} {tfLabel} bars already available for {contractId}"
                    progress?.Report(msg)
                    _logger.LogInformation(
                        "EnsureBarsAsync: {Count} {Tf} bars in DB for {Contract} — span OK, skipping download",
                        existing.Count, tfLabel, contractId)
                    Return New BarEnsureResult With {
                        .Success = True,
                        .BarCount = existing.Count,
                        .ContractId = contractId,
                        .Message = msg
                    }
                End If

                _logger.LogInformation(
                    "EnsureBarsAsync: {Count} {Tf} bars in DB for {Contract} but they don't cover " &
                    "the requested range — downloading missing history",
                    existing.Count, tfLabel, contractId)
            End If

            ' ── Step 2: Paginate API forward from startDate in 500-bar batches ─────────
            _logger.LogInformation(
                "EnsureBarsAsync: {Count} {Tf} bars in DB for {Contract} (need ≥ {Min}). " &
                "Downloading from API (unit={U}, unitNumber={UN})...",
                existing.Count, tfLabel, contractId, MinBarsForBacktest, apiUnit, apiUnitNumber)

            Dim totalFetched As Integer = 0
            Dim totalInserted As Integer = 0
            Dim currentStart As DateTimeOffset = fromDt
            Dim batchNum As Integer = 0

            While currentStart < toDt
                cancel.ThrowIfCancellationRequested()

                batchNum += 1
                _logger.LogDebug(
                    "EnsureBarsAsync: fetching {Tf} batch {N} from {Start} to {End} for {Contract}",
                    tfLabel, batchNum, currentStart, toDt, contractId)

                ' ── API call ──────────────────────────────────────────────────────────
                Dim response As API.Models.Responses.BarResponse = Nothing
                Try
                    response = Await _historyClient.RetrieveBarsAsync(
                        contractId,
                        unit:=apiUnit,
                        unitNumber:=apiUnitNumber,
                        unitsBack:=BatchSize,
                        startTime:=currentStart,
                        endTime:=toDt,
                        cancel:=cancel)
                Catch ex As OperationCanceledException
                    Throw  ' Always propagate cancellation
                Catch ex As Exception
                    _logger.LogWarning(ex,
                        "EnsureBarsAsync: API error on {Tf} batch {N} for {Contract} — stopping download",
                        tfLabel, batchNum, contractId)
                    Exit While
                End Try

                ' No data returned — reached end of available history
                If response Is Nothing OrElse Not response.Success OrElse
                   response.Bars Is Nothing OrElse response.Bars.Count = 0 Then
                    _logger.LogInformation(
                        "EnsureBarsAsync: {Tf} batch {N} returned no bars for {Contract} — download complete",
                        tfLabel, batchNum, contractId)
                    Exit While
                End If

                ' ── Map BarDto → MarketBar (same logic as BarIngestionService) ─────────
                Dim bars = response.Bars _
                    .Select(Function(b) New MarketBar With {
                        .ContractId = contractId,
                        .Timeframe = CType(timeframe, BarTimeframe),
                        .Timestamp = DateTimeOffset.Parse(
                                          b.Timestamp, Nothing,
                                          System.Globalization.DateTimeStyles.RoundtripKind),
                        .Open = CDec(b.Open),
                        .High = CDec(b.High),
                        .Low = CDec(b.Low),
                        .Close = CDec(b.Close),
                        .Volume = b.Volume,
                        .VWAP = (CDec(b.Open) + CDec(b.Close)) / 2D
                    }) _
                    .ToList()

                totalFetched += bars.Count

                ' ── Persist to SQLite (INSERT OR IGNORE for deduplication) ──────────
                Dim inserted As Integer = 0
                Try
                    inserted = Await _barRepository.BulkInsertAsync(bars, timeframe, cancel)
                    totalInserted += inserted
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    _logger.LogError(ex,
                        "EnsureBarsAsync: BulkInsertAsync failed on {Tf} batch {N} for {Contract}",
                        tfLabel, batchNum, contractId)
                    Exit While
                End Try

                ' ── Report progress ────────────────────────────────────────────────────
                progress?.Report(
                    $"⏳ {contractId} ({tfLabel}): {totalFetched:N0} bars fetched, {totalInserted:N0} stored...")

                _logger.LogDebug(
                    "EnsureBarsAsync: {Tf} batch {N} — {Fetched} fetched, {Inserted} inserted",
                    tfLabel, batchNum, bars.Count, inserted)

                ' ── Advance cursor past the last bar received ─────────────────────────
                ' Use the actual bar width in minutes so the cursor is placed exactly at
                ' the start of the next expected bar (e.g. +5 min for 5-min bars).
                Dim lastTimestamp = bars.Max(Function(b) b.Timestamp)
                currentStart = lastTimestamp.AddMinutes(barMinutes)

                ' If fewer bars returned than requested, we've reached the API data boundary
                If bars.Count < BatchSize Then Exit While
            End While

            ' ── Step 3: Final count of bars in SQLite for the requested range ──────────
            Dim finalBars As New List(Of MarketBar)()
            Try
                finalBars = Await _barRepository.GetBarsAsync(
                    contractId, timeframe, fromDt, toDt, cancel)
            Catch ex As OperationCanceledException
                Throw
            Catch
                ' Use count 0 — result below will set the right message
            End Try

            Dim success = finalBars.Count >= MinBarsForBacktest

            Dim finalMessage As String
            If success Then
                finalMessage = $"✓ {finalBars.Count:N0} {tfLabel} bars available for {contractId} " &
                               $"({startDate:MM/dd/yyyy} – {endDate:MM/dd/yyyy})"
            ElseIf finalBars.Count > 0 Then
                finalMessage = $"⚠ Only {finalBars.Count:N0} {tfLabel} bars available — " &
                               "try a more recent or shorter date range"
            Else
                finalMessage = $"✗ No {tfLabel} bars available for {contractId}. Check API connection."
            End If

            progress?.Report(finalMessage)
            _logger.LogInformation(
                "EnsureBarsAsync: complete — {Count} {Tf} bars for {Contract} in range, success={Ok}",
                finalBars.Count, tfLabel, contractId, success)

            Return New BarEnsureResult With {
                .Success = success,
                .BarCount = finalBars.Count,
                .ContractId = contractId,
                .Message = finalMessage
            }
        End Function

        ' ── Helpers ───────────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Maps a BarTimeframe to the ProjectX API unit codes and the bar width in minutes.
        ''' Mirrors BarIngestionService.TimeframeToApiParams so both services use identical mappings.
        '''   API codes: unit=1 → 1-min, unit=2 → 5-min, unit=3 → 15-min,
        '''              unit=4 → 30-min, unit=5 → 1-hour, unit=6 → daily.
        ''' </summary>
        Private Shared Sub TimeframeToApiParams(tf As BarTimeframe,
                                                ByRef unit As Integer,
                                                ByRef unitNumber As Integer,
                                                ByRef barMinutes As Integer)
            Select Case tf
                Case BarTimeframe.OneMinute
                    unit = 1 : unitNumber = 1 : barMinutes = 1
                Case BarTimeframe.FiveMinute
                    unit = 2 : unitNumber = 2 : barMinutes = 5
                Case BarTimeframe.FifteenMinute
                    unit = 3 : unitNumber = 3 : barMinutes = 15
                Case BarTimeframe.ThirtyMinute
                    unit = 4 : unitNumber = 4 : barMinutes = 30
                Case BarTimeframe.OneHour
                    unit = 5 : unitNumber = 5 : barMinutes = 60
                Case Else
                    ' Default to 5-minute for any unsupported timeframe
                    unit = 2 : unitNumber = 2 : barMinutes = 5
            End Select
        End Sub

        ''' <summary>Short display label for a timeframe, e.g. "5-min", "1-hour".</summary>
        Private Shared Function TimeframeLabel(tf As BarTimeframe) As String
            Select Case tf
                Case BarTimeframe.OneMinute : Return "1-min"
                Case BarTimeframe.ThreeMinute : Return "3-min"
                Case BarTimeframe.FiveMinute : Return "5-min"
                Case BarTimeframe.FifteenMinute : Return "15-min"
                Case BarTimeframe.ThirtyMinute : Return "30-min"
                Case BarTimeframe.OneHour : Return "1-hour"
                Case BarTimeframe.FourHour : Return "4-hour"
                Case Else : Return tf.ToString()
            End Select
        End Function

        Private Shared Function Fail(contractId As String,
                                     message As String,
                                     progress As IProgress(Of String)) As BarEnsureResult
            progress?.Report($"✗ {message}")
            Return New BarEnsureResult With {
                .Success = False,
                .BarCount = 0,
                .ContractId = contractId,
                .Message = message
            }
        End Function

    End Class

End Namespace
