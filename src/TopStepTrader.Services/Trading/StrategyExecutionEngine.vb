Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Models.Diagnostics
Imports TopStepTrader.ML.Features
Imports TopStepTrader.Services.Diagnostics
Imports TopStepTrader.Services.Market

Namespace TopStepTrader.Services.Trading

    ''' <summary>
    ''' Manually-started execution engine that monitors live bars every 30 seconds,
    ''' evaluates the strategy condition, and places entry + bracket orders when triggered.
    ''' Raises LogMessage events that the UI subscribes to for real-time feedback.
    ''' Register as Transient — one instance per strategy session.
    ''' </summary>
    Public Class StrategyExecutionEngine
        Implements IDisposable

        ' ── Dependencies ──────────────────────────────────────────────────────────
        Private ReadOnly _ingestionService As BarIngestionService
        Private ReadOnly _orderService As IOrderService
        Private ReadOnly _riskGuard As IRiskGuardService
        Private ReadOnly _logger As ILogger(Of StrategyExecutionEngine)

        ' ── State ─────────────────────────────────────────────────────────────────
        Private _strategy As StrategyDefinition
        Private _timer As System.Threading.Timer
        Private _cts As CancellationTokenSource       ' cancelled by Stop() / Dispose()
        Private _callbackRunning As Integer = 0       ' Interlocked reentrancy guard
        Private _positionOpen As Boolean = False
        Private _disposed As Boolean = False
        Private _running As Boolean = False
        Private _lastCheckedBarCount As Integer = 0  ' tracks when to re-log bar-window / gap info
        Private _openPositionId As Long? = Nothing     ' eToro positionId of the live trade; Nothing = no open position

        ' ── Events
        ''' <summary>Raised on the thread-pool whenever a log line is produced.</summary>
        Public Event LogMessage As EventHandler(Of String)
        ''' <summary>Raised when the engine stops (expired, stopped, or errored).</summary>
        Public Event ExecutionStopped As EventHandler(Of String)
        ''' <summary>Raised when an entry order is placed (trade opened).</summary>
        Public Event TradeOpened As EventHandler(Of TradeOpenedEventArgs)
        ''' <summary>Raised when the bracket position closes (TP or SL filled).</summary>
        Public Event TradeClosed As EventHandler(Of TradeClosedEventArgs)
        ''' <summary>Raised each bar-check cycle with the latest bar close price, for live P&amp;L updates.</summary>
        Public Event BarPriceUpdated As EventHandler(Of Decimal)
        ''' <summary>Raised every 30-second tick while a position is open with API-authoritative P&amp;L and positionId.</summary>
        Public Event PositionSynced As EventHandler(Of PositionSyncedEventArgs)
        ''' <summary>Raised after every bar check with the live EMA/RSI confidence score (0–100), even when no signal fires.</summary>
        Public Event ConfidenceUpdated As EventHandler(Of ConfidenceUpdatedEventArgs)
        ''' <summary>Raised when the Turtle bracket is first placed or advances a step.</summary>
        Public Event TurtleBracketChanged As EventHandler(Of TurtleBracketChangedEventArgs)

        ' ── Market-open guard ─────────────────────────────────────────────────────
        ''' <summary>
        ''' Optional predicate evaluated immediately before any entry or scale-in order is
        ''' submitted.  Return False to suppress order placement while keeping the bar-check
        ''' loop and confidence telemetry fully active (used by Hydra market-hours guard).
        ''' Defaults to Function() True so all non-Hydra callers are unaffected.
        ''' </summary>
        Public Property IsOrderingAllowed As Func(Of Boolean) = Function() True

        ' ── Trade-tracking state (for performance panel) ──────────────────────────
        Private _lastEntryPrice As Decimal = 0D
        Private _lastEntrySide As OrderSide = OrderSide.Buy
        Private _lastConfidencePct As Integer = 0
        Private _lastTpPrice As Decimal = 0D
        Private _lastSlPrice As Decimal = 0D
        Private _lastTpExternalId As Long? = Nothing
        Private _pendingConfidencePct As Integer = 0
        Private _lastFinalAmount As Decimal = 0D   ' cash amount submitted to eToro (after min-notional clamp)
        Private _lastLeverage As Integer = 1        ' leverage applied to that order
        ' Running sum of DollarPerPoint across ALL open positions (initial + scale-ins).
        ' DollarPerPoint = units open = (amount × leverage) / price per position.
        ' The bracket is rescaled after each scale-in so SL/TP advancement reflects
        ' the TOTAL portfolio P&L, not just the initial single position.
        Private _totalDollarPerPoint As Decimal = 0D
        ' Timestamp recorded when a position is confirmed open (after PlaceOrderAsync succeeds).
        ' Used to skip the portfolio close-check for the first 60 s so the eToro API has time
        ' to reflect the new position before we would mistakenly declare it closed.
        Private _positionOpenedAt As DateTimeOffset = DateTimeOffset.MinValue
        ' Timestamp of the most recent broker-confirmed position close (SL/TP, flatten, or trail).
        ' Enforces a re-entry cooldown so the engine cannot place a new order in the same
        ' 30-second tick that detected the close — preventing instant re-entry cascades.
        Private _lastPositionClosedAt As DateTimeOffset = DateTimeOffset.MinValue
        Private Const ReEntryCooldownSeconds As Integer = 60  ' minimum gap between a close and the next entry
        Private _lastApiPnl As Decimal = 0D     ' last broker-reported unrealised P&L; used as final P&L on close
        ' Cloud-edge SL price set by the MultiConfluence case; consumed once by PlaceBracketOrdersAsync.
        ' Nothing for all other strategy types.
        Private _mcCloudSlPrice As Decimal? = Nothing
        ' Absolute SL price for LULT Divergence — trigger wave extreme ± ATR-scaled tick buffer.
        ' Set when the 6-step LULT signal fires; Nothing for all other strategy types.
        Private _lultTriggerExtreme As Decimal? = Nothing
        ' Set to True when a startup-detected (or orphan-detected) position is attached but the
        ' Turtle bracket could not be initialized because ATR = 0 at that moment.
        ' Cleared on the first DoCheckAsync tick where ATR is available, and on every position close/reset.
        Private _bracketInitPending As Boolean = False
        ' Confirmed reversal requires ReversalConfirmBars consecutive NEW bars each
        ' producing an opposite-direction signal.  Bar-timestamp de-duplication prevents
        ' the 30-second timer from counting multiple checks of the same last bar as
        ' separate confirmation steps — only a genuine new completed bar advances the counter.
        Private Const ReversalConfirmBars As Integer = 2
        Private _currentTrendSide As OrderSide?          ' direction we are currently trading
        Private _reversalCandidateSide As OrderSide?     ' opposite side being confirmed
        Private _reversalConfirmCount As Integer = 0     ' consecutive new-bar opposite signals seen
        Private _lastBarTimestamp As DateTimeOffset = DateTimeOffset.MinValue

        ' ── High-fidelity diagnostic logging (8-hour test session) ───────────────
        Private ReadOnly _diagLogger As DiagnosticLogger
        Private ReadOnly _marketData As IMarketDataService
        ' Pending entry built in the signal-evaluation branch; logged in the dispatch block.
        ' Nothing = current strategy does not build diagnostic entries this tick.
        Private _pendingDiagEntry As DiagnosticLogEntry = Nothing
        ' Complete TRADE record held in memory until the position closes; written once
        ' as a single JSON line with Outcome fully populated (MFE, MAE, P&L, status).
        Private _openTradeDiagEntry As DiagnosticLogEntry = Nothing

        ' ── Confidence-driven scale-in state ──────────────────────────────────────
        ' Used exclusively by the EmaRsiWeightedScore strategy condition.
        ' ScaleInAmount and ScaleInLeverage are both driven by the strategy definition (set from UI).
        Private Const ScaleInRequiredTicks As Integer = 3      ' consecutive extreme ticks before scale-in fires
        Private Const MaxScaleInTrades As Integer = 3          ' cap: max additional trades after initial
        Private Const ExtremeConfidenceHighThreshold As Integer = 85  ' bullish extreme (upPct ≥ this)
        Private Const ExtremeConfidenceLowThreshold As Integer = 25   ' bearish extreme (upPct ≤ this)
        Private Const NeutralConfidenceLow As Integer = 40     ' neutral band lower bound (upPct)
        Private Const NeutralConfidenceHigh As Integer = 60    ' neutral band upper bound (upPct)
        Private _extremeConfidenceDurationCount As Integer = 0  ' consecutive extreme-confidence ticks
        Private _scaleInTradeCount As Integer = 0               ' scale-in trades placed this session
        ' Count of all open trades tracked this session (initial + scale-ins).
        ' Incremented on each successful PlaceOrder; used to fire the correct number
        ' of TradeClosed events when the broker reports no open positions.
        Private _openTradeCount As Integer = 0
        Private _currentAtrValue As Decimal = 0D      ' ATR(14) from latest bar — drives dynamic SL/TP levels
        Private _currentEma21 As Decimal = 0D          ' EMA21 from latest bar — logged as quality metric alongside scale-in
        Private Const ScaleInBullThreshold As Integer = 80   ' bull score > this required for scale-in (UP ≥ 81%)
        Private Const ScaleInBearThreshold As Integer = 20   ' bull score < this required for bear scale-in (DOWN ≥ 81%)

        ' ── Mid-confidence adverse exit (EmaRsiWeightedScore only) ──────────────────
        ' When a position is open and confidence has clearly shifted into the opposite
        ' direction (above NeutralConfidenceHigh but below the full reversal threshold)
        ' for this many consecutive NEW bars, the position is flattened immediately.
        ' This closes the "61–84% purgatory zone" where neither neutral exit (≤60%)
        ' nor reversal exit (≥85% for 2 bars) fires, allowing large losses to accumulate.
        Private Const AdverseConfidenceBars As Integer = 3     ' 3 new bars = 3 × timeframe minutes of confirmation
        Private _adverseConfidenceCount As Integer = 0         ' consecutive new-bar adverse ticks

        ' ── Turtle bracket state (reset per position) ───────────────────────────────
        Private _turtleBracket As TopStepTrader.Core.Trading.TurtleBracketState = Nothing

        Public Sub New(ingestionService As BarIngestionService,
                       orderService As IOrderService,
                       riskGuard As IRiskGuardService,
                       logger As ILogger(Of StrategyExecutionEngine),
                       diagLogger As DiagnosticLogger,
                       marketData As IMarketDataService)
            _ingestionService = ingestionService
            _orderService = orderService
            _riskGuard = riskGuard
            _logger = logger
            _diagLogger = diagLogger
            _marketData = marketData
        End Sub

        ' ── Public API ────────────────────────────────────────────────────────────

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return _running
            End Get
        End Property

        ''' <summary>
        ''' Start monitoring. Sets ExpiresAt on the strategy and begins the 30-second polling loop.
        ''' </summary>
        Public Sub Start(strategy As StrategyDefinition)
            If _running Then Return
            _strategy = strategy
            _strategy.ExpiresAt = DateTimeOffset.UtcNow.AddHours(strategy.DurationHours)
            _positionOpen = False
            _openPositionId = Nothing
            _openTradeCount = 0
            _positionOpenedAt = DateTimeOffset.MinValue
            _lastPositionClosedAt = DateTimeOffset.MinValue
            _lastApiPnl = 0D
            _mcCloudSlPrice = Nothing
            _lultTriggerExtreme = Nothing
            _currentTrendSide = Nothing
            _reversalCandidateSide = Nothing
            _reversalConfirmCount = 0
            _lastBarTimestamp = DateTimeOffset.MinValue
            _extremeConfidenceDurationCount = 0
            _scaleInTradeCount = 0
            _adverseConfidenceCount = 0
            _currentAtrValue = 0D
            _currentEma21 = 0D
            _totalDollarPerPoint = 0D
            ResetTrailState()
            _running = True
            _lastCheckedBarCount = 0   ' reset so bar-window is logged on first tick of this session
            _cts = New CancellationTokenSource()
            Interlocked.Exchange(_callbackRunning, 0)

            ' ── Diagnostic session reset ───────────────────────────────────────────
            _openTradeDiagEntry = Nothing
            _pendingDiagEntry = Nothing
            _diagLogger?.StartSession(strategy.ContractId, strategy.Name)

            Log($"Strategy started — {strategy.ContractId} | {strategy.Name}")
            Log($"Duration: {strategy.DurationHours}hrs | Expires: {strategy.ExpiresAt:HH:mm} UTC")
            Log($"Checking bars every 30 seconds...")

            ' ── Existing-position check on startup ──────────────────────────────
            ' Query the broker immediately so the engine knows about any open
            ' positions left by a previous session.  If found, set _positionOpen = True
            ' so the engine skips the initial-entry path and goes straight to monitoring.
            ' This prevents piling new orders on top of already-open ones.
            Task.Run(Async Function() As Task
                         Try
                             Dim snapshot = Await _orderService.GetLivePositionSnapshotAsync(
                                 strategy.AccountId, strategy.ContractId, Nothing, _cts.Token)
                             If snapshot IsNot Nothing Then
                                 _positionOpen = True
                                 _openPositionId = snapshot.PositionId
                                 _positionOpenedAt = DateTimeOffset.UtcNow.AddSeconds(-61) ' skip propagation guard
                                 If snapshot.OpenRate > 0D Then
                                     _lastEntryPrice = snapshot.OpenRate
                                     _lastEntrySide = If(snapshot.IsBuy, OrderSide.Buy, OrderSide.Sell)
                                 End If
                                 _currentTrendSide = If(snapshot.IsBuy, OrderSide.Buy, OrderSide.Sell)
                                 _lastFinalAmount = snapshot.Amount
                                 _lastLeverage = snapshot.Leverage
                                 _openTradeCount = snapshot.PositionCount
                                 ' Infer how many scale-ins have already been placed so the cap
                                 ' is enforced correctly when the engine restarts with positions open.
                                 ' initial trade = 1; every additional position = one scale-in.
                                 _scaleInTradeCount = Math.Min(MaxScaleInTrades,
                                                               Math.Max(0, snapshot.PositionCount - 1))
                                 ' Seed total DPP from the aggregate units the broker reports so
                                 ' the deferred bracket init uses the correct combined sensitivity.
                                 _totalDollarPerPoint = If(snapshot.Units > 0D, snapshot.Units, 0D)
                                 Dim startupSide = If(snapshot.IsBuy, OrderSide.Buy, OrderSide.Sell)

                                 ' Always attach to the existing position — regardless of current P&L.
                                 ' The engine cannot know the intended risk sizing of a position that was
                                 ' opened at a different notional or placed manually on eToro, so
                                 ' rescue-closing it on startup based on the current strategy's SL
                                 ' dollar amount is inappropriate.  The turtle bracket will establish
                                 ' SL/TP protection once ATR is available on the first bar-check tick.
                                 RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(startupSide, strategy.ContractId, 100,
                                         snapshot.OpenedAtUtc, Nothing, snapshot.PositionId,
                                         snapshot.OpenedAtUtc, snapshot.Amount, snapshot.Leverage, snapshot.OpenRate))
                                 Dim pnlStr = If(snapshot.UnrealizedPnlUsd <> 0D,
                                     $" P&L=${snapshot.UnrealizedPnlUsd:F2}", String.Empty)
                                 Dim capStr = If(_scaleInTradeCount >= MaxScaleInTrades,
                                                 $"scale-in cap REACHED ({_scaleInTradeCount}/{MaxScaleInTrades})",
                                                 $"scale-in {_scaleInTradeCount}/{MaxScaleInTrades} used")
                                 Log($"⚠️  Existing {snapshot.PositionCount} position(s) detected on startup " &
                                     $"(positionId={snapshot.PositionId}, entry={snapshot.OpenRate:F4}, " &
                                     $"units={snapshot.Units:F3}{pnlStr}, {capStr}) — attaching and applying turtle bracket.")
                                 ' Turtle bracket cannot be initialized here because ATR = 0 before the
                                 ' first bar check.  Set the pending flag so DoCheckAsync creates the
                                 ' bracket on the first tick where ATR is available.
                                 _bracketInitPending = True
                             Else
                                 Log($"✓ No existing positions for {strategy.ContractId} — ready to trade.")
                             End If
                         Catch ex As Exception
                             Log($"⚠️  Startup position check failed: {ex.Message} — assuming no open positions.")
                         End Try
                     End Function)

            ' ── Market data subscription ────────────────────────────────────────────
            ' PlaceBracketOrdersAsync uses a live bid/ask quote to anchor SL/TP to the
            ' actual fill price (BUY fills at ASK; SELL fills at BID).  Without a quote
            ' the live-quote guard defers every order attempt indefinitely.  Subscribe
            ' here so that quotes begin arriving from the MarketHub before the first
            ' order-placement tick fires.  Unsubscription happens in [Stop].
            Task.Run(Async Function() As Task
                         Try
                             Await _marketData.SubscribeAsync(_strategy.ContractId)
                             Log($"📡 Live quotes subscribed — {_strategy.ContractId}")
                         Catch ex As Exception
                             Log($"⚠️  Market data subscription failed for {_strategy.ContractId}: {ex.Message} — orders will use bar-close price until resolved")
                         End Try
                     End Function)

            ' 3-second initial delay gives the startup position-check Task.Run time to complete
            ' before the first bar-check tick fires, eliminating a race where _positionOpen
            ' could still be False when the timer's first callback runs.
            _timer = New System.Threading.Timer(AddressOf TimerCallback, Nothing,
                                                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30))
        End Sub

        ''' <summary>Stop the engine and raise ExecutionStopped event.</summary>
        Public Sub [Stop](Optional reason As String = "Stopped by user")
            If Not _running Then Return
            _running = False
            _cts?.Cancel()                          ' cancel any in-flight API call immediately
            _timer?.Change(Timeout.Infinite, 0)     ' prevent future timer ticks

            ' Unsubscribe from live market-data quotes — cleanup counterpart to
            ' the SubscribeAsync call in Start().  Best-effort: hub disconnection is
            ' non-critical so failures are silently swallowed.
            Task.Run(Async Function() As Task
                         Try
                             Await _marketData.UnsubscribeAsync(_strategy.ContractId)
                         Catch
                             ' Best-effort — do not surface hub errors on engine stop
                         End Try
                     End Function)

            ' RC-3: warn the user if positions are still open so they know the account is
            ' exposed between sessions.  Neutral-exit logic requires the engine to be running;
            ' on next Start() the startup position check (RC-2) will re-attach and the first
            ' tick will evaluate confidence and flatten if the band is neutral.
            If _positionOpen Then
                Log($"⚠️  POSITIONS STILL OPEN — {_strategy?.ContractId} has active positions. " &
                    $"Monitor manually or restart the engine to resume automated management.")
            End If
            ' Flush any in-memory TRADE record as ENGINE_STOPPED before closing the log
            If _openTradeDiagEntry IsNot Nothing Then
                If _openTradeDiagEntry.Outcome Is Nothing Then
                    _openTradeDiagEntry.Outcome = New DiagOutcome()
                End If
                _openTradeDiagEntry.Outcome.Status = "ENGINE_STOPPED"
                _openTradeDiagEntry.Outcome.TradeLifetimeSeconds =
                    If(_positionOpenedAt > DateTimeOffset.MinValue,
                       CLng((DateTimeOffset.UtcNow - _positionOpenedAt).TotalSeconds), 0L)
                _openTradeDiagEntry.Timestamp = DateTimeOffset.UtcNow.ToString("o")
                _diagLogger?.WriteEntry(_openTradeDiagEntry)
                _openTradeDiagEntry = Nothing
            End If
            _diagLogger?.CloseSession()
            Log($"■ Strategy stopped — {reason}")
            RaiseEvent ExecutionStopped(Me, reason)
        End Sub

        ' ── Timer callback ────────────────────────────────────────────────────────

        Private Sub TimerCallback(state As Object)
            If Not _running Then Return

            ' Reentrancy guard: skip this tick if the previous check hasn't finished yet.
            ' Prevents API calls from piling up when the network is slow.
            If Interlocked.CompareExchange(_callbackRunning, 1, 0) <> 0 Then
                Log("⏭  Previous bar check still running — skipping this tick")
                Return
            End If

            Task.Run(Async Function() As Task
                         Try
                             Await DoCheckAsync()
                         Catch ex As OperationCanceledException
                             ' Normal: Stop() was called while an API request was in flight.
                         Catch ex As Exception
                             _logger.LogError(ex, "StrategyExecutionEngine unhandled error")
                             Log($"⚠️  Error during bar check: {ex.Message}")
                         Finally
                             Interlocked.Exchange(_callbackRunning, 0)
                         End Try
                     End Function)
        End Sub

        Private Async Function DoCheckAsync() As Task
            If Not _running Then Return
            Dim ct = If(_cts IsNot Nothing, _cts.Token, CancellationToken.None)

            ' ── Check expiry ──────────────────────────────────────────────────────
            If DateTimeOffset.UtcNow > _strategy.ExpiresAt Then
                [Stop]("Strategy duration expired")
                Return
            End If

            Dim remaining = _strategy.ExpiresAt - DateTimeOffset.UtcNow
            Dim remStr = $"{CInt(remaining.TotalHours)}h {remaining.Minutes}m remaining"

            ' ── Fetch bars ────────────────────────────────────────────────────────
            Dim timeframe = CType(If(_strategy.TimeframeMinutes = 1, BarTimeframe.OneMinute,
                                  If(_strategy.TimeframeMinutes = 5, BarTimeframe.FiveMinute,
                                  If(_strategy.TimeframeMinutes = 15, BarTimeframe.FifteenMinute,
                                  If(_strategy.TimeframeMinutes = 60, BarTimeframe.OneHour,
                                     BarTimeframe.FiveMinute)))), BarTimeframe)

            ' Compute minimum bars required by the strategy and request a generous buffer
            ' above that threshold so the DB is fully populated on the very first tick.
            ' Previously IngestAsync was called with 50 while minBars = IndicatorPeriod+5 = 55,
            ' causing a ~25-min accumulation wait (5 live bars × 5 min) before the strategy
            ' could evaluate at all.  fetchCount = max(minBars+15, 70) eliminates that delay.
            Dim minBars = _strategy.IndicatorPeriod + 5
            Dim fetchCount = Math.Max(minBars + 15, 70)  ' buffer above minBars guard

            ' Ingest fresh bars — on the very first call this populates the DB with fetchCount
            ' bars (e.g. 70 for EMA/RSI Combined) so the strategy can run immediately.
            Await _ingestionService.IngestAsync(_strategy.ContractId, timeframe, fetchCount, ct)

            Dim bars = Await _ingestionService.GetBarsForMLAsync(_strategy.ContractId, timeframe, fetchCount, ct)

            If bars Is Nothing OrElse bars.Count < minBars Then
                Dim barCount = If(bars Is Nothing, 0, bars.Count)
                If barCount = 0 Then
                    Log($"No bars returned for '{_strategy.ContractId}' — market may be closed or outside trading hours. Retrying… ({remStr})")
                Else
                    Log($"Waiting for bars — have {barCount}/{minBars} needed ({remStr})")
                End If
                Return
            End If

            ' ── Bar-window and gap-continuity logging ────────────────────────────
            ' Fires on startup and whenever a new bar arrives (bar count increases).
            ' Surfaces any timestamp gaps (market closures, missing data) that would
            ' cause EMA/RSI warmup to span non-contiguous sessions.
            If bars.Count > _lastCheckedBarCount Then
                _lastCheckedBarCount = bars.Count
                Dim tfMin = _strategy.TimeframeMinutes
                Dim gapThresholdMin = tfMin * 2.5
                Log($"📊 Bar window: {bars.First().Timestamp:yyyy-MM-dd HH:mm} UTC → {bars.Last().Timestamp:yyyy-MM-dd HH:mm} UTC ({bars.Count} bars, {tfMin}-min tf)")
                Dim gapCount As Integer = 0
                For i = 1 To bars.Count - 1
                    Dim gapMin = (bars(i).Timestamp - bars(i - 1).Timestamp).TotalMinutes
                    If gapMin > gapThresholdMin Then
                        Log($"⚠️  Gap at bar {i}: {bars(i - 1).Timestamp:HH:mm} UTC → {bars(i).Timestamp:HH:mm} UTC ({gapMin:F0} min, expected {tfMin} min)")
                        gapCount += 1
                    End If
                Next
                If gapCount = 0 Then
                    Log($"✓ Bar series contiguous — {bars.Count} bars, no gaps > {CInt(gapThresholdMin)} min")
                Else
                    Log($"⚠️  {gapCount} gap(s) detected — EMA/RSI warmup spans a market closure; indicators may be unreliable across the gap")
                End If
            End If

            ' ── Evaluate condition ────────────────────────────────────────────────
            Dim closes = bars.Select(Function(b) b.Close).ToList()
            Dim highs = bars.Select(Function(b) b.High).ToList()
            Dim lows = bars.Select(Function(b) b.Low).ToList()

            Dim lastBar = bars.Last()
            RaiseEvent BarPriceUpdated(Me, CDec(lastBar.Close))
            Dim side As OrderSide? = Nothing
            Dim rawUpPct As Integer = 0    ' captured from EmaRsiWeightedScore for confidence actions
            Dim rawDownPct As Integer = 0

            ' ── Bar-period de-duplication guard ─────────────────────────────────
            ' The BarIngestionService may store a partially-formed "current" bar whose
            ' sub-minute timestamp advances on every 30-second API poll.  Without
            ' snapping to the canonical period boundary, isNewBar would be True on every
            ' tick, firing scale-ins every 90 seconds instead of every 15 minutes (3 bars).
            ' Snap to the start of the current period (floor to timeframe minutes).
            Dim periodTicks = TimeSpan.FromMinutes(_strategy.TimeframeMinutes).Ticks
            Dim barPeriodStart = New DateTimeOffset(
                lastBar.Timestamp.Ticks - (lastBar.Timestamp.Ticks Mod periodTicks),
                lastBar.Timestamp.Offset)
            Dim isNewBar = (barPeriodStart > _lastBarTimestamp)
            If isNewBar Then _lastBarTimestamp = barPeriodStart

            ' ── Stale bar guard ──────────────────────────────────────────────────────
            ' When the market is closed the ingestion service stops receiving new bars
            ' but the DB retains the last session's history.  barIsStale = True when the
            ' most recent bar is older than 3× the strategy timeframe — a reliable sign
            ' that the market is not currently producing data (after-hours, weekend, or
            ' daily maintenance break).  Entry signals and order placement are suppressed;
            ' position monitoring (broker sync + trailing bracket) continues normally.
            Dim barAgeMins = (DateTimeOffset.UtcNow - lastBar.Timestamp).TotalMinutes
            Dim barIsStale = barAgeMins > _strategy.TimeframeMinutes * 3.0

            If Not barIsStale Then

                Select Case _strategy.Condition
                    Case StrategyConditionType.FullCandleOutsideBands,
                     StrategyConditionType.CloseOutsideBands
                        Dim bands = TechnicalIndicators.BollingerBands(closes,
                                                                   _strategy.IndicatorPeriod,
                                                                   _strategy.IndicatorMultiplier)
                        Dim upper = CDec(TechnicalIndicators.LastValid(bands.Upper))
                        Dim lower = CDec(TechnicalIndicators.LastValid(bands.Lower))
                        Dim middle = CDec(TechnicalIndicators.LastValid(bands.Middle))

                        If _strategy.Condition = StrategyConditionType.FullCandleOutsideBands Then
                            ' Entire candle must be outside the band
                            If _strategy.GoLongWhenBelowBands AndAlso lastBar.High < lower Then
                                Log($"✅ Full candle below lower band! High={lastBar.High:F2} < Lower={lower:F2}")
                                side = OrderSide.Buy
                            ElseIf _strategy.GoShortWhenAboveBands AndAlso lastBar.Low > upper Then
                                Log($"✅ Full candle above upper band! Low={lastBar.Low:F2} > Upper={upper:F2}")
                                side = OrderSide.Sell
                            Else
                                Log($"Bar checked — Close={lastBar.Close:F2} | BB [{lower:F2} — {middle:F2} — {upper:F2}] | no signal ({remStr})")
                            End If
                        Else
                            ' Close outside band
                            If _strategy.GoLongWhenBelowBands AndAlso lastBar.Close < lower Then
                                Log($"✅ Close below lower band! Close={lastBar.Close:F2} < Lower={lower:F2}")
                                side = OrderSide.Buy
                            ElseIf _strategy.GoShortWhenAboveBands AndAlso lastBar.Close > upper Then
                                Log($"✅ Close above upper band! Close={lastBar.Close:F2} > Upper={upper:F2}")
                                side = OrderSide.Sell
                            Else
                                Log($"Bar checked — Close={lastBar.Close:F2} | BB [{lower:F2}—{upper:F2}] | no signal ({remStr})")
                            End If
                        End If

                    Case StrategyConditionType.RSIOversold, StrategyConditionType.RSIOverbought
                        Dim rsi = TechnicalIndicators.RSI(closes, _strategy.IndicatorPeriod)
                        Dim rsiVal = TechnicalIndicators.LastValid(rsi)

                        If _strategy.GoLongWhenBelowBands AndAlso rsiVal < 30 Then
                            Log($"✅ RSI oversold! RSI={rsiVal:F1} < 30")
                            side = OrderSide.Buy
                        ElseIf _strategy.GoShortWhenAboveBands AndAlso rsiVal > 70 Then
                            Log($"✅ RSI overbought! RSI={rsiVal:F1} > 70")
                            side = OrderSide.Sell
                        Else
                            Log($"Bar checked — RSI={rsiVal:F1} | no signal ({remStr})")
                        End If

                    Case StrategyConditionType.EMACrossAbove, StrategyConditionType.EMACrossBelow
                        Dim fastEma = TechnicalIndicators.EMA(closes, _strategy.IndicatorPeriod)
                        Dim slowEma = TechnicalIndicators.EMA(closes, _strategy.SecondaryPeriod)
                        Dim fastNow = TechnicalIndicators.LastValid(fastEma)
                        Dim fastPrev = TechnicalIndicators.PreviousValid(fastEma)
                        Dim slowNow = TechnicalIndicators.LastValid(slowEma)
                        Dim slowPrev = TechnicalIndicators.PreviousValid(slowEma)

                        Dim crossedAbove = fastPrev < slowPrev AndAlso fastNow > slowNow
                        Dim crossedBelow = fastPrev > slowPrev AndAlso fastNow < slowNow

                        If _strategy.GoLongWhenBelowBands AndAlso crossedAbove Then
                            Log($"✅ EMA{_strategy.IndicatorPeriod} crossed above EMA{_strategy.SecondaryPeriod}!")
                            side = OrderSide.Buy
                        ElseIf _strategy.GoShortWhenAboveBands AndAlso crossedBelow Then
                            Log($"✅ EMA{_strategy.IndicatorPeriod} crossed below EMA{_strategy.SecondaryPeriod}!")
                            side = OrderSide.Sell
                        Else
                            Log($"Bar checked — EMA{_strategy.IndicatorPeriod}={fastNow:F2} EMA{_strategy.SecondaryPeriod}={slowNow:F2} | no signal ({remStr})")
                        End If

                    Case StrategyConditionType.EmaRsiWeightedScore
                        ' Six-signal weighted scoring — mirrors TrendAnalysisService on Test Trade tab.
                        ' Periods are hardcoded (EMA21/EMA50/RSI14) for this combined strategy.
                        Dim ema21Vals = TechnicalIndicators.EMA(closes, 21)
                        Dim ema50Vals = TechnicalIndicators.EMA(closes, 50)
                        Dim rsi14Vals = TechnicalIndicators.RSI(closes, 14)

                        Dim ema21Now = TechnicalIndicators.LastValid(ema21Vals)
                        Dim ema21Prev = TechnicalIndicators.PreviousValid(ema21Vals)
                        Dim ema50Now = TechnicalIndicators.LastValid(ema50Vals)
                        Dim rsiVal = TechnicalIndicators.LastValid(rsi14Vals)
                        Dim lastClose = CDec(lastBar.Close)
                        _currentEma21 = CDec(ema21Now)    ' snapshot for pullback scale-in guard

                        ' Accumulate bull score (max 100)
                        Dim bullScore As Double = 0

                        ' 1. EMA21 vs EMA50 crossover — 25%
                        If ema21Now > ema50Now Then bullScore += 25

                        ' 2. Price vs EMA21 — 20%
                        If lastClose > CDec(ema21Now) Then bullScore += 20

                        ' 3. Price vs EMA50 — 15%
                        If lastClose > CDec(ema50Now) Then bullScore += 15

                        ' 4. RSI trending zone — 20 pts
                        ' Awards +20 when RSI is in the 50–70 range: the market is trending
                        ' bullishly but not yet overbought — the ideal continuation zone.
                        ' Zero contribution outside that window (oversold, overbought, or neutral RSI).
                        Dim rsiScore As Double
                        If rsiVal >= 50 AndAlso rsiVal < 70 Then
                            rsiScore = 20
                        Else
                            rsiScore = 0
                        End If
                        bullScore += rsiScore
                        bullScore = Math.Max(0.0, Math.Min(100.0, bullScore))

                        ' 5. EMA21 momentum — 10%
                        If ema21Now > ema21Prev Then bullScore += 10

                        ' 6. Recent 3 candles — 10%  (majority green = bullish)
                        Dim lastThree = bars.Skip(bars.Count - 3).ToList()
                        Dim bullCandles = lastThree.Where(Function(b) b.Close >= b.Open).Count()
                        If bullCandles >= 2 Then bullScore += 10

                        Dim upPct As Double = bullScore
                        Dim downPct As Double = 100 - bullScore
                        rawUpPct = CInt(upPct)
                        rawDownPct = CInt(downPct)
                        Dim minPct As Integer = _strategy.MinConfidencePct  ' user-set threshold (default 85)

                        ' ── ADX trend-strength gate (TICKET-019) ────────────────────────
                        ' Threshold lowered to 19.9 for 5-min intraday bars (ADX >= 20 passes).
                        ' A signal is only acted on when the market is in a trending phase.
                        ' ADX < 20 = ranging/consolidating market — suppress entry to avoid
                        ' false signals in sideways price action.
                        Dim dmiResult = TechnicalIndicators.DMI(highs, lows, closes)
                        Dim adxNow = TechnicalIndicators.LastValid(dmiResult.ADX)
                        Dim adxGatePassed = (adxNow >= 19.9F)

                        Dim atrVals = TechnicalIndicators.ATR(highs, lows, closes, 14)
                        _currentAtrValue = CDec(TechnicalIndicators.LastValid(atrVals))

                        ' Raise ConfidenceUpdated AFTER the ADX gate is known so the UI can
                        ' display the suppressed state (amber ⊘) instead of a misleading green arrow.
                        RaiseEvent ConfidenceUpdated(Me, New ConfidenceUpdatedEventArgs(CInt(upPct), CInt(downPct), adxGatePassed, CSng(adxNow), lastClose) With {
                            .Ema21 = CDec(ema21Now),
                            .Ema50 = CDec(ema50Now),
                            .Rsi14 = CSng(rsiVal),
                            .Ema21Rising = (ema21Now > ema21Prev),
                            .RecentCandlesBullish = (bullCandles >= 2),
                            .PlusDI = TechnicalIndicators.LastValid(dmiResult.PlusDI),
                            .MinusDI = TechnicalIndicators.LastValid(dmiResult.MinusDI),
                            .TotalConditions = 6
                        })

                        If Not adxGatePassed Then
                            Log($"Bar checked — ADX={adxNow:F1} < 20 (ranging market) — signal suppressed | EMA/RSI: UP={upPct:F0}% DOWN={downPct:F0}% | ATR={_currentAtrValue:F4} | {remStr}")
                        ElseIf upPct >= minPct Then
                            _pendingConfidencePct = CInt(upPct)
                            Log($"✅ EMA/RSI weighted: UP={upPct:F0}% ≥ {minPct}% — LONG signal! Close={lastClose:F2} EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1} ADX={adxNow:F1}")
                            side = OrderSide.Buy
                        ElseIf downPct >= minPct Then
                            _pendingConfidencePct = CInt(downPct)
                            Log($"✅ EMA/RSI weighted: DOWN={downPct:F0}% ≥ {minPct}% — SHORT signal! Close={lastClose:F2} EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1} ADX={adxNow:F1}")
                            side = OrderSide.Sell
                        Else
                            Log($"Bar checked — EMA/RSI: UP={upPct:F0}% DOWN={downPct:F0}% | no signal (need ≥{minPct}%) | EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1} ADX={adxNow:F1} | {remStr}")
                        End If

                    Case StrategyConditionType.MultiConfluence
                        Dim mcResult = MultiConfluenceStrategy.Evaluate(highs, lows, closes)
                        _currentAtrValue = mcResult.AtrValue
                        _mcCloudSlPrice = Nothing   ' reset; will be set only when a signal fires

                        ' Raise live confidence telemetry every bar regardless of signal state
                        Dim mcArgs As New ConfidenceUpdatedEventArgs(mcResult.BullScore, mcResult.BearScore, adxValue:=mcResult.AdxValue, lastClose:=CDec(lastBar.Close)) With {
                            .Cloud1 = mcResult.Cloud1,
                            .Cloud2 = mcResult.Cloud2,
                            .Tenkan = mcResult.Tenkan,
                            .Kijun = mcResult.Kijun,
                            .Ema21 = mcResult.Ema21,
                            .Ema50 = mcResult.Ema50,
                            .PlusDI = mcResult.PlusDI,
                            .MinusDI = mcResult.MinusDI,
                            .MacdHist = mcResult.MacdHist,
                            .MacdHistPrev = mcResult.MacdHistPrev,
                            .StochRsiK = mcResult.StochRsiK,
                            .LongCount = mcResult.LongCount,
                            .ShortCount = mcResult.ShortCount,
                            .TotalConditions = 7
                        }
                        RaiseEvent ConfidenceUpdated(Me, mcArgs)

                        If mcResult.Side.HasValue Then
                            Dim mcSide = mcResult.Side.Value
                            _pendingConfidencePct = 100   ' all 7 conditions met = full confluence
                            _mcCloudSlPrice = mcResult.CloudEdgeSl
                            If mcSide = OrderSide.Buy Then
                                Log($"✅ Multi-Confluence LONG — all 7 conditions met! {mcResult.StatusLine} | {remStr}")
                            Else
                                Log($"✅ Multi-Confluence SHORT — all 7 conditions met! {mcResult.StatusLine} | {remStr}")
                            End If
                            side = mcSide
                        Else
                            Log($"Bar checked — Multi-Confluence: {mcResult.StatusLine} | {remStr}")
                        End If

                    Case StrategyConditionType.LultDivergence
                        _lultTriggerExtreme = Nothing   ' reset each tick; set only when signal fires
                        Dim lultOpens = bars.Select(Function(b) b.Open).ToList()
                        Dim lultAtrVals = TechnicalIndicators.ATR(highs, lows, closes, 14)
                        _currentAtrValue = CDec(TechnicalIndicators.LastValid(lultAtrVals))
                        Dim lultResult = LultDivergenceStrategy.Evaluate(highs, lows, closes, lultOpens)
                        ' Raise live confidence telemetry every bar regardless of signal state
                        RaiseEvent ConfidenceUpdated(Me, New ConfidenceUpdatedEventArgs(lultResult.BullScore, lultResult.BearScore, lastClose:=CDec(lastBar.Close)))
                        If Not lultResult.IsInTradingWindow Then
                            Log($"Bar checked — LULT (OUT of EST window): {lultResult.StatusLine} | {remStr}")
                        ElseIf lultResult.Side.HasValue Then
                            Dim lultSide = lultResult.Side.Value
                            _pendingConfidencePct = 100
                            ' SL absolute price: trigger wave extreme ± max(3 NQ ticks = 0.75 pts, 25 % of ATR)
                            Dim tickBuf = If(_currentAtrValue > 0D,
                                         Math.Max(_currentAtrValue * 0.25D, 0.75D), 0.75D)
                            _lultTriggerExtreme = If(lultSide = OrderSide.Buy,
                                                  lultResult.TriggerWaveExtreme - tickBuf,
                                                  lultResult.TriggerWaveExtreme + tickBuf)
                            Dim partialMsg = If(lultResult.PartialTpSwingLevel.HasValue,
                                            $" | Partial TP swing={lultResult.PartialTpSwingLevel.Value:F4}",
                                            String.Empty)
                            If lultSide = OrderSide.Buy Then
                                Log($"✅ LULT LONG — 6/6 steps confirmed! {lultResult.StatusLine} | " &
                                $"AnchorWT1={lultResult.AnchorWt1:F1} TriggerWT1={lultResult.TriggerWt1:F1} " &
                                $"TriggerLow={lultResult.TriggerWaveExtreme:F4} SL≈{_lultTriggerExtreme:F4}{partialMsg} | {remStr}")
                            Else
                                Log($"✅ LULT SHORT — 6/6 steps confirmed! {lultResult.StatusLine} | " &
                                $"AnchorWT1={lultResult.AnchorWt1:F1} TriggerWT1={lultResult.TriggerWt1:F1} " &
                                $"TriggerHigh={lultResult.TriggerWaveExtreme:F4} SL≈{_lultTriggerExtreme:F4}{partialMsg} | {remStr}")
                            End If
                            side = lultSide
                        Else
                            Log($"Bar checked — LULT: {lultResult.StatusLine} | {remStr}")
                        End If

                    Case StrategyConditionType.BbSqueezeScalper
                        ' ── BB Squeeze Scalper ────────────────────────────────────────────
                        ' Dual-mode: Squeeze Breakout (momentum) or Band Bounce (mean-reversion).
                        ' Indicators: BB(12,2.0), BBW, %B, RSI(7), EMA(5), ATR(10).
                        ' Mode A fires when bands are squeezing; Mode B when bands are wide.

                        Const BbPeriod As Integer = 12
                        Const BbMult As Double = 2.0
                        Const BbwSmaPeriod As Integer = 20
                        Const SqueezeConsecutiveBars As Integer = 3

                        Dim bbBands = TechnicalIndicators.BollingerBands(closes, BbPeriod, BbMult)
                        Dim bbwArr = TechnicalIndicators.BollingerBandWidth(closes, BbPeriod, BbMult)
                        Dim pctBArr = TechnicalIndicators.BollingerPercentB(closes, BbPeriod, BbMult)
                        Dim rsi7Arr = TechnicalIndicators.RSI(closes, 7)
                        Dim ema5Arr = TechnicalIndicators.EMA(closes, 5)
                        Dim atr10Arr = TechnicalIndicators.ATR(highs, lows, closes, 10)
                        Dim bbwSmaArr = TechnicalIndicators.SMA(
                        bbwArr.Select(Function(v) If(Single.IsNaN(v), 0D, CDec(v))).ToList(),
                        BbwSmaPeriod)

                        _currentAtrValue = CDec(TechnicalIndicators.LastValid(atr10Arr))

                        Dim bbUpper = CDec(TechnicalIndicators.LastValid(bbBands.Upper))
                        Dim bbLower = CDec(TechnicalIndicators.LastValid(bbBands.Lower))
                        Dim bbMiddle = CDec(TechnicalIndicators.LastValid(bbBands.Middle))
                        Dim pctBNow = CDbl(TechnicalIndicators.LastValid(pctBArr))
                        Dim rsi7Now = CDbl(TechnicalIndicators.LastValid(rsi7Arr))
                        Dim ema5Now = CDbl(TechnicalIndicators.LastValid(ema5Arr))
                        Dim ema5Prev = CDbl(TechnicalIndicators.PreviousValid(ema5Arr))
                        Dim bbwNow = CDbl(TechnicalIndicators.LastValid(bbwArr))
                        Dim bbwSma = CDbl(TechnicalIndicators.LastValid(bbwSmaArr))
                        Dim bbLastClose = CDec(lastBar.Close)

                        ' Count consecutive bars where BBW < SMA(BBW) — squeeze detection
                        Dim squeezeCount As Integer = 0
                        For si = bars.Count - 1 To Math.Max(0, bars.Count - 10) Step -1
                            Dim bwVal = CDbl(bbwArr(si))
                            Dim smaIdx = Math.Min(si, bbwSmaArr.Length - 1)
                            Dim smaVal = CDbl(bbwSmaArr(smaIdx))
                            If Not Double.IsNaN(bwVal) AndAlso Not Double.IsNaN(smaVal) AndAlso
                           smaVal > 0 AndAlso bwVal < smaVal Then
                                squeezeCount += 1
                            Else
                                Exit For
                            End If
                        Next
                        Dim squeezeActive = squeezeCount >= SqueezeConsecutiveBars

                        Dim ema5Rising = ema5Now > ema5Prev

                        ' Bar range metrics for wick filter (Mode B)
                        Dim bbBarRange = CDbl(lastBar.High - lastBar.Low)
                        Dim lowerWick = CDbl(Math.Min(lastBar.Open, lastBar.Close) - lastBar.Low)
                        Dim upperWick = CDbl(lastBar.High - Math.Max(lastBar.Open, lastBar.Close))
                        Dim lowerWickPct = If(bbBarRange > 0, lowerWick / bbBarRange, 0.0)
                        Dim upperWickPct = If(bbBarRange > 0, upperWick / bbBarRange, 0.0)

                        ' ══════════════════════════════════════════════════════════════════
                        ' DIAGNOSTIC SNAPSHOT — built every tick, logged after signal decision.
                        ' Captures indicator state, market micro-structure, and bar noise
                        ' regardless of whether a signal fires or is suppressed.
                        ' ══════════════════════════════════════════════════════════════════
                        _pendingDiagEntry = Nothing
                        Dim diagQuote As Quote = Nothing
                        If _marketData IsNot Nothing Then
                            Try
                                diagQuote = Await _marketData.GetCurrentQuoteAsync(_strategy.ContractId)
                            Catch
                                ' Quote fetch is best-effort — non-fatal if the market hub is not streaming
                            End Try
                        End If

                        ' Previous 3 bars noise floor (the "noise" price must overcome to be profitable)
                        Dim diagPrev3 As New List(Of DiagBarSnapshot)
                        For pi As Integer = Math.Max(0, bars.Count - 4) To bars.Count - 2
                            Dim pb = bars(pi)
                            diagPrev3.Add(New DiagBarSnapshot With {
                            .Timestamp = pb.Timestamp.ToString("o"),
                            .Open = pb.Open,
                            .High = pb.High,
                            .Low = pb.Low,
                            .Close = pb.Close,
                            .Range = pb.High - pb.Low,
                            .Body = Math.Abs(pb.Close - pb.Open),
                            .IsBullish = pb.Close >= pb.Open
                        })
                        Next
                        Dim diagAvg3Range As Decimal = If(diagPrev3.Count > 0,
                        diagPrev3.Average(Function(b) b.Range), 0D)

                        Dim diagSpread As Decimal = 0D
                        Dim diagSpreadPct As Decimal = 0D
                        If diagQuote IsNot Nothing AndAlso diagQuote.AskPrice > 0D Then
                            diagSpread = diagQuote.Spread
                            Dim mid = diagQuote.MidPrice
                            If mid > 0D Then diagSpreadPct = Math.Round(diagSpread / mid * 100D, 5)
                        End If

                        Dim diagMid As Decimal = If(diagQuote IsNot Nothing AndAlso diagQuote.MidPrice > 0D,
                                                diagQuote.MidPrice, bbLastClose)
                        Dim diagBid As Decimal = If(diagQuote IsNot Nothing, diagQuote.BidPrice, 0D)
                        Dim diagAsk As Decimal = If(diagQuote IsNot Nothing, diagQuote.AskPrice, 0D)

                        _pendingDiagEntry = New DiagnosticLogEntry With {
                        .TradeId = Guid.NewGuid().ToString("N"),
                        .EventType = "NO_SIGNAL",
                        .Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                        .Symbol = _strategy.ContractId,
                        .Strategy = _strategy.Name,
                        .Action = "NONE",
                        .MetricsAtEntry = New DiagMetricsAtEntry With {
                            .Rsi7 = CDec(rsi7Now),
                            .BbUpper = bbUpper,
                            .BbMiddle = bbMiddle,
                            .BbLower = bbLower,
                            .BbPercentB = CDec(pctBNow),
                            .BbWidth = CDec(bbwNow),
                            .BbWidthSma20 = CDec(bbwSma),
                            .BbSqueezeCount = squeezeCount,
                            .BbSqueezeActive = squeezeActive,
                            .Ema5Now = CDec(ema5Now),
                            .Ema5Prev = CDec(ema5Prev),
                            .Ema5Rising = ema5Rising,
                            .Atr10 = _currentAtrValue,
                            .PriceEntry = bbLastClose,
                            .SpreadBps = If(diagMid > 0D, Math.Round(diagSpread / diagMid * 10000D, 1), 0D),
                            .Bid = diagBid,
                            .Ask = diagAsk,
                            .BarTimestamp = lastBar.Timestamp.ToString("o"),
                            .BarOpen = lastBar.Open,
                            .BarHigh = lastBar.High,
                            .BarLow = lastBar.Low,
                            .BarClose = lastBar.Close,
                            .BarRange = CDec(bbBarRange),
                            .BarLowerWickPct = CDec(lowerWickPct),
                            .BarUpperWickPct = CDec(upperWickPct)
                        },
                        .NoiseCheck = New DiagNoiseCheck With {
                            .Prev3BarAvgRange = diagAvg3Range,
                            .PrevBars = diagPrev3
                        }
                    }
                        ' ── end diagnostic setup ───────────────────────────────────────────

                        If squeezeActive Then
                            ' ── Mode A: Squeeze Breakout (momentum entry in breakout direction) ──
                            If bbLastClose > bbUpper AndAlso ema5Rising AndAlso rsi7Now > 50 Then
                                Log($"✅ BB SQUEEZE BREAKOUT LONG! Close={bbLastClose:F4} > Upper={bbUpper:F4} " &
                                $"| BBW={bbwNow:F3} < SMA={bbwSma:F3} ({squeezeCount} bars) " &
                                $"| EMA5↑ RSI7={rsi7Now:F1}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode A)"
                                    _pendingDiagEntry.Why = $"Mode A LONG ✓ | squeeze={squeezeCount}bars | Close={bbLastClose:F4}>Upper={bbUpper:F4} | EMA5↑({ema5Now:F4}>{ema5Prev:F4}) | RSI7={rsi7Now:F1}>50"
                                End If
                                side = OrderSide.Buy
                            ElseIf bbLastClose < bbLower AndAlso Not ema5Rising AndAlso rsi7Now < 50 Then
                                Log($"✅ BB SQUEEZE BREAKOUT SHORT! Close={bbLastClose:F4} < Lower={bbLower:F4} " &
                                $"| BBW={bbwNow:F3} < SMA={bbwSma:F3} ({squeezeCount} bars) " &
                                $"| EMA5↓ RSI7={rsi7Now:F1}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode A)"
                                    _pendingDiagEntry.Why = $"Mode A SHORT ✓ | squeeze={squeezeCount}bars | Close={bbLastClose:F4}<Lower={bbLower:F4} | EMA5↓({ema5Now:F4}<{ema5Prev:F4}) | RSI7={rsi7Now:F1}<50"
                                End If
                                side = OrderSide.Sell
                            Else
                                Log($"BB Squeeze ({squeezeCount} bars) — waiting for breakout | " &
                                $"Close={bbLastClose:F4} BB=[{bbLower:F4}—{bbUpper:F4}] " &
                                $"RSI7={rsi7Now:F1} EMA5={ema5Now:F4} | {remStr}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode A)"
                                    Dim aParts = New List(Of String) From {$"squeeze={squeezeCount}bars"}
                                    If bbLastClose > bbUpper Then
                                        aParts.Add($"Close={bbLastClose:F4}>Upper={bbUpper:F4}✓")
                                    ElseIf bbLastClose < bbLower Then
                                        aParts.Add($"Close={bbLastClose:F4}<Lower={bbLower:F4}✓")
                                    Else
                                        aParts.Add($"Close={bbLastClose:F4} between bands-no breakout✗")
                                    End If
                                    If ema5Rising Then aParts.Add("EMA5rising✓") Else aParts.Add("EMA5flat/falling✗")
                                    If rsi7Now > 50 Then aParts.Add($"RSI7={rsi7Now:F1}>50✓") Else aParts.Add($"RSI7={rsi7Now:F1}<=50✗")
                                    _pendingDiagEntry.Why = "Mode A no-signal | " & String.Join(" | ", aParts)
                                End If
                            End If
                        Else
                            ' ── Mode B: Band Bounce (mean-reversion fade at extremes) ──────────
                            If pctBNow < 0.0 AndAlso rsi7Now < 25 AndAlso lowerWickPct >= 0.6 Then
                                Log($"✅ BB BAND BOUNCE LONG! %B={pctBNow:F3} | RSI7={rsi7Now:F1} < 25 " &
                                $"| Lower wick={lowerWickPct:P0} | BBW={bbwNow:F3} ≥ SMA={bbwSma:F3}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode B)"
                                    _pendingDiagEntry.Why = $"Mode B LONG ✓ | %B={pctBNow:F3}<0 | RSI7={rsi7Now:F1}<25 | LowerWick={lowerWickPct:P0}≥60% | BBW={bbwNow:F3}≥SMA={bbwSma:F3}"
                                End If
                                side = OrderSide.Buy
                            ElseIf pctBNow > 1.0 AndAlso rsi7Now > 75 AndAlso upperWickPct >= 0.6 Then
                                Log($"✅ BB BAND BOUNCE SHORT! %B={pctBNow:F3} | RSI7={rsi7Now:F1} > 75 " &
                                $"| Upper wick={upperWickPct:P0} | BBW={bbwNow:F3} ≥ SMA={bbwSma:F3}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode B)"
                                    _pendingDiagEntry.Why = $"Mode B SHORT ✓ | %B={pctBNow:F3}>1 | RSI7={rsi7Now:F1}>75 | UpperWick={upperWickPct:P0}≥60% | BBW={bbwNow:F3}≥SMA={bbwSma:F3}"
                                End If
                                side = OrderSide.Sell
                            Else
                                Log($"BB no signal — %B={pctBNow:F3} RSI7={rsi7Now:F1} | " &
                                $"BB=[{bbLower:F4}—{bbMiddle:F4}—{bbUpper:F4}] " &
                                $"BBW={bbwNow:F3} SMA={bbwSma:F3} | {remStr}")
                                If _pendingDiagEntry IsNot Nothing Then
                                    _pendingDiagEntry.Strategy = $"{_strategy.Name} (Mode B)"
                                    Dim bParts = New List(Of String)
                                    If pctBNow < 0.0 Then
                                        bParts.Add($"pctB={pctBNow:F3}<0✓")
                                    ElseIf pctBNow > 1.0 Then
                                        bParts.Add($"pctB={pctBNow:F3}>1✓")
                                    Else
                                        bParts.Add($"pctB={pctBNow:F3} in-band✗")
                                    End If
                                    If rsi7Now < 25 Then
                                        bParts.Add($"RSI7={rsi7Now:F1}<25✓")
                                    ElseIf rsi7Now > 75 Then
                                        bParts.Add($"RSI7={rsi7Now:F1}>75✓")
                                    Else
                                        bParts.Add($"RSI7={rsi7Now:F1} neutral✗")
                                    End If
                                    If lowerWickPct >= 0.6 Then
                                        bParts.Add($"LowerWick={lowerWickPct:P1}>=60pct✓")
                                    ElseIf upperWickPct >= 0.6 Then
                                        bParts.Add($"UpperWick={upperWickPct:P1}>=60pct✓")
                                    Else
                                        bParts.Add($"lo-wick={lowerWickPct:P1} up-wick={upperWickPct:P1} neither>=60pct✗")
                                    End If
                                    _pendingDiagEntry.Why = "Mode B no-signal | " & String.Join(" | ", bParts)
                                End If
                            End If
                        End If

                        ' Emit %B as confidence signal (scaled to 0–100; 50 = middle band)
                        Dim bbPctScaled = CInt(Math.Max(0, Math.Min(100, pctBNow * 100.0)))
                        RaiseEvent ConfidenceUpdated(Me, New ConfidenceUpdatedEventArgs(
                        If(pctBNow >= 0.5, bbPctScaled, 100 - bbPctScaled),
                        If(pctBNow < 0.5, bbPctScaled, 100 - bbPctScaled),
                        adxValue:=CSng(bbwNow),
                        lastClose:=bbLastClose))

                    Case Else
                        Log($"Condition '{_strategy.Condition}' not yet implemented")
                End Select

                If side.HasValue Then
                    If _currentTrendSide Is Nothing Then
                        ' First signal of this session — establish the trend direction.
                        _currentTrendSide = side
                        _reversalCandidateSide = Nothing
                        _reversalConfirmCount = 0
                    ElseIf side.Value = _currentTrendSide.Value Then
                        ' Continuing the same direction — cancel any pending reversal candidate.
                        If _reversalCandidateSide.HasValue Then
                            Log($"↩  Reversal candidate cleared — {side.Value} signal confirms existing trend")
                        End If
                        _reversalCandidateSide = Nothing
                        _reversalConfirmCount = 0
                    Else
                        ' Opposite signal — advance confirmation counter on new bars only.
                        If isNewBar Then
                            If Not _reversalCandidateSide.HasValue OrElse _reversalCandidateSide.Value <> side.Value Then
                                _reversalCandidateSide = side
                                _reversalConfirmCount = 1
                            Else
                                _reversalConfirmCount += 1
                            End If
                            Log($"↔  Reversal candidate: was {_currentTrendSide.Value}, now {side.Value} " &
                            $"({_reversalConfirmCount}/{ReversalConfirmBars} confirmations)")
                        End If

                        If _reversalConfirmCount >= ReversalConfirmBars Then
                            Await DoReversalFlushAsync(side.Value, CDec(lastBar.Close), ct)
                        End If
                    End If
                End If

            End If ' Not barIsStale

            ' ── API-authoritative position reconciliation
            ' Queries the broker for ANY open position on this contract every tick (after
            ' the 60-s propagation guard).  Broker state is always authoritative.
            ' If the API reports no positions, ALL locally-tracked UI rows are force-closed
            ' regardless of how many are shown as "In Progress" — this correctly handles
            ' the multi-position (scale-in) scenario where the engine tracks only one
            ' _openPositionId but multiple rows may be open in the UI.
            ' This reconciliation runs BEFORE any confidence-driven action below.
            If _positionOpen Then
                Dim secondsSinceEntry = (DateTimeOffset.UtcNow - _positionOpenedAt).TotalSeconds
                If secondsSinceEntry < 60 Then
                    Log($"⏳ Sync skipped ({CInt(secondsSinceEntry)}s since entry — waiting 60 s for portfolio to reflect new position)")
                Else
                    ' Pass Nothing so the call finds ANY live position for this contract,
                    ' not just _openPositionId.  This detects SL/TP closures on scale-in
                    ' positions even when the initial position ID has changed.
                    Dim snapshot = Await _orderService.GetLivePositionSnapshotAsync(
                        _strategy.AccountId, _strategy.ContractId, Nothing, ct)
                    If snapshot IsNot Nothing Then
                        ' At least one position confirmed open at broker.
                        If Not _openPositionId.HasValue Then
                            _openPositionId = snapshot.PositionId
                            Log($"🔗 eToro positionId resolved: {snapshot.PositionId}")
                        End If
                        ' Use the broker's reported P&L directly; fall back to a bar-close
                        ' estimate when the field is zero (e.g. during market-closed hours).
                        Dim livePnl = If(snapshot.UnrealizedPnlUsd <> 0D, snapshot.UnrealizedPnlUsd,
                            If(snapshot.Units > 0D,
                                Math.Round((CDec(lastBar.Close) - snapshot.OpenRate) * snapshot.Units *
                                           If(snapshot.IsBuy, 1D, -1D), 2), 0D))
                        _lastApiPnl = livePnl
                        RaiseEvent PositionSynced(Me, New PositionSyncedEventArgs(
                            snapshot.PositionId, livePnl, snapshot.OpenedAtUtc))

                        ' ── DIAGNOSTIC: update MFE / MAE every tick ─────────────────────────
                        If _openTradeDiagEntry IsNot Nothing AndAlso
                           _openTradeDiagEntry.Outcome IsNot Nothing AndAlso
                           _lastEntryPrice > 0D Then
                            Dim monPct = If(_lastEntrySide = OrderSide.Buy,
                                (CDec(lastBar.Close) - _lastEntryPrice) / _lastEntryPrice * 100D,
                                (_lastEntryPrice - CDec(lastBar.Close)) / _lastEntryPrice * 100D)
                            If monPct > _openTradeDiagEntry.Outcome.MaxFavorableExcursion Then
                                _openTradeDiagEntry.Outcome.MaxFavorableExcursion = monPct
                                _openTradeDiagEntry.Outcome.MfePrice = CDec(lastBar.Close)
                            End If
                            If monPct < _openTradeDiagEntry.Outcome.MaxAdverseExcursion Then
                                _openTradeDiagEntry.Outcome.MaxAdverseExcursion = monPct
                                _openTradeDiagEntry.Outcome.MaePrice = CDec(lastBar.Close)
                            End If
                        End If
                    Else
                        ' No positions at all for this contract — closed by SL/TP, manual
                        ' action, or broker risk controls.  Force-close every in-progress UI row.
                        Dim closedCount = Math.Max(1, _openTradeCount)
                        Log($"⚠️  API reconciliation: no open positions for {_strategy.ContractId} — " &
                            $"force-closing {closedCount} UI trade row(s) (SL/TP/external close). " &
                            $"Final P&L={If(_lastApiPnl >= 0, "+", "")}${_lastApiPnl:F2}. Ready for next signal.")
                        WriteDiagPostMortem("SL/TP", _lastApiPnl)
                        _positionOpen = False
                        _openPositionId = Nothing
                        Dim closePnl = _lastApiPnl
                        For i As Integer = 1 To closedCount
                            RaiseEvent TradeClosed(Me, New TradeClosedEventArgs("SL/TP", closePnl))
                            closePnl = 0D   ' P&L only available for the first (most-recently synced) close
                        Next
                        _openTradeCount = 0
                        _lastApiPnl = 0D
                        ResetTrailState()
                        _lastPositionClosedAt = DateTimeOffset.UtcNow  ' start re-entry cooldown
                    End If
                End If
            Else
                ' ── Orphan-position detection ──────────────────────────────────────────
                ' When the engine has no known open position, query the broker each tick to
                ' catch positions opened externally (manually on eToro or by a concurrent
                ' session) that the startup check may have missed (async race or API hiccup).
                ' Guard: skip during the re-entry cooldown window — a position we just closed
                ' can still appear briefly in the portfolio API before the broker reconciles.
                Dim cooldownElapsed = (DateTimeOffset.UtcNow - _lastPositionClosedAt).TotalSeconds
                If cooldownElapsed >= ReEntryCooldownSeconds Then
                    Try
                        Dim orphan = Await _orderService.GetLivePositionSnapshotAsync(
                            _strategy.AccountId, _strategy.ContractId, Nothing, ct)
                        If orphan IsNot Nothing Then
                            _positionOpen = True
                            _openPositionId = orphan.PositionId
                            _positionOpenedAt = DateTimeOffset.UtcNow.AddSeconds(-61) ' skip propagation guard
                            If orphan.OpenRate > 0D Then
                                _lastEntryPrice = orphan.OpenRate
                                _lastEntrySide = If(orphan.IsBuy, OrderSide.Buy, OrderSide.Sell)
                            End If
                            _currentTrendSide = If(orphan.IsBuy, OrderSide.Buy, OrderSide.Sell)
                            _lastFinalAmount = orphan.Amount
                            _lastLeverage = orphan.Leverage
                            _openTradeCount = Math.Max(1, orphan.PositionCount)
                            _scaleInTradeCount = Math.Min(MaxScaleInTrades,
                                                          Math.Max(0, orphan.PositionCount - 1))
                            _totalDollarPerPoint = If(orphan.Units > 0D, orphan.Units, 0D)
                            _bracketInitPending = True ' Turtle bracket deferred until ATR is ready
                            Dim orphanSide = If(orphan.IsBuy, OrderSide.Buy, OrderSide.Sell)
                            RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(
                                orphanSide, _strategy.ContractId, 100,
                                orphan.OpenedAtUtc, Nothing, orphan.PositionId,
                                orphan.OpenedAtUtc, orphan.Amount, orphan.Leverage, orphan.OpenRate))
                            Dim orphanCapStr = If(_scaleInTradeCount >= MaxScaleInTrades,
                                $"scale-in cap REACHED ({_scaleInTradeCount}/{MaxScaleInTrades})",
                                $"scale-in {_scaleInTradeCount}/{MaxScaleInTrades} used")
                            Log($"🔍 Orphan position attached — positionId={orphan.PositionId} " &
                                $"entry={orphan.OpenRate:F4} side={orphanSide} " &
                                $"({orphan.PositionCount} position(s), {orphanCapStr}) — Turtle bracket pending ATR.")
                        End If
                    Catch ex As Exception
                        ' Non-fatal — orphan check retries on next tick.
                        Log($"⚠️  Orphan position check failed: {ex.Message}")
                    End Try
                End If
            End If

            ' ── Deferred Turtle bracket init for startup / orphan-detected positions ─────
            ' _turtleBracket cannot be initialised during Start() or the orphan-detection
            ' path because ATR = 0 before the first bar check.  Once ATR is available
            ' (computed above in the strategy-condition block) we create the bracket here
            ' so all subsequent ticks get full SL/TP stepped trail management.
            '
            ' Guard: if the position's current P&L is already beyond the configured SL
            ' threshold (e.g. a manually-opened position that is deep in loss), initialising
            ' the bracket would cause ApplySteppedTrailAsync to breach the SL on this very
            ' tick, flatten the position, and raise TradeClosed.  In that case skip the
            ' bracket and let the broker's native SL/TP manage the position; the engine
            ' will still track P&L via broker sync and surface it on the tile.
            If _positionOpen AndAlso _bracketInitPending AndAlso
               _turtleBracket Is Nothing AndAlso
               _lastEntryPrice > 0D AndAlso _currentAtrValue > 0D Then
                Dim dppDeferred As Decimal = 0D
                If _lastFinalAmount > 0D AndAlso _lastLeverage > 0 AndAlso _lastEntryPrice > 0D Then
                    dppDeferred = Math.Round((_lastFinalAmount * CDec(_lastLeverage)) / _lastEntryPrice, 4)
                End If
                Dim sideStrDeferred = If(_lastEntrySide = OrderSide.Buy, "BUY", "SELL")

                ' Estimate current P&L: prefer the broker-reported value from the last
                ' sync tick; fall back to a price-based estimate when not yet available.
                Dim slThreshold = -Math.Abs(_strategy.InitialSlAmount)
                Dim estimatedPnl As Decimal
                If _lastApiPnl <> 0D Then
                    estimatedPnl = _lastApiPnl
                ElseIf dppDeferred > 0D Then
                    Dim dirMult = If(_lastEntrySide = OrderSide.Buy, 1D, -1D)
                    estimatedPnl = Math.Round(dirMult * (CDec(lastBar.Close) - _lastEntryPrice) * dppDeferred, 2)
                Else
                    estimatedPnl = 0D
                End If

                If estimatedPnl <= slThreshold Then
                    ' Position already in a loss that exceeds the engine SL — skip bracket.
                    ' ApplySteppedTrailAsync would fire the SL immediately if the bracket were
                    ' created, closing a position the user wants the engine to monitor.
                    _bracketInitPending = False
                    Log($"⚠️  Attached position P&L≈${estimatedPnl:F2} already exceeds SL threshold " &
                        $"${slThreshold:F2} — turtle bracket not applied; monitoring via broker sync only.")
                Else
                    _turtleBracket = Core.Trading.TurtleBracketManager.Initialise(
                        entryPrice:=_lastEntryPrice,
                        side:=sideStrDeferred,
                        dollarPerPoint:=dppDeferred,
                        atrPrice:=_currentAtrValue,
                        initialSlDollars:=_strategy.InitialSlAmount,
                        initialTpDollars:=_strategy.InitialTpAmount)
                    _bracketInitPending = False
                    Log($"🐢 Turtle bracket attached to detected position — " &
                        $"entry={_lastEntryPrice:F4} side={sideStrDeferred} " &
                        $"ATR={_currentAtrValue:F4} dollarPerPoint={dppDeferred:F4}" & vbLf &
                        $"   Bracket#0 → SL={_turtleBracket.SlPrice:F4} (−${_strategy.InitialSlAmount:F2}) " &
                        $"TP={_turtleBracket.TpPrice:F4} (+${_strategy.InitialTpAmount:F2})")
                    Await PushTrailToAllPositionsAsync(ct)
                    Log($"🐢 Initial bracket SL/TP pushed to broker — SL={_turtleBracket.SlPrice:F4} TP={_turtleBracket.TpPrice:F4}")
                    ' isAdvance:=False — bracket reattached to an existing position on engine restart;
                    ' no TP level was hit so the "Turtle Applied" UI message should not appear.
                    RaiseEvent TurtleBracketChanged(Me, New TurtleBracketChangedEventArgs(_turtleBracket.BracketNumber, _turtleBracket.SlPrice, _turtleBracket.TpPrice, isAdvance:=False))
                End If
            End If

            ' ── Stepped trailing bracket
            ' Runs after the broker reconciliation so a just-detected broker close does
            ' not trigger a double-flatten.  Returns True when it closes the position.
            If _positionOpen AndAlso _lastEntryPrice > 0D Then
                Dim trailClosed = Await ApplySteppedTrailAsync(CDec(lastBar.Close), ct)
                If trailClosed Then Return
            End If

            ' ── Place orders / confidence-driven scale-in ────────────────────────
            ' EmaRsiWeightedScore uses the confidence model (scale-in + neutral exit).
            ' All other strategies retain the single-trade-at-a-time guardrail.

            ' ── Risk guard (TICKET-016) ───────────────────────────────────────────
            ' Evaluate daily-loss and drawdown limits before any order is placed.
            ' Uses the broker-DB P&L for the daily check; balance-based drawdown
            ' check requires the live account balance (passed as 0 here so it is
            ' non-blocking — the daily P&L check is the primary safety gate).
            ' If the guard halts, log the reason and stop the engine immediately.
            Dim riskAccount As New Account With {.Id = _strategy.AccountId}
            If Not Await _riskGuard.EvaluateRiskAsync(riskAccount) Then
                Log($"⛔ Risk limit breached ({_riskGuard.HaltReason}) — strategy stopped to protect capital.")
                [Stop]($"Risk halt: {_riskGuard.HaltReason}")
                Return
            End If

            If barIsStale Then
                Log($"⏸  Market closed — last bar is {CInt(barAgeMins)} min old (limit: {_strategy.TimeframeMinutes * 3} min) — monitoring positions only. ({remStr})")
            ElseIf _strategy.Condition = StrategyConditionType.EmaRsiWeightedScore Then
                Await EvaluateConfidenceActionsAsync(rawUpPct, rawDownPct, side, CDec(lastBar.Close), isNewBar, ct)
            Else
                If side.HasValue Then
                    If _positionOpen Then
                        Log($"⛔ Signal ({side.Value}) blocked — position already open (positionId={If(_openPositionId.HasValue, _openPositionId.Value.ToString(), "pending")}). Waiting for close before next entry.")
                        If _pendingDiagEntry IsNot Nothing Then
                            _pendingDiagEntry.EventType = "REJECT"
                            _pendingDiagEntry.RejectionReason = $"Position already open (positionId={If(_openPositionId.HasValue, _openPositionId.Value.ToString(), "pending")})"
                            FinalizeDiagEntry(side.Value, CDec(lastBar.Close))
                            _diagLogger?.WriteEntry(_pendingDiagEntry)
                            _pendingDiagEntry = Nothing
                        End If
                    ElseIf Not IsOrderingAllowed.Invoke() Then
                        Log($"⏸  {_strategy.ContractId} market CLOSED — monitoring only (no orders) | signal: {side.Value}")
                        If _pendingDiagEntry IsNot Nothing Then
                            _pendingDiagEntry.EventType = "REJECT"
                            _pendingDiagEntry.RejectionReason = "Market closed — IsOrderingAllowed returned False"
                            FinalizeDiagEntry(side.Value, CDec(lastBar.Close))
                            _diagLogger?.WriteEntry(_pendingDiagEntry)
                            _pendingDiagEntry = Nothing
                        End If
                    ElseIf (DateTimeOffset.UtcNow - _lastPositionClosedAt).TotalSeconds < ReEntryCooldownSeconds Then
                        Dim cooldownLeft = CInt(ReEntryCooldownSeconds - (DateTimeOffset.UtcNow - _lastPositionClosedAt).TotalSeconds)
                        Log($"⏸  Re-entry cooldown — {cooldownLeft}s remaining after last close | signal: {side.Value}")
                        If _pendingDiagEntry IsNot Nothing Then
                            _pendingDiagEntry.EventType = "REJECT"
                            _pendingDiagEntry.RejectionReason = $"Re-entry cooldown ({cooldownLeft}s remaining)"
                            _diagLogger?.WriteEntry(_pendingDiagEntry)
                            _pendingDiagEntry = Nothing
                        End If
                    Else
                        _positionOpen = True
                        ' ── DIAGNOSTIC: stamp TRADE, hold in memory until position closes ─────
                        If _pendingDiagEntry IsNot Nothing Then
                            _pendingDiagEntry.EventType = "TRADE"
                            FinalizeDiagEntry(side.Value, CDec(lastBar.Close))
                            ' Init Outcome tracker; MFE/MAE updated on every tick until close
                            _pendingDiagEntry.Outcome = New DiagOutcome With {.Status = "OPEN"}
                            _openTradeDiagEntry = _pendingDiagEntry
                            _pendingDiagEntry = Nothing
                        End If
                        Dim slArg As Decimal? = If(_strategy.Condition = StrategyConditionType.LultDivergence,
                                                   _lultTriggerExtreme, _mcCloudSlPrice)
                        Await PlaceBracketOrdersAsync(side.Value, lastBar.Close, slArg)
                    End If
                Else
                    ' No signal this tick — log the diagnostic snapshot if one was built
                    If _pendingDiagEntry IsNot Nothing Then
                        _diagLogger?.WriteEntry(_pendingDiagEntry)
                        _pendingDiagEntry = Nothing
                    End If
                End If
            End If
        End Function

        Private Async Function PlaceBracketOrdersAsync(side As OrderSide, lastClose As Decimal,
                                                         Optional cloudSlPrice As Decimal? = Nothing) As Task
            ' ── Resolve eToro instrumentId ────────────────────────────────────────────
            Dim instrId As Integer = 0
            Dim fav = TopStepTrader.Core.Trading.FavouriteContracts.TryGetBySymbol(_strategy.ContractId)
            If fav IsNot Nothing Then
                instrId = fav.InstrumentId
            ElseIf Not Integer.TryParse(_strategy.ContractId, instrId) Then
                Log($"⚠️  Cannot resolve instrumentId for '{_strategy.ContractId}' — order aborted. " &
                    $"Add contract to Core.Trading.FavouriteContracts.")
                _positionOpen = False
                Return
            End If

            Dim priceUsed = lastClose   ' price used to compute SL/TP rates and for the audit log

            ' ── Margin-adjusted reference price for SL/TP calculation ───────────────────
            ' A BUY fills at ASK (that is what you actually pay); a SELL fills at BID
            ' (those are the actual proceeds).  SL% and TP% must be measured from the
            ' real fill price so that they translate to the intended monetary outcome.
            '
            ' eToro's trading margin means a position is immediately ~0.25% in loss at
            ' entry (the bid/ask spread is the cost already baked in).  Anchoring SL/TP
            ' to the fill price (ASK for BUY, BID for SELL) accounts for this correctly:
            '   BUY  : SL = Ask × (1 − sl%)   TP = Ask × (1 + tp%)
            '   SELL : SL = Bid × (1 + sl%)   TP = Bid × (1 − tp%)
            '
            ' WARNING: if sl% < spread%, the computed SL will be above the current bid
            ' (BUY) or below the current ask (SELL) and will trigger immediately at entry.
            ' Strategy parameters must set SL ≥ spread% to allow any room post-entry.
            Dim priceForSlTp = priceUsed
            Dim liveSpreadCostPct As Decimal = 0D   ' for audit log
            Dim liveQuoteObtained As Boolean = False
            If _marketData IsNot Nothing Then
                Try
                    Dim q = Await _marketData.GetCurrentQuoteAsync(_strategy.ContractId)
                    If q IsNot Nothing AndAlso q.BidPrice > 0D AndAlso q.AskPrice > 0D Then
                        ' Anchor to the side's actual fill price, not the opposite side
                        priceForSlTp = If(side = OrderSide.Buy, q.AskPrice, q.BidPrice)
                        liveSpreadCostPct = Math.Round((q.AskPrice - q.BidPrice) / q.AskPrice * 100D, 4)
                        liveQuoteObtained = True
                    End If
                Catch
                    ' Best-effort — falls back to bar close if the quote is unavailable
                End Try
            End If

            ' ── Live-quote guard ──────────────────────────────────────────────────────────────
            ' Bracket SL/TP are computed from priceForSlTp.  Ideally priceForSlTp is the live
            ' ask/bid so that SL/TP are anchored to the true fill price rather than the bar close.
            '
            ' NOTE: The MarketHubClient WebSocket feed is not yet fully implemented; live quotes
            ' may be unavailable even when the market is open.  The previous behaviour — deferring
            ' every order until a quote arrives — would permanently block all order placement while
            ' the hub is a stub.  Instead we proceed with priceForSlTp = lastClose (the most recent
            ' completed bar close) and log a clear warning.  For liquid indices (NSDQ100, SPX500)
            ' and 5-min bars the bar close is typically within 0.1–0.3% of the ask, so the computed
            ' SL/TP remain valid.  Once the live quote feed is wired the warning will disappear
            ' automatically and spread-adjusted prices will be used with no code change required.
            If Not liveQuoteObtained Then
                Log($"⚠️  Live quote unavailable for {_strategy.ContractId} — " &
                    $"using bar-close {priceUsed:F4} as SL/TP anchor (spread not measured). " &
                    $"Order proceeds; SL/TP may be slightly mis-anchored relative to fill price.")
                ' priceForSlTp already equals lastClose — no change needed, fall through
            End If

            ' ── Compute SL / TP absolute price levels from percentage ─────────────────
            ' ── Enforce eToro minimum trade size ──────────────────────────────────────
            Dim leverage = If(_strategy.Leverage > 0, _strategy.Leverage, 1)
            Dim minNotional = If(fav IsNot Nothing, fav.MinNotionalUsd, 1000D)
            Dim minCash = minNotional / leverage
            Dim userAmount = _strategy.CapitalAtRisk
            Dim finalAmount = Math.Max(userAmount, minCash)
            Dim clamped = (finalAmount > userAmount)

            ' ── Turtle bracket initialisation ─────────────────────────────────────
            ' DollarPerPoint = units open = (cash × leverage) / entryPrice
            ' priceForSlTp may differ from priceUsed when spread-adjusted for eToro.
            Dim dollarPerPoint = Math.Round((finalAmount * leverage) / priceForSlTp, 4)
            Dim sideStr = If(side = OrderSide.Buy, "BUY", "SELL")
            _totalDollarPerPoint = dollarPerPoint   ' seed with initial position units
            _turtleBracket = TopStepTrader.Core.Trading.TurtleBracketManager.Initialise(
                entryPrice:=priceForSlTp,
                side:=sideStr,
                dollarPerPoint:=dollarPerPoint,
                atrPrice:=_currentAtrValue,
                initialSlDollars:=_strategy.InitialSlAmount,
                initialTpDollars:=_strategy.InitialTpAmount)

            Dim slPriceVal As Decimal? = _turtleBracket.SlPrice
            Dim tpPriceVal As Decimal? = _turtleBracket.TpPrice

            ' ── Structured audit log (emitted before submission) ──────────────────────
            Log($"📋 ORDER | instrId={instrId} side={side} leverage={leverage}x | " &
                $"user=${userAmount:F2} minCash=${minCash:F2} final=${finalAmount:F2}" &
                If(clamped, " (clamped to min ✓)", String.Empty))
            Dim marginAdj = If(priceForSlTp <> priceUsed, $" margin-adj={priceForSlTp:F4} (spread={liveSpreadCostPct:F3}%)", String.Empty)
            Log($"📋 Turtle bracket | priceUsed={priceUsed:F4}{marginAdj} ATR={_currentAtrValue:F4} " &
                $"N=${_turtleBracket.N:F2} step=${_turtleBracket.StepSize:F2} | " &
                $"SL=${_strategy.InitialSlAmount:F2}→{slPriceVal.Value:F4} " &
                $"TP=${_strategy.InitialTpAmount:F2}→{tpPriceVal.Value:F4}")
            ' isAdvance:=False — initial placement; SL/TP set for the first time, no price level was hit.
            RaiseEvent TurtleBracketChanged(Me, New TurtleBracketChangedEventArgs(_turtleBracket.BracketNumber, _turtleBracket.SlPrice, _turtleBracket.TpPrice, isAdvance:=False))

            ' ── Entry: by-amount Market order with native eToro SL/TP + TSL ────────────
            ' Using the by-amount endpoint (Amount + Leverage) rather than by-units so that
            ' CapitalAtRisk directly controls cash invested and the min-notional clamp applies.
            ' IsTslEnabled=True activates eToro's native Trailing Stop Loss: the broker
            ' automatically trails the SL by the initial stop-distance as price improves,
            ' locking in profit continuously without requiring EditPosition calls.
            ' This is the documented, reliable path — the PUT /positions endpoint is
            ' undocumented and has hidden minimum-distance constraints.
            Dim minSlPoints = If(fav IsNot Nothing, fav.MinSlDistancePoints(priceForSlTp), 0D)
            Dim slActualDist = If(slPriceVal.HasValue, Math.Abs(priceForSlTp - slPriceVal.Value), 0D)
            If minSlPoints > 0D AndAlso slActualDist < minSlPoints Then
                Log($"⚠️  SL distance {slActualDist:F2} pts < estimated minimum {minSlPoints:F2} pts for {_strategy.ContractId} " &
                    $"— eToro will silently widen SL. TSL is enabled so the distance will self-correct.")
            End If
            Dim entryOrder As New Order With {
                .AccountId = _strategy.AccountId,
                .ContractId = _strategy.ContractId,
                .InstrumentId = instrId,
                .Side = side,
                .OrderType = OrderType.Market,
                .Amount = finalAmount,
                .Leverage = leverage,
                .StopLossRate = slPriceVal,
                .TakeProfitRate = tpPriceVal,
                .IsTslEnabled = True,
                .Status = OrderStatus.Pending,
                .PlacedAt = DateTimeOffset.UtcNow,
                .Notes = $"AI Strategy: {_strategy.Name}"
            }

            Try
                Await _orderService.PlaceOrderAsync(entryOrder)
                If entryOrder.Status = OrderStatus.Rejected Then
                    Log($"⚠️  Entry order rejected by API — no position opened.")
                    _positionOpen = False
                    Return
                End If
                Log($"✅ Entry {side} placed — instrId={instrId} amount=${finalAmount:F2} " &
                    $"SL={If(slPriceVal.HasValue, slPriceVal.Value.ToString("F4"), "none")} " &
                    $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")} TSL=on")
            Catch ex As Exception
                Log($"⚠️  Entry order failed: {ex.Message}")
                _positionOpen = False
                Return
            End Try

            ' Save entry context for position tracking
            _lastEntryPrice = priceUsed
            _lastEntrySide = side
            _lastConfidencePct = _pendingConfidencePct
            _lastTpExternalId = entryOrder.ExternalOrderId
            _lastTpPrice = If(tpPriceVal.HasValue, tpPriceVal.Value, 0D)
            _lastSlPrice = If(slPriceVal.HasValue, slPriceVal.Value, 0D)
            _lastFinalAmount = finalAmount
            _lastLeverage = leverage
            _openPositionId = entryOrder.ExternalPositionId
            _positionOpenedAt = DateTimeOffset.UtcNow   ' start 60-s propagation guard
            _openTradeCount += 1

            RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(side, _strategy.ContractId,
                                                                _lastConfidencePct,
                                                                _positionOpenedAt,
                                                                entryOrder.ExternalOrderId,
                                                                entryOrder.ExternalPositionId,
                                                                _positionOpenedAt,
                                                                finalAmount,
                                                                leverage,
                                                                priceUsed))
            Log($"Position open — positionId={_openPositionId}. Monitoring for SL/TP hit every 30 s.")
        End Function

        ' ── Helpers ───────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Closes all open positions for the current contract, fires TradeClosed for
        ''' any row still in-progress, then resets position state and flips the trend.
        ''' Called when ReversalConfirmBars consecutive new bars confirm an opposing signal.
        ''' </summary>
        Private Async Function DoReversalFlushAsync(newSide As OrderSide,
                                                     lastClose As Decimal,
                                                     ct As CancellationToken) As Task
            Dim prevSide = If(_currentTrendSide.HasValue, _currentTrendSide.Value.ToString(), "None")
            Log($"🔄 REVERSAL CONFIRMED — was {prevSide}, flipping to {newSide}. " &
                $"Closing/cancelling all {_strategy.ContractId} positions...")

            Dim ok = Await _orderService.FlattenContractAsync(_strategy.AccountId, _strategy.ContractId, ct)
            If ok Then
                Log($"✅ Flatten complete — {_strategy.ContractId} closed. Waiting for next {newSide} signal...")
            Else
                Log($"⚠️  Flatten partially failed for {_strategy.ContractId} — check positions manually. Waiting for next {newSide} signal...")
            End If

            ' Raise TradeClosed for every row that is still showing "In Progress".
            Dim reversalClosedCount = If(_positionOpen, Math.Max(1, _openTradeCount), 0)
            If reversalClosedCount > 0 Then
                ' Prefer broker-authoritative P&L; fall back to local estimate only when
                ' the API sync has not yet run (position opened less than 60 s ago).
                Dim closePnl As Decimal = _lastApiPnl
                If closePnl = 0D AndAlso _lastEntryPrice > 0D AndAlso _lastFinalAmount > 0D Then
                    Dim priceMove = If(_lastEntrySide = OrderSide.Buy,
                                       lastClose - _lastEntryPrice,
                                       _lastEntryPrice - lastClose)
                    closePnl = Math.Round(priceMove / _lastEntryPrice * _lastFinalAmount * CDec(_lastLeverage), 2)
                End If
                If reversalClosedCount > 1 Then
                    Log($"🔄 Closing {reversalClosedCount} stale UI trade row(s) during reversal flush for {_strategy.ContractId}")
                End If
                For i As Integer = 1 To reversalClosedCount
                    RaiseEvent TradeClosed(Me, New TradeClosedEventArgs("Reversal", closePnl))
                    closePnl = 0D   ' P&L only known for the most-recently synced position
                Next
            End If

            WriteDiagPostMortem("Reversal", _lastApiPnl)
            _positionOpen = False
            _openPositionId = Nothing
            _openTradeCount = 0
            _positionOpenedAt = DateTimeOffset.MinValue
            _lastPositionClosedAt = DateTimeOffset.UtcNow
            _lastApiPnl = 0D
            _currentTrendSide = newSide
            _reversalCandidateSide = Nothing
            _reversalConfirmCount = 0
            _extremeConfidenceDurationCount = 0
            _scaleInTradeCount = 0
            ResetTrailState()
        End Function

        ' ── Confidence-driven scale-in / neutral-exit (EMA/RSI strategy) ─────────

        ''' <summary>
        ''' Central confidence decision point for EmaRsiWeightedScore trades.
        ''' Priority order:
        '''   1. Neutral band (40–60%) → flatten all positions immediately.
        '''   2. Mid-confidence adverse exit: position open + opposite direction above neutral
        '''      band for AdverseConfidenceBars consecutive new bars → flatten.
        '''   3. No position open + signal fired → place initial trade.
        '''   4. Position open + extreme confidence in same direction → accumulate ticks,
        '''      fire scale-in trade every 3 consecutive extreme ticks (max 3 scale-ins).
        ''' </summary>
        Private Async Function EvaluateConfidenceActionsAsync(
                rawUpPct As Integer,
                rawDownPct As Integer,
                side As OrderSide?,
                lastClose As Decimal,
                isNewBar As Boolean,
                ct As CancellationToken) As Task

            ' ── 1. Neutral band — highest priority: get flat immediately ──────────
            If rawUpPct >= NeutralConfidenceLow AndAlso rawUpPct <= NeutralConfidenceHigh Then
                _extremeConfidenceDurationCount = 0
                _adverseConfidenceCount = 0
                If _positionOpen Then
                    Log($"🔔 NEUTRAL CONFIDENCE — UP={rawUpPct}% DOWN={rawDownPct}% " &
                        $"(band: {NeutralConfidenceLow}–{NeutralConfidenceHigh}%) — flattening all positions immediately...")
                    Await DoNeutralFlattenAsync(ct)
                Else
                    Log($"Confidence neutral — UP={rawUpPct}% DOWN={rawDownPct}% | no open positions at broker — confidence exit skipped")
                End If
                Return
            End If

            ' ── 2. Mid-confidence adverse exit ──────────────────────────────────────
            ' The reversal exit (below) only fires at ≥85% for 2 bars; a SELL position
            ' with confidence at 61–84% BUY would otherwise sit open indefinitely accumulating
            ' losses.  When confidence has risen above NeutralConfidenceHigh AND is clearly
            ' against the current position direction for AdverseConfidenceBars consecutive NEW
            ' bars, flatten regardless of whether the full reversal threshold has been reached.
            If _positionOpen AndAlso _currentTrendSide.HasValue Then
                Dim adverseBull = (rawUpPct > NeutralConfidenceHigh AndAlso _currentTrendSide.Value = OrderSide.Sell)
                Dim adverseBear = (rawUpPct < NeutralConfidenceLow AndAlso _currentTrendSide.Value = OrderSide.Buy)

                If adverseBull OrElse adverseBear Then
                    If isNewBar Then
                        _adverseConfidenceCount += 1
                        Log($"⚠️  Adverse confidence bar {_adverseConfidenceCount}/{AdverseConfidenceBars} — " &
                            $"UP={rawUpPct}% DOWN={rawDownPct}% against open {_currentTrendSide.Value} position")
                    End If
                    If _adverseConfidenceCount >= AdverseConfidenceBars Then
                        Log($"🔴 ADVERSE CONFIDENCE EXIT — UP={rawUpPct}% DOWN={rawDownPct}% " &
                            $"has been against open {_currentTrendSide.Value} for {_adverseConfidenceCount} new bars — flattening...")
                        _adverseConfidenceCount = 0
                        Await DoNeutralFlattenAsync(ct)
                        Return
                    End If
                Else
                    If _adverseConfidenceCount > 0 Then
                        Log($"↩  Adverse confidence cleared — UP={rawUpPct}% no longer adverse to {_currentTrendSide.Value}")
                    End If
                    _adverseConfidenceCount = 0
                End If
            Else
                _adverseConfidenceCount = 0
            End If

            ' ── 3. Initial trade placement (no position open yet) ─────────────────
            If Not _positionOpen Then
                If side.HasValue Then
                    If Not IsOrderingAllowed.Invoke() Then
                        Log($"⏸  {_strategy.ContractId} market CLOSED — monitoring only (no orders) | UP={rawUpPct}% DOWN={rawDownPct}% signal={side.Value}")
                    ElseIf (DateTimeOffset.UtcNow - _lastPositionClosedAt).TotalSeconds < ReEntryCooldownSeconds Then
                        Dim cooldownLeft = CInt(ReEntryCooldownSeconds - (DateTimeOffset.UtcNow - _lastPositionClosedAt).TotalSeconds)
                        Log($"⏸  Re-entry cooldown — {cooldownLeft}s remaining after last close | UP={rawUpPct}% DOWN={rawDownPct}%")
                    Else
                        Log($"🎯 INITIAL TRADE — {side.Value} | Confidence: UP={rawUpPct}% DOWN={rawDownPct}%")
                        _positionOpen = True
                        Await PlaceBracketOrdersAsync(side.Value, lastClose)
                        _extremeConfidenceDurationCount = 0   ' start fresh counter for scale-in window
                    End If
                End If
                Return
            End If

            ' ── 4. Position is open — evaluate extreme confidence for scale-in ─────
            ' Scale-in fires when the confidence score is in extreme territory:
            '   Bull: rawUpPct  > ScaleInBullThreshold (>80 = UP ≥ 81%)
            '   Bear: rawUpPct  < ScaleInBearThreshold (<20 = DOWN ≥ 81%)
            '
            ' NOTE: The EMA21 proximity guard (withinPullback) has been removed as a
            ' blocking condition.  In a strong trend — exactly when the confidence score
            ' reaches the extreme threshold — price is intentionally AWAY from EMA21;
            ' requiring proximity to EMA21 simultaneously creates a logical contradiction
            ' that prevents scale-in from ever firing.  EMA21 distance is still computed
            ' and logged for diagnostic purposes.
            Dim isExtremeBull = rawUpPct > ScaleInBullThreshold
            Dim isExtremeBear = rawUpPct < ScaleInBearThreshold

            ' Log EMA21 proximity as a quality metric (informational only)
            Dim ema21DistPct As Decimal = 0D
            If _currentEma21 > 0D Then
                ema21DistPct = Math.Round(Math.Abs(lastClose - _currentEma21) / _currentEma21 * 100D, 3)
            End If

            If Not isExtremeBull AndAlso Not isExtremeBear Then
                If _extremeConfidenceDurationCount > 0 Then
                    Log($"Scale-in paused — UP={rawUpPct}% DOWN={rawDownPct}% " &
                        $"(need >{ScaleInBullThreshold}% or <{ScaleInBearThreshold}%) | " &
                        $"EMA21 dist={ema21DistPct:F3}% | timer reset")
                End If
                _extremeConfidenceDurationCount = 0
                Return
            End If

            Dim extremeSide As OrderSide = If(isExtremeBull, OrderSide.Buy, OrderSide.Sell)

            ' Direction must match the established trend — no cross-contamination.
            ' NOTE: Only _extremeConfidenceDurationCount is reset here (the "patience" counter
            ' for requiring N consecutive extreme-confidence bars before firing a scale-in).
            ' _scaleInTradeCount is intentionally NOT reset on a direction mismatch: the cap
            ' counts how many scale-in positions have actually been opened in this position
            ' lifecycle and must remain accurate regardless of transient oscillations in the
            ' confidence indicator.  Resetting it here would allow extra positions to be opened
            ' after a short-lived direction flip, violating the MaxScaleInTrades guard.
            If _currentTrendSide.HasValue AndAlso _currentTrendSide.Value <> extremeSide Then
                If _extremeConfidenceDurationCount > 0 Then
                    Log($"Scale-in direction mismatch — extreme={extremeSide} but trend={_currentTrendSide.Value} | patience counter reset (scale-in count preserved at {_scaleInTradeCount}/{MaxScaleInTrades})")
                End If
                _extremeConfidenceDurationCount = 0
                Return
            End If

            ' Increment the consecutive extreme-confidence counter on NEW BARS only (TICKET-021).
            ' The reversal counter uses the same isNewBar guard for the same reason: multiple
            ' 30-second timer ticks can see the same last bar, and counting them separately
            ' would allow all 3 scale-ins to fire within a single 5-minute candle.
            If isNewBar Then
                _extremeConfidenceDurationCount += 1
                Log($"⏱  Extreme confidence bar {_extremeConfidenceDurationCount}/{ScaleInRequiredTicks} — " &
                    $"UP={rawUpPct}% DOWN={rawDownPct}% | {extremeSide} | EMA21 dist={ema21DistPct:F3}% | " &
                    $"scale-in {_scaleInTradeCount}/{MaxScaleInTrades}")
            Else
                Log($"⏱  Extreme confidence (same bar, tick skipped) — " &
                    $"UP={rawUpPct}% DOWN={rawDownPct}% | {extremeSide} | EMA21 dist={ema21DistPct:F3}% | " &
                    $"bar count {_extremeConfidenceDurationCount}/{ScaleInRequiredTicks}")
            End If

            ' Scale-in cap reached — log once per tick to keep the user informed
            If _scaleInTradeCount >= MaxScaleInTrades Then
                Log($"Scale-in cap reached ({MaxScaleInTrades}/{MaxScaleInTrades}) — holding position, no further scale-in trades")
                Return
            End If

            ' Not enough consecutive extreme ticks yet
            If _extremeConfidenceDurationCount < ScaleInRequiredTicks Then Return

            ' ── All conditions met — place a scale-in trade ───────────────────────
            _extremeConfidenceDurationCount = 0   ' reset window for next scale-in
            _scaleInTradeCount += 1
            If Not IsOrderingAllowed.Invoke() Then
                Log($"⏸  {_strategy.ContractId} market CLOSED — scale-in {_scaleInTradeCount}/{MaxScaleInTrades} suppressed | UP={rawUpPct}% DOWN={rawDownPct}%")
                Return
            End If
            Log($"📈 SCALE-IN {_scaleInTradeCount}/{MaxScaleInTrades} — Adding {extremeSide} position | " &
                $"Confidence: UP={rawUpPct}% DOWN={rawDownPct}% | " &
                $"Amount=${_strategy.ScaleInAmount:F0} Leverage={_strategy.ScaleInLeverage}x")
            Await PlaceScaleInOrderAsync(extremeSide, lastClose, _scaleInTradeCount)
        End Function

        ''' <summary>
        ''' Places a fixed-size scale-in order ($200 / 5×) using the same SL/TP % as the
        ''' parent strategy.  Raises TradeOpened so the UI trade table gains a new row.
        ''' </summary>
        Private Async Function PlaceScaleInOrderAsync(side As OrderSide,
                                                       lastClose As Decimal,
                                                       scaleIndex As Integer) As Task
            Dim instrId As Integer = 0
            Dim fav = TopStepTrader.Core.Trading.FavouriteContracts.TryGetBySymbol(_strategy.ContractId)
            If fav IsNot Nothing Then
                instrId = fav.InstrumentId
            ElseIf Not Integer.TryParse(_strategy.ContractId, instrId) Then
                Log($"⚠️  Cannot resolve instrumentId for '{_strategy.ContractId}' — scale-in {scaleIndex}/{MaxScaleInTrades} aborted.")
                Return
            End If

            Dim priceUsed = lastClose
            Dim priceForSlTp = priceUsed
            Dim siSpreadCostPct As Decimal = 0D   ' for audit log
            If _marketData IsNot Nothing Then
                Try
                    Dim q = Await _marketData.GetCurrentQuoteAsync(_strategy.ContractId)
                    If q IsNot Nothing AndAlso q.BidPrice > 0D AndAlso q.AskPrice > 0D Then
                        ' Anchor to actual fill price: BUY pays ASK; SELL receives BID
                        priceForSlTp = If(side = OrderSide.Buy, q.AskPrice, q.BidPrice)
                        siSpreadCostPct = Math.Round((q.AskPrice - q.BidPrice) / q.AskPrice * 100D, 4)
                    End If
                Catch
                End Try
            End If
            ' Reuse the current Turtle bracket's SL/TP prices — the entire position
            ' (initial + all scale-ins) shares one bracket, which only advances in steps.
            Dim slPriceVal As Decimal? = If(_turtleBracket IsNot Nothing, _turtleBracket.SlPrice, CType(Nothing, Decimal?))
            Dim tpPriceVal As Decimal? = If(_turtleBracket IsNot Nothing, _turtleBracket.TpPrice, CType(Nothing, Decimal?))

            ' Enforce eToro minimum notional for scale-in leverage
            Dim minNotional = If(fav IsNot Nothing, fav.MinNotionalUsd, 1000D)
            Dim minCash = minNotional / _strategy.ScaleInLeverage
            Dim finalAmount = Math.Max(_strategy.ScaleInAmount, minCash)
            Dim clamped = (finalAmount > _strategy.ScaleInAmount)

            Log($"📋 SCALE-IN ORDER {scaleIndex}/{MaxScaleInTrades} | instrId={instrId} side={side} leverage={_strategy.ScaleInLeverage}x | " &
                $"amount=${_strategy.ScaleInAmount:F0} final=${finalAmount:F2}" & If(clamped, " (clamped to min ✓)", String.Empty))
            Dim marginAdjSi = If(priceForSlTp <> priceUsed, $" margin-adj={priceForSlTp:F4} (spread={siSpreadCostPct:F3}%)", String.Empty)
            Dim bracketInfo = If(_turtleBracket IsNot Nothing, $"bracket#{_turtleBracket.BracketNumber} SL=${_strategy.InitialSlAmount:F2} TP=${_strategy.InitialTpAmount:F2}", "no bracket")
            Log($"📋 priceUsed={priceUsed:F4}{marginAdjSi} | {bracketInfo} | " &
                $"SL={If(slPriceVal.HasValue, slPriceVal.Value.ToString("F4"), "none")} | " &
                $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")}")

            Dim entryOrder As New Order With {
                .AccountId = _strategy.AccountId,
                .ContractId = _strategy.ContractId,
                .InstrumentId = instrId,
                .Side = side,
                .OrderType = OrderType.Market,
                .Amount = finalAmount,
                .Leverage = _strategy.ScaleInLeverage,
                .StopLossRate = slPriceVal,
                .TakeProfitRate = tpPriceVal,
                .IsTslEnabled = True,
                .Status = OrderStatus.Pending,
                .PlacedAt = DateTimeOffset.UtcNow,
                .Notes = $"AI Scale-In {scaleIndex}/{MaxScaleInTrades}: {_strategy.Name}"
            }

            Try
                Await _orderService.PlaceOrderAsync(entryOrder)
                If entryOrder.Status = OrderStatus.Rejected Then
                    Log($"⚠️  Scale-in {scaleIndex}/{MaxScaleInTrades} rejected by API — order not placed.")
                    Return
                End If
                Log($"✅ Scale-in {scaleIndex}/{MaxScaleInTrades} {side} placed — instrId={instrId} amount=${finalAmount:F2} " &
                    $"SL={If(slPriceVal.HasValue, slPriceVal.Value.ToString("F4"), "none")} " &
                    $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")} TSL=on")
            Catch ex As Exception
                Log($"⚠️  Scale-in {scaleIndex}/{MaxScaleInTrades} order failed: {ex.Message}")
                Return
            End Try

            ' ── Rescale the Turtle bracket to include this position's units ────────────
            ' DollarPerPoint = total units open = sum of (amount × leverage / price) per position.
            ' Updating it ensures advancement thresholds and SL-breach checks reflect the
            ' combined portfolio P&L, not just the initial single-position P&L.
            If _turtleBracket IsNot Nothing AndAlso priceForSlTp > 0D Then
                Dim newPositionDpp = Math.Round((finalAmount * CDec(_strategy.ScaleInLeverage)) / priceForSlTp, 4)
                _totalDollarPerPoint += newPositionDpp
                _turtleBracket = TopStepTrader.Core.Trading.TurtleBracketManager.Rescale(
                    _turtleBracket, _totalDollarPerPoint)
                Log($"🐢 Bracket rescaled — scale-in {scaleIndex} added {newPositionDpp:F4} DPP " &
                    $"→ total DPP={_totalDollarPerPoint:F4} " &
                    $"SL=${_turtleBracket.CurrentSlDollars:F2} TP=${_turtleBracket.CurrentTpDollars:F2}")
            End If

            RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(
                side, _strategy.ContractId, _pendingConfidencePct,
                DateTimeOffset.UtcNow,
                entryOrder.ExternalOrderId,
                entryOrder.ExternalPositionId,
                DateTimeOffset.UtcNow,
                finalAmount,
                _strategy.ScaleInLeverage,
                priceUsed))
            _openTradeCount += 1
        End Function

        ''' <summary>
        ''' Flattens all open positions for the current contract when confidence enters the
        ''' neutral band (40–60%).  Resets all position and scale-in state.
        ''' Works regardless of whether positions were opened by the app or manually on eToro.
        ''' </summary>
        Private Async Function DoNeutralFlattenAsync(ct As CancellationToken) As Task
            Log($"🔴 NEUTRAL EXIT — Closing ALL positions for {_strategy.ContractId} via API flatten...")
            Dim ok = Await _orderService.FlattenContractAsync(_strategy.AccountId, _strategy.ContractId, ct)
            If ok Then
                Log($"✅ Neutral flatten complete — {_strategy.ContractId} fully closed. " &
                    $"Confidence returned to neutral; re-entry requires a new extreme signal.")
            Else
                Log($"⚠️  Neutral flatten partially failed for {_strategy.ContractId} — check positions manually.")
            End If

            ' Fire TradeClosed for every locally-tracked open trade so all "In Progress" UI
            ' rows are reconciled — not just the initial position row.
            Dim closedCount = If(_positionOpen, Math.Max(1, _openTradeCount), 0)
            If closedCount > 0 Then
                If closedCount > 1 Then
                    Log($"⚠️  Closing {closedCount} UI trade row(s) for {_strategy.ContractId} — all positions flattened")
                End If
                Dim closePnl = _lastApiPnl
                For i As Integer = 1 To closedCount
                    RaiseEvent TradeClosed(Me, New TradeClosedEventArgs("Neutral", closePnl))
                    closePnl = 0D   ' P&L only known for the most-recently synced position
                Next
            End If

            WriteDiagPostMortem("Neutral", _lastApiPnl)
            _positionOpen = False
            _openPositionId = Nothing
            _openTradeCount = 0
            _positionOpenedAt = DateTimeOffset.MinValue
            _lastPositionClosedAt = DateTimeOffset.UtcNow
            _lastApiPnl = 0D
            _extremeConfidenceDurationCount = 0
            _scaleInTradeCount = 0
            _currentTrendSide = Nothing
            _reversalCandidateSide = Nothing
            _reversalConfirmCount = 0
            ResetTrailState()
        End Function

        ' ── Turtle bracket monitoring ─────────────────────────────────────────────

        ''' <summary>
        ''' Checks the Turtle bracket state each tick. If the live P&amp;L has hit the SL
        ''' the position is closed.  If it has reached the TP the bracket advances one step
        ''' (new SL = old TP; new TP = old TP + 0.5×N) and the updated levels are pushed
        ''' to the broker so positions remain protected if the engine is stopped.
        ''' Returns True when the position was closed this tick so the caller can return early.
        ''' </summary>
        Private Async Function ApplySteppedTrailAsync(currentPrice As Decimal,
                                                      ct As CancellationToken) As Task(Of Boolean)
            ' ── Guard: bracket not yet initialised ───────────────────────────────
            ' Log explicitly so the audit trail shows why bracket management is absent.
            If _turtleBracket Is Nothing Then
                If _lastEntryPrice > 0D Then
                    Log($"🐢 Bracket check skipped — bracket not yet initialised " &
                        $"(entry={_lastEntryPrice:F4} bracketInitPending={_bracketInitPending})")
                End If
                Return False
            End If
            If _lastEntryPrice <= 0D Then Return False

            Dim pnl = TopStepTrader.Core.Trading.TurtleBracketManager.ComputePnlDollars(_turtleBracket, currentPrice)

            ' ── Per-tick bracket progress ─────────────────────────────────────────
            ' Logged on every timer tick so the audit log shows the gap between current
            ' P&L and each level, even when nothing fires.  Immediately reveals whether
            ' the function is running and how far the position is from an advance or close.
            Dim gapToTp = _turtleBracket.CurrentTpDollars - pnl
            Dim gapToSl = pnl - _turtleBracket.CurrentSlDollars
            Log($"🐢 Bracket#{_turtleBracket.BracketNumber} — " &
                $"price={currentPrice:F4} P&L=${pnl:F2} | " &
                $"SL ${_turtleBracket.CurrentSlDollars:F2}→{_turtleBracket.SlPrice:F4} (${gapToSl:F2} to breach) | " &
                $"TP ${_turtleBracket.CurrentTpDollars:F2}→{_turtleBracket.TpPrice:F4} (${gapToTp:F2} to advance)")

            ' ── SL breach → close position ───────────────────────────────────────
            If TopStepTrader.Core.Trading.TurtleBracketManager.IsSlBreached(_turtleBracket, pnl) Then
                Dim reason = $"Turtle SL (bracket #{_turtleBracket.BracketNumber})"
                Log($"🛑 {reason} HIT — price={currentPrice:F4} " &
                    $"SL={_turtleBracket.SlPrice:F4} P&L=${pnl:F2} — closing position")
                Await _orderService.FlattenContractAsync(_strategy.AccountId, _strategy.ContractId, ct)
                Dim closedCount = Math.Max(1, _openTradeCount)
                Dim closePnl = _lastApiPnl
                For i As Integer = 1 To closedCount
                    RaiseEvent TradeClosed(Me, New TradeClosedEventArgs(reason, closePnl))
                    closePnl = 0D
                Next
                WriteDiagPostMortem(reason, _lastApiPnl)
                _positionOpen = False
                _openPositionId = Nothing
                _openTradeCount = 0
                _positionOpenedAt = DateTimeOffset.MinValue
                _lastPositionClosedAt = DateTimeOffset.UtcNow
                _lastApiPnl = 0D
                ResetTrailState()
                Return True
            End If

            ' ── TP reached → advance bracket ─────────────────────────────────────
            If Not TopStepTrader.Core.Trading.TurtleBracketManager.ShouldAdvance(_turtleBracket, pnl) Then Return False

            _turtleBracket = TopStepTrader.Core.Trading.TurtleBracketManager.Advance(_turtleBracket)
            Log($"⬆  BRACKET #{_turtleBracket.BracketNumber} advanced — P&L=${pnl:F2} " &
                $"new SL=${_turtleBracket.CurrentSlDollars:F2}→{_turtleBracket.SlPrice:F4} " &
                $"new TP=${_turtleBracket.CurrentTpDollars:F2}→{_turtleBracket.TpPrice:F4}")
            ' isAdvance:=True — TP level was hit; SL stepped up to lock profit.
            ' This is the only raise site that should display the "Turtle Applied" UI message.
            RaiseEvent TurtleBracketChanged(Me, New TurtleBracketChangedEventArgs(_turtleBracket.BracketNumber, _turtleBracket.SlPrice, _turtleBracket.TpPrice, isAdvance:=True))
            Await PushTrailToAllPositionsAsync(ct)
            Return False
        End Function

        ''' <summary>
        ''' Pushes the current Turtle bracket SL/TP prices to every live position on the broker.
        ''' Called each time the bracket advances so broker-side SL/TP stays in sync —
        ''' positions remain protected if the engine is stopped between ticks.
        ''' </summary>
        Private Async Function PushTrailToAllPositionsAsync(ct As CancellationToken) As Task
            If _turtleBracket Is Nothing Then Return
            Dim slToSend As Decimal? = _turtleBracket.SlPrice
            Dim tpToSend As Decimal? = _turtleBracket.TpPrice

            Log($"🐢 PushTrail — bracket#{_turtleBracket.BracketNumber} " &
                $"SL={slToSend.Value:F4} TP={tpToSend.Value:F4} — querying live positions for {_strategy.ContractId}…")

            Dim liveOrders = Await _orderService.GetLiveWorkingOrdersAsync(_strategy.AccountId, _strategy.ContractId, ct)
            Dim orderList = liveOrders.ToList()

            If orderList.Count = 0 Then
                ' No positions found — SL/TP cannot be pushed.  This is the most common reason
                ' why the bracket advances in the engine but the broker SL/TP is not updated.
                ' Possible causes: SearchOrdersAsync returned an empty/error response; the
                ' contractId symbol lookup failed; or all positions were closed on the broker side.
                Log($"⚠️  PushTrail — GetLiveWorkingOrders returned 0 positions for {_strategy.ContractId} " &
                    $"(bracket#{_turtleBracket.BracketNumber}) — SL/TP NOT sent to broker")
                Return
            End If

            Log($"🐢 PushTrail — {orderList.Count} position(s) found; pushing SL={slToSend.Value:F4} TP={tpToSend.Value:F4}")

            For Each pos In orderList
                Dim posId = If(pos.ExternalPositionId, pos.ExternalOrderId)
                If Not posId.HasValue Then
                    Log($"⚠️  PushTrail — position has no ExternalPositionId or ExternalOrderId " &
                        $"(ContractId={pos.ContractId}) — skipped")
                    Continue For
                End If
                Log($"🐢 PushTrail — calling EditPositionSlTp for positionId={posId.Value} " &
                    $"SL={slToSend.Value:F4} TP={tpToSend.Value:F4} TSL=on")
                Dim ok = Await _orderService.EditPositionSlTpAsync(posId.Value, slToSend, tpToSend,
                                                                    enableTsl:=True, cancel:=ct)
                If ok Then
                    Log($"✅ Bracket SL/TP pushed to broker — positionId={posId.Value} " &
                        $"SL={slToSend.Value:F4} TP={tpToSend.Value:F4} TSL=on")
                Else
                    Log($"⚠️  Failed to push bracket SL/TP to broker for positionId={posId.Value} " &
                        $"SL={slToSend.Value:F4} TP={tpToSend.Value:F4} — engine-side monitoring still active")
                End If
            Next
        End Function

        ''' <summary>Resets Turtle bracket state. Called on position close, reversal, and flatten.</summary>
        Private Sub ResetTrailState()
            _turtleBracket = Nothing
            _bracketInitPending = False
            _adverseConfidenceCount = 0
            _totalDollarPerPoint = 0D
        End Sub

        Private Sub Log(message As String)
            Dim timestamped = $"{DateTime.Now:HH:mm:ss}  {message}"
            _logger.LogInformation("[StrategyEngine] {Msg}", message)
            RaiseEvent LogMessage(Me, timestamped)
        End Sub

        ' ══════════════════════════════════════════════════════════════════════════
        ' DIAGNOSTIC HELPERS
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Populates the nested Settings object and completes the NoiseCheck object on
        ''' <see cref="_pendingDiagEntry"/> immediately before it is written to the log.
        ''' Uses the same SL/TP formula as <see cref="PlaceBracketOrdersAsync"/> so the
        ''' diagnostic entry accurately reflects what will be submitted to eToro.
        ''' Safe to call when _pendingDiagEntry is Nothing (no-op).
        ''' </summary>
        Private Sub FinalizeDiagEntry(side As OrderSide, entryPrice As Decimal)
            If _pendingDiagEntry Is Nothing Then Return

            _pendingDiagEntry.Action = If(side = OrderSide.Buy, "BUY", "SELL")

            ' Mirror the margin-adjusted anchor from PlaceBracketOrdersAsync using the
            ' bid/ask already captured in the snapshot so diagnostic settings match the
            ' actual order.  ASK for BUY (fill price = cost basis); BID for SELL.
            Dim priceForSlTp = entryPrice
            Dim diagMarginCostPct As Decimal = 0D
            Dim m = _pendingDiagEntry.MetricsAtEntry
            If m IsNot Nothing AndAlso m.Bid > 0D AndAlso m.Ask > 0D Then
                priceForSlTp = If(side = OrderSide.Buy, m.Ask, m.Bid)
                diagMarginCostPct = Math.Round((m.Ask - m.Bid) / m.Ask * 100D, 4)
            End If

            ' Use the Turtle bracket that was just initialised in PlaceBracketOrdersAsync.
            Dim slPrice As Decimal = If(_turtleBracket IsNot Nothing, _turtleBracket.SlPrice, 0D)
            Dim tpPrice As Decimal = If(_turtleBracket IsNot Nothing, _turtleBracket.TpPrice, 0D)
            ' SL distance in price units — used for noise-check diagnostics
            Dim slDist As Decimal = If(priceForSlTp > 0D AndAlso slPrice > 0D,
                                       Math.Abs(priceForSlTp - slPrice), 0D)
            Dim tpDist As Decimal = If(priceForSlTp > 0D AndAlso tpPrice > 0D,
                                       Math.Abs(priceForSlTp - tpPrice), 0D)

            ' ── Settings nested object ─────────────────────────────────────────────
            _pendingDiagEntry.Settings = New DiagSettings With {
                .InitialSlAmount = _strategy.InitialSlAmount,
                .InitialTpAmount = _strategy.InitialTpAmount,
                .SlPrice = slPrice,
                .TpPrice = tpPrice,
                .SlSource = If(_turtleBracket IsNot Nothing, $"Turtle(${_strategy.InitialSlAmount:F0})", "none"),
                .RiskRewardRatio = If(slDist > 0D, Math.Round(tpDist / slDist, 2), 0D),
                .SlDistanceInAtr = If(_currentAtrValue > 0D AndAlso slDist > 0D, Math.Round(slDist / _currentAtrValue, 3), 0D),
                .EffectiveEntryPrice = priceForSlTp,
                .EntryMarginCostPct = diagMarginCostPct
            }

            ' ── Complete NoiseCheck with SL-distance-derived flags ─────────────────
            ' The NoiseCheck object was partially built in the snapshot block (avg range + prev bars).
            ' Here we compute the SL-dependent fields: is the stop inside the noise floor?
            If _pendingDiagEntry.NoiseCheck IsNot Nothing Then
                Dim avgRange = _pendingDiagEntry.NoiseCheck.Prev3BarAvgRange
                _pendingDiagEntry.NoiseCheck.SlDistanceAbs = slDist
                _pendingDiagEntry.NoiseCheck.IsSlInsideNoise = (slDist > 0D AndAlso slDist < avgRange)
                _pendingDiagEntry.NoiseCheck.NoiseRatio =
                    If(avgRange > 0D, Math.Round(slDist / avgRange, 3), 0D)
                Dim spread As Decimal = 0D
                If _pendingDiagEntry.MetricsAtEntry IsNot Nothing Then
                    spread = _pendingDiagEntry.MetricsAtEntry.Ask - _pendingDiagEntry.MetricsAtEntry.Bid
                End If
                If spread > 0D AndAlso slDist > 0D Then
                    _pendingDiagEntry.NoiseCheck.EffectiveSlippageRatio = Math.Round(spread / slDist, 3)
                End If
            End If

            ' ── Refine entry price in MetricsAtEntry (was set to bar close; now exact) ─
            If _pendingDiagEntry.MetricsAtEntry IsNot Nothing Then
                _pendingDiagEntry.MetricsAtEntry.PriceEntry = entryPrice
            End If
        End Sub

        ''' <summary>
        ''' Completes and writes the in-memory TRADE record when a position closes.
        ''' Populates the Outcome nested object (status, P&amp;L, MFE/MAE, lifetime) and
        ''' writes the single complete JSON line to the diagnostic log.
        ''' No-op when no active TRADE record or logger is not started.
        ''' </summary>
        Private Sub WriteDiagPostMortem(closeReason As String, finalPnl As Decimal)
            If _diagLogger Is Nothing OrElse _openTradeDiagEntry Is Nothing Then Return

            Dim lifetime As Long = 0L
            If _positionOpenedAt > DateTimeOffset.MinValue Then
                lifetime = CLng(Math.Max(0, (DateTimeOffset.UtcNow - _positionOpenedAt).TotalSeconds))
            End If

            ' Spread cost estimate: (entrySpread / entryPrice) × capitalAtRisk
            ' Expressed as fraction of |finalPnl| — high values flag spread-dominated losses.
            Dim entrySpread As Decimal = 0D
            If _openTradeDiagEntry.MetricsAtEntry IsNot Nothing Then
                entrySpread = _openTradeDiagEntry.MetricsAtEntry.Ask -
                              _openTradeDiagEntry.MetricsAtEntry.Bid
            End If
            Dim spreadCashCost As Decimal = 0D
            If _lastEntryPrice > 0D AndAlso entrySpread > 0D Then
                spreadCashCost = Math.Round(entrySpread / _lastEntryPrice * _lastFinalAmount, 4)
            End If
            Dim spreadImpact As Decimal = 0D
            If Math.Abs(finalPnl) > 0D Then
                spreadImpact = Math.Round(spreadCashCost / Math.Abs(finalPnl), 4)
            End If

            ' Map close reason to a standard status string
            Dim status As String
            If closeReason = "SL/TP" Then
                status = If(finalPnl >= 0D, "TP_HIT", "SL_HIT")
            ElseIf closeReason = "Trail SL" Then
                status = "TRAIL_SL"
            ElseIf closeReason = "Trail TP" Then
                status = "TRAIL_TP"
            ElseIf closeReason = "Reversal" Then
                status = "REVERSAL"
            ElseIf closeReason = "Neutral" Then
                status = "NEUTRAL"
            Else
                status = closeReason.ToUpper().Replace(" ", "_")
            End If

            ' Populate (or create) the Outcome block
            Dim outcome = If(_openTradeDiagEntry.Outcome, New DiagOutcome())
            outcome.Status = status
            outcome.PlUsd = finalPnl
            outcome.PlPct = If(_lastEntryPrice > 0D AndAlso _lastFinalAmount > 0D,
                               Math.Round(finalPnl / _lastFinalAmount * 100D, 5), 0D)
            outcome.TradeLifetimeSeconds = lifetime
            outcome.SpreadCostImpact = spreadImpact
            _openTradeDiagEntry.Outcome = outcome

            ' Stamp close time and write the single complete record
            _openTradeDiagEntry.Timestamp = DateTimeOffset.UtcNow.ToString("o")
            _diagLogger.WriteEntry(_openTradeDiagEntry)
            _openTradeDiagEntry = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                [Stop]("Engine disposed")   ' sets _running=False, cancels CTS, halts timer
                _timer?.Dispose()
                _cts?.Dispose()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
