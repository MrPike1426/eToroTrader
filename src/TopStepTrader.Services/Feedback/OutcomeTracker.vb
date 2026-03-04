Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.Data.Entities
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Feedback

    ''' <summary>
    ''' Tracks the real-world outcome of every AI-originated order.
    ''' RecordOpenOutcomeAsync : called by AutoExecutionService after PlaceOrderAsync.
    ''' ResolveOpenOutcomesAsync: called periodically by OutcomeMonitorWorker to compute P&amp;L.
    ''' Registered as Scoped — OutcomeMonitorWorker creates a fresh scope each tick.
    ''' </summary>
    Public Class OutcomeTracker

        Private ReadOnly _outcomeRepo As TradeOutcomeRepository
        Private ReadOnly _barRepo As BarRepository
        Private ReadOnly _settings As TradingSettings
        Private ReadOnly _logger As ILogger(Of OutcomeTracker)

        ' Ticks per contract (used for P&L). NQ = $5/tick, ES = $12.50/tick.
        ' A simple configurable constant; production would look this up by contractId.
        Private Const TickSizePoints As Decimal = 0.25D
        Private Const TickValueDollars As Decimal = 12.5D   ' ES default

        Public Sub New(outcomeRepo As TradeOutcomeRepository,
                       barRepo As BarRepository,
                       tradingOptions As IOptions(Of TradingSettings),
                       logger As ILogger(Of OutcomeTracker))
            _outcomeRepo = outcomeRepo
            _barRepo = barRepo
            _settings = tradingOptions.Value
            _logger = logger
        End Sub

        ' ── Record new outcome after order placement ───────────────────────────

        ''' <summary>
        ''' Record an open outcome for an AI-originated order.
        ''' Called by AutoExecutionService immediately after PlaceOrderAsync.
        ''' EntryPrice will be 0 if fill confirmation has not yet arrived; the
        ''' resolution pass will approximate it from the entry bar in that case.
        ''' </summary>
        Public Async Function RecordOpenOutcomeAsync(order As Order) As Task
            If Not order.SourceSignalId.HasValue Then Return

            Dim outcome As New TradeOutcome With {
                .SignalId = order.SourceSignalId.Value,
                .OrderId = order.Id,
                .ContractId = order.ContractId,
                .Timeframe = _settings.DefaultTimeframe,
                .SignalType = order.Side.ToString(),
                .EntryTime = DateTimeOffset.UtcNow,
                .EntryPrice = If(order.FillPrice.HasValue, order.FillPrice.Value, 0D),
                .IsOpen = True
            }

            Dim id = Await _outcomeRepo.SaveOutcomeAsync(outcome)
            _logger.LogInformation(
                "Outcome tracker: recorded open outcome {Id} for signal {SignalId} (entry={Price:F2})",
                id, outcome.SignalId, outcome.EntryPrice)
        End Function

        ' ── Periodic resolution — called by OutcomeMonitorWorker every 5 min ─

        ''' <summary>
        ''' Scan open outcomes and resolve those old enough to have a valid exit bar.
        ''' Uses bar close prices already ingested by BarIngestionWorker.
        ''' </summary>
        Public Async Function ResolveOpenOutcomesAsync(cancel As CancellationToken) As Task
            Dim openOutcomes = Await _outcomeRepo.GetOpenOutcomesAsync()
            If openOutcomes.Count = 0 Then Return

            _logger.LogDebug("Resolving {Count} open outcomes...", openOutcomes.Count)

            For Each entity In openOutcomes
                If cancel.IsCancellationRequested Then Return
                Try
                    Await TryResolveAsync(entity, cancel)
                Catch ex As Exception
                    _logger.LogWarning(ex, "Could not resolve outcome {Id}", entity.Id)
                End Try
            Next
        End Function

        Private Async Function TryResolveAsync(entity As TradeOutcomeEntity,
                                               cancel As CancellationToken) As Task
            ' Exit window = entry time + (timeframe × 3) minutes
            Dim lookAheadMinutes = entity.Timeframe * 3
            Dim exitWindow = entity.EntryTime.AddMinutes(lookAheadMinutes)

            ' Don't resolve until the exit window has fully elapsed
            If DateTimeOffset.UtcNow < exitWindow Then Return

            Dim tf = CType(entity.Timeframe, BarTimeframe)

            ' ── Entry price: use stored value or look up from the entry bar ──
            Dim entryPrice = entity.EntryPrice
            If entryPrice = 0D Then
                Dim entryBars = Await _barRepo.GetBarsAsync(
                    entity.ContractId, tf,
                    entity.EntryTime.AddMinutes(-entity.Timeframe),
                    entity.EntryTime.AddMinutes(entity.Timeframe),
                    cancel)
                Dim entryBar = entryBars.OrderBy(Function(b) b.Timestamp).FirstOrDefault()
                If entryBar Is Nothing Then
                    _logger.LogDebug(
                        "No entry bar found for outcome {Id} at {Time} — deferring",
                        entity.Id, entity.EntryTime)
                    Return
                End If
                entryPrice = entryBar.Close
            End If

            ' ── Exit bar: first bar at/after the exit window ──────────────────
            Dim exitBars = Await _barRepo.GetBarsAsync(
                entity.ContractId, tf,
                exitWindow.AddMinutes(-entity.Timeframe),
                exitWindow.AddMinutes(entity.Timeframe),
                cancel)

            Dim exitBar = exitBars.OrderBy(Function(b) b.Timestamp).FirstOrDefault()
            If exitBar Is Nothing Then
                _logger.LogDebug(
                    "No exit bar found for outcome {Id} at {Time} — bar not yet ingested",
                    entity.Id, exitWindow)
                Return  ' Try again on the next scan
            End If

            Dim exitPrice = exitBar.Close
            Dim direction = If(entity.SignalType = "Buy", 1D, -1D)
            Dim priceDiff = (exitPrice - entryPrice) * direction
            Dim pnl = priceDiff / TickSizePoints * TickValueDollars

            Await _outcomeRepo.ResolveOutcomeAsync(
                entity.Id,
                exitBar.Timestamp,
                exitPrice,
                pnl,
                pnl > 0,
                "LookAheadExpiry")

            _logger.LogInformation(
                "Outcome {Id} resolved: {Side} entry={Entry:F2} exit={Exit:F2} PnL=${PnL:F0} ({Result})",
                entity.Id, entity.SignalType, entryPrice, exitPrice, pnl,
                If(pnl > 0, "WIN", "LOSS"))
        End Function

    End Class

End Namespace
