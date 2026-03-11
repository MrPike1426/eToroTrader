Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.ML.Features
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
        ' Timestamp recorded when a position is confirmed open (after PlaceOrderAsync succeeds).
        ' Used to skip the portfolio close-check for the first 60 s so the eToro API has time
        ' to reflect the new position before we would mistakenly declare it closed.
        Private _positionOpenedAt As DateTimeOffset = DateTimeOffset.MinValue
        Private _lastApiPnl As Decimal = 0D     ' last broker-reported unrealised P&L; used as final P&L on close
        ' Cloud-edge SL price set by the MultiConfluence case; consumed once by PlaceBracketOrdersAsync.
        ' Nothing for all other strategy types.
        Private _mcCloudSlPrice As Decimal? = Nothing
        ' Absolute SL price for LULT Divergence — trigger wave extreme ± ATR-scaled tick buffer.
        ' Set when the 6-step LULT signal fires; Nothing for all other strategy types.
        Private _lultTriggerExtreme As Decimal? = Nothing
        ' Confirmed reversal requires ReversalConfirmBars consecutive NEW bars each
        ' producing an opposite-direction signal.  Bar-timestamp de-duplication prevents
        ' the 30-second timer from counting multiple checks of the same last bar as
        ' separate confirmation steps — only a genuine new completed bar advances the counter.
        Private Const ReversalConfirmBars As Integer = 2
        Private _currentTrendSide As OrderSide?          ' direction we are currently trading
        Private _reversalCandidateSide As OrderSide?     ' opposite side being confirmed
        Private _reversalConfirmCount As Integer = 0     ' consecutive new-bar opposite signals seen
        Private _lastBarTimestamp As DateTimeOffset = DateTimeOffset.MinValue

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
        Private _currentEma21 As Decimal = 0D          ' EMA21 from latest bar — drives pullback scale-in guard
        Private Const AtrSlMultiplier As Double = 1.5  ' SL = entry ± (1.5 × ATR)
        Private Const AtrTpMultiplier As Double = 3.0  ' TP = entry ± (3.0 × ATR)
        Private Const ScaleInPullbackTolerance As Double = 0.001  ' price must be within 0.1% of EMA21
        Private Const ScaleInBullThreshold As Integer = 80   ' bull score > this required for scale-in
        Private Const ScaleInBearThreshold As Integer = 20   ' bull score < this required for bear scale-in

        ' ── Stepped trailing bracket constants ──────────────────────────────────────
        ' Activates when profitPct ≥ 2.0%.  SL and TP advance in 0.5% steps.
        ' SL_profitPct = steppedProfit − 1.5  (free-ride once profitPct ≥ 2.0%).
        ' TP_profitPct = steppedProfit + tpAbove (tpAbove locked at activation).
        Private Shared ReadOnly TrailTriggerPct As Decimal = 2.0D   ' profit% threshold to arm trailing
        Private Shared ReadOnly TrailStepPct As Decimal = 0.5D      ' step size in profit%
        Private Shared ReadOnly TrailSlOffset As Decimal = 1.5D     ' SL = steppedProfit − TrailSlOffset
        Private Shared ReadOnly TrailDefaultTpAbove As Decimal = 2.0D ' default TP offset above steppedProfit

        ' ── Stepped trailing bracket state (reset per position) ─────────────────────
        Private _trailLastSteps As Integer = -1             ' −1 = not yet activated; ≥ 0 = last step count
        Private _trailTpAbove As Decimal = 2.0D            ' TP offset locked at activation (= TrailDefaultTpAbove)
        Private _trailTrackedSlPrice As Decimal = 0D       ' engine-monitored stepped SL price
        Private _trailTrackedTpPrice As Decimal = 0D       ' engine-monitored stepped TP price

        Public Sub New(ingestionService As BarIngestionService,
                       orderService As IOrderService,
                       riskGuard As IRiskGuardService,
                       logger As ILogger(Of StrategyExecutionEngine))
            _ingestionService = ingestionService
            _orderService = orderService
            _riskGuard = riskGuard
            _logger = logger
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
            _lastApiPnl = 0D
            _mcCloudSlPrice = Nothing
            _lultTriggerExtreme = Nothing
            _currentTrendSide = Nothing
            _reversalCandidateSide = Nothing
            _reversalConfirmCount = 0
            _lastBarTimestamp = DateTimeOffset.MinValue
            _extremeConfidenceDurationCount = 0
            _scaleInTradeCount = 0
            _currentAtrValue = 0D
            _currentEma21 = 0D
            ResetTrailState()
            _running = True
            _lastCheckedBarCount = 0   ' reset so bar-window is logged on first tick of this session
            _cts = New CancellationTokenSource()
            Interlocked.Exchange(_callbackRunning, 0)

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
                                     Dim startupSide = If(snapshot.IsBuy, OrderSide.Buy, OrderSide.Sell)
                                     RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(startupSide, strategy.ContractId, 100,
                                         snapshot.OpenedAtUtc, Nothing, snapshot.PositionId,
                                         snapshot.OpenedAtUtc, snapshot.Amount, snapshot.Leverage, snapshot.OpenRate))
                                     Log($"⚠️  Existing {snapshot.PositionCount} position(s) detected on startup (positionId={snapshot.PositionId}, entry={snapshot.OpenRate:F4}, units={snapshot.Units:F3}) — monitoring without placing new entry. Stepped trail active from ≥2% profit.")
                             Else
                                 Log($"✓ No existing positions for {strategy.ContractId} — ready to trade.")
                             End If
                         Catch ex As Exception
                             Log($"⚠️  Startup position check failed: {ex.Message} — assuming no open positions.")
                         End Try
                     End Function)

            _timer = New System.Threading.Timer(AddressOf TimerCallback, Nothing,
                                                TimeSpan.Zero, TimeSpan.FromSeconds(30))
        End Sub

        ''' <summary>Stop the engine and raise ExecutionStopped event.</summary>
        Public Sub [Stop](Optional reason As String = "Stopped by user")
            If Not _running Then Return
            _running = False
            _cts?.Cancel()                          ' cancel any in-flight API call immediately
            _timer?.Change(Timeout.Infinite, 0)     ' prevent future timer ticks
            ' RC-3: warn the user if positions are still open so they know the account is
            ' exposed between sessions.  Neutral-exit logic requires the engine to be running;
            ' on next Start() the startup position check (RC-2) will re-attach and the first
            ' tick will evaluate confidence and flatten if the band is neutral.
            If _positionOpen Then
                Log($"⚠️  POSITIONS STILL OPEN — {_strategy?.ContractId} has active positions. " &
                    $"Monitor manually or restart the engine to resume automated management.")
            End If
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
                    ' etoro TradingView Indicators.txt: "Strong Trend Criteria: ADX > 25".
                    ' A signal is only acted on when the market is in a trending phase.
                    ' ADX < 25 = ranging/consolidating market — suppress entry to avoid
                    ' false signals in sideways price action.
                    Dim dmiResult = TechnicalIndicators.DMI(highs, lows, closes)
                    Dim adxNow = TechnicalIndicators.LastValid(dmiResult.ADX)
                    Dim adxGatePassed = (adxNow >= 25.0F)

                    Dim atrVals = TechnicalIndicators.ATR(highs, lows, closes, 14)
                    _currentAtrValue = CDec(TechnicalIndicators.LastValid(atrVals))

                    ' Raise ConfidenceUpdated AFTER the ADX gate is known so the UI can
                    ' display the suppressed state (amber ⊘) instead of a misleading green arrow.
                    RaiseEvent ConfidenceUpdated(Me, New ConfidenceUpdatedEventArgs(CInt(upPct), CInt(downPct), adxGatePassed, CSng(adxNow), lastClose))

                    If Not adxGatePassed Then
                        Log($"Bar checked — ADX={adxNow:F1} < 25 (ranging market) — signal suppressed | EMA/RSI: UP={upPct:F0}% DOWN={downPct:F0}% | ATR={_currentAtrValue:F4} | {remStr}")
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
                    RaiseEvent ConfidenceUpdated(Me, New ConfidenceUpdatedEventArgs(mcResult.BullScore, mcResult.BearScore, adxValue:=mcResult.AdxValue, lastClose:=CDec(lastBar.Close)))

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
                        ' eToro portfolio API does not return a pnL field — calculate from
                        ' current bar close price across all aggregated position units.
                        Dim calculatedPnl = If(snapshot.Units > 0D,
                            Math.Round((CDec(lastBar.Close) - snapshot.OpenRate) * snapshot.Units *
                                       If(snapshot.IsBuy, 1D, -1D), 2), 0D)
                        _lastApiPnl = calculatedPnl
                        RaiseEvent PositionSynced(Me, New PositionSyncedEventArgs(
                            snapshot.PositionId, calculatedPnl, snapshot.OpenedAtUtc))
                    Else
                        ' No positions at all for this contract — closed by SL/TP, manual
                        ' action, or broker risk controls.  Force-close every in-progress UI row.
                        Dim closedCount = Math.Max(1, _openTradeCount)
                        Log($"⚠️  API reconciliation: no open positions for {_strategy.ContractId} — " &
                            $"force-closing {closedCount} UI trade row(s) (SL/TP/external close). " &
                            $"Final P&L={If(_lastApiPnl >= 0, "+", "")}${_lastApiPnl:F2}. Ready for next signal.")
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
                    End If
                End If
            End If

            ' ── Stepped trailing bracket — engine-tracked free-ride SL/TP ────────
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
                    ElseIf Not IsOrderingAllowed.Invoke() Then
                        Log($"⏸  {_strategy.ContractId} market CLOSED — monitoring only (no orders) | signal: {side.Value}")
                    Else
                        _positionOpen = True
                        Dim slArg As Decimal? = If(_strategy.Condition = StrategyConditionType.LultDivergence,
                                                   _lultTriggerExtreme, _mcCloudSlPrice)
                        Await PlaceBracketOrdersAsync(side.Value, lastBar.Close, slArg)
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

            ' ── Compute SL / TP absolute price levels from percentage ─────────────────
            ' eToro expects StopLossRate / TakeProfitRate as absolute prices ("rates") on the
            ' opening order — not tick offsets.  We derive them here from user-supplied %.
            '   Long:  SL = price × (1 − sl% / 100),  TP = price × (1 + tp% / 100)
            '   Short: SL = price × (1 + sl% / 100),  TP = price × (1 − tp% / 100)
            Dim slPriceVal As Decimal? = Nothing
            Dim tpPriceVal As Decimal? = Nothing
            If _currentAtrValue > 0D Then
                ' ATR-based dynamic levels: SL = 1.5×ATR, TP = 3×ATR from entry price
                Dim slDelta = Math.Round(_currentAtrValue * CDec(AtrSlMultiplier), 4)
                Dim tpDelta = Math.Round(_currentAtrValue * CDec(AtrTpMultiplier), 4)
                slPriceVal = Math.Round(If(side = OrderSide.Buy, priceUsed - slDelta, priceUsed + slDelta), 4)
                tpPriceVal = Math.Round(If(side = OrderSide.Buy, priceUsed + tpDelta, priceUsed - tpDelta), 4)
            Else
                ' Fallback: percentage-based levels from strategy settings
                If _strategy.StopLossPct > 0 Then
                    slPriceVal = Math.Round(
                        If(side = OrderSide.Buy,
                           priceUsed * (1D - _strategy.StopLossPct / 100D),
                           priceUsed * (1D + _strategy.StopLossPct / 100D)), 4)
                End If
                If _strategy.TakeProfitPct > 0 Then
                    tpPriceVal = Math.Round(
                        If(side = OrderSide.Buy,
                           priceUsed * (1D + _strategy.TakeProfitPct / 100D),
                           priceUsed * (1D - _strategy.TakeProfitPct / 100D)), 4)
                End If
            End If

            ' ── Enforce eToro minimum trade size ──────────────────────────────────────
            ' eToro requires the notional position value (cash × leverage) to be ≥ MinNotionalUsd.
            ' Minimum cash = MinNotionalUsd / leverage.
            ' Example: OIL min=$1000, leverage=1 → minCash=$1000;
            '          leverage=2 → minCash=$500; leverage=5 → minCash=$200.
            Dim leverage = If(_strategy.Leverage > 0, _strategy.Leverage, 1)
            Dim minNotional = If(fav IsNot Nothing, fav.MinNotionalUsd, 1000D)
            Dim minCash = minNotional / leverage
            Dim userAmount = _strategy.CapitalAtRisk
            Dim finalAmount = Math.Max(userAmount, minCash)
            Dim clamped = (finalAmount > userAmount)

            ' ── Absolute SL override — MultiConfluence (cloud edge) and LULT (trigger extreme) ──
            ' MultiConfluence: use cloud SL only when tighter than the ATR-based SL;
            '                  TP is recalculated at 2:1 R:R.
            ' LULT           : always apply trigger-wave SL; TP = 2:1 R:R.
            If cloudSlPrice.HasValue Then
                Dim cloudSlDist = Math.Abs(priceUsed - cloudSlPrice.Value)
                If _strategy.Condition = StrategyConditionType.LultDivergence Then
                    ' LULT: trigger-wave SL is always authoritative.
                    slPriceVal = Math.Round(cloudSlPrice.Value, 4)
                    tpPriceVal = Math.Round(
                        If(side = OrderSide.Buy,
                           priceUsed + cloudSlDist * 2D,
                           priceUsed - cloudSlDist * 2D), 4)
                ElseIf slPriceVal.HasValue Then
                    ' MultiConfluence: override only when cloud edge is tighter than ATR-based SL.
                    Dim atrSlDist = Math.Abs(priceUsed - slPriceVal.Value)
                    If cloudSlDist < atrSlDist Then
                        slPriceVal = cloudSlPrice.Value
                        tpPriceVal = Math.Round(
                            If(side = OrderSide.Buy,
                               priceUsed + cloudSlDist * 2D,
                               priceUsed - cloudSlDist * 2D), 4)
                    End If
                End If
            End If

            ' ── Structured audit log (emitted before submission) ──────────────────────
            Log($"📋 ORDER | instrId={instrId} side={side} leverage={leverage}x | " &
                $"user=${userAmount:F2} minCash=${minCash:F2} final=${finalAmount:F2}" &
                If(clamped, " (clamped to min ✓)", String.Empty))
            Dim slTpSource = If(_currentAtrValue > 0D, $"ATR={_currentAtrValue:F4} (1.5× / 3.0×)", "pct-based")
            Log($"📋 priceUsed={priceUsed:F4} | {slTpSource} | " &
                $"SL={If(slPriceVal.HasValue, slPriceVal.Value.ToString("F4"), "none")} | " &
                $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")}")

            ' ── Entry: by-amount Market order with native eToro SL/TP ─────────────────
            ' Using the by-amount endpoint (Amount + Leverage) rather than by-units so that
            ' CapitalAtRisk directly controls cash invested and the min-notional clamp applies.
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
                    $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")}")
            Catch ex As Exception
                Log($"⚠️  Entry order failed: {ex.Message}")
                _positionOpen = False
                Return
            End Try

            ' New position — reset stepped trailing state so the fresh entry is tracked cleanly.
            ResetTrailState()

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

            _positionOpen = False
            _openPositionId = Nothing
            _openTradeCount = 0
            _positionOpenedAt = DateTimeOffset.MinValue
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
        '''   2. No position open + signal fired → place initial trade.
        '''   3. Position open + extreme confidence in same direction → accumulate ticks,
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
                If _positionOpen Then
                    Log($"🔔 NEUTRAL CONFIDENCE — UP={rawUpPct}% DOWN={rawDownPct}% " &
                        $"(band: {NeutralConfidenceLow}–{NeutralConfidenceHigh}%) — flattening all positions immediately...")
                    Await DoNeutralFlattenAsync(ct)
                Else
                    Log($"Confidence neutral — UP={rawUpPct}% DOWN={rawDownPct}% | no open positions at broker — confidence exit skipped")
                End If
                Return
            End If

            ' ── 2. Initial trade placement (no position open yet) ─────────────────
            If Not _positionOpen Then
                If side.HasValue Then
                    If Not IsOrderingAllowed.Invoke() Then
                        Log($"⏸  {_strategy.ContractId} market CLOSED — monitoring only (no orders) | UP={rawUpPct}% DOWN={rawDownPct}% signal={side.Value}")
                    Else
                        Log($"🎯 INITIAL TRADE — {side.Value} | Confidence: UP={rawUpPct}% DOWN={rawDownPct}%")
                        _positionOpen = True
                        Await PlaceBracketOrdersAsync(side.Value, lastClose)
                        _extremeConfidenceDurationCount = 0   ' start fresh counter for scale-in window
                    End If
                End If
                Return
            End If

            ' ── 3. Position is open — evaluate extreme confidence for scale-in ─────
            ' Scale-in requires bull score > 80 AND price within 0.1% of EMA21
            ' (buying the pullback to the fast EMA, not chasing a new peak).
            Dim withinPullback = _currentEma21 > 0D AndAlso
                Math.Abs(lastClose - _currentEma21) / _currentEma21 <= CDec(ScaleInPullbackTolerance)
            Dim isExtremeBull = (rawUpPct > ScaleInBullThreshold AndAlso withinPullback)
            Dim isExtremeBear = (rawUpPct < ScaleInBearThreshold AndAlso withinPullback)

            If Not isExtremeBull AndAlso Not isExtremeBear Then
                If _extremeConfidenceDurationCount > 0 Then
                    Dim ema21Dist = If(_currentEma21 > 0D,
                                       $" (EMA21 dist={Math.Abs(lastClose - _currentEma21) / _currentEma21 * 100D:F3}%, need ≤0.1%)",
                                       String.Empty)
                    Log($"Scale-in blocked — UP={rawUpPct}% DOWN={rawDownPct}%{ema21Dist} | timer reset")
                End If
                _extremeConfidenceDurationCount = 0
                Return
            End If

            Dim extremeSide As OrderSide = If(isExtremeBull, OrderSide.Buy, OrderSide.Sell)

            ' Direction must match the established trend — no cross-contamination
            If _currentTrendSide.HasValue AndAlso _currentTrendSide.Value <> extremeSide Then
                If _extremeConfidenceDurationCount > 0 OrElse _scaleInTradeCount > 0 Then
                    Log($"Scale-in direction mismatch — extreme={extremeSide} but trend={_currentTrendSide.Value} | counters reset")
                End If
                _extremeConfidenceDurationCount = 0
                _scaleInTradeCount = 0
                Return
            End If

            ' Increment the consecutive extreme-confidence counter on NEW BARS only (TICKET-021).
            ' The reversal counter uses the same isNewBar guard for the same reason: multiple
            ' 30-second timer ticks can see the same last bar, and counting them separately
            ' would allow all 3 scale-ins to fire within a single 5-minute candle.
            If isNewBar Then
                _extremeConfidenceDurationCount += 1
                Log($"⏱  Extreme confidence bar {_extremeConfidenceDurationCount}/{ScaleInRequiredTicks} — " &
                    $"UP={rawUpPct}% DOWN={rawDownPct}% | {extremeSide} | pullback to EMA21 ✓ | " &
                    $"scale-in {_scaleInTradeCount}/{MaxScaleInTrades}")
            Else
                Log($"⏱  Extreme confidence (same bar, tick skipped) — " &
                    $"UP={rawUpPct}% DOWN={rawDownPct}% | {extremeSide} | " &
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
            Dim slPriceVal As Decimal? = Nothing
            Dim tpPriceVal As Decimal? = Nothing
            If _currentAtrValue > 0D Then
                ' ATR-based dynamic levels (same multipliers as initial entry)
                Dim slDelta = Math.Round(_currentAtrValue * CDec(AtrSlMultiplier), 4)
                Dim tpDelta = Math.Round(_currentAtrValue * CDec(AtrTpMultiplier), 4)
                slPriceVal = Math.Round(If(side = OrderSide.Buy, priceUsed - slDelta, priceUsed + slDelta), 4)
                tpPriceVal = Math.Round(If(side = OrderSide.Buy, priceUsed + tpDelta, priceUsed - tpDelta), 4)
            Else
                If _strategy.StopLossPct > 0 Then
                    slPriceVal = Math.Round(
                        If(side = OrderSide.Buy,
                           priceUsed * (1D - _strategy.StopLossPct / 100D),
                           priceUsed * (1D + _strategy.StopLossPct / 100D)), 4)
                End If
                If _strategy.TakeProfitPct > 0 Then
                    tpPriceVal = Math.Round(
                        If(side = OrderSide.Buy,
                           priceUsed * (1D + _strategy.TakeProfitPct / 100D),
                           priceUsed * (1D - _strategy.TakeProfitPct / 100D)), 4)
                End If
            End If

            ' Enforce eToro minimum notional for scale-in leverage
            Dim minNotional = If(fav IsNot Nothing, fav.MinNotionalUsd, 1000D)
            Dim minCash = minNotional / _strategy.ScaleInLeverage
            Dim finalAmount = Math.Max(_strategy.ScaleInAmount, minCash)
            Dim clamped = (finalAmount > _strategy.ScaleInAmount)

            Log($"📋 SCALE-IN ORDER {scaleIndex}/{MaxScaleInTrades} | instrId={instrId} side={side} leverage={_strategy.ScaleInLeverage}x | " &
                $"amount=${_strategy.ScaleInAmount:F0} final=${finalAmount:F2}" & If(clamped, " (clamped to min ✓)", String.Empty))
            Dim slTpSourceSi = If(_currentAtrValue > 0D, $"ATR={_currentAtrValue:F4} (1.5× / 3.0×)", "pct-based")
            Log($"📋 priceUsed={priceUsed:F4} | {slTpSourceSi} | " &
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
                    $"TP={If(tpPriceVal.HasValue, tpPriceVal.Value.ToString("F4"), "none")}")
            Catch ex As Exception
                Log($"⚠️  Scale-in {scaleIndex}/{MaxScaleInTrades} order failed: {ex.Message}")
                Return
            End Try

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

            _positionOpen = False
            _openPositionId = Nothing
            _openTradeCount = 0
            _positionOpenedAt = DateTimeOffset.MinValue
            _lastApiPnl = 0D
            _extremeConfidenceDurationCount = 0
            _scaleInTradeCount = 0
            _currentTrendSide = Nothing
            _reversalCandidateSide = Nothing
            _reversalConfirmCount = 0
            ResetTrailState()
        End Function

        ' ── Stepped trailing bracket methods ──────────────────────────────────────

        ''' <summary>
        ''' Engine-side stepped trailing bracket (free-ride SL/TP).
        ''' Activates when profitPct ≥ 2.0%.  Advances tracked SL/TP in 0.5% steps.
        ''' Closes the position and fires TradeClosed when the current bar price breaches
        ''' the tracked SL or reaches the tracked TP.
        ''' Returns True when the position was closed this tick so the caller can return early.
        ''' </summary>
        Private Async Function ApplySteppedTrailAsync(currentPrice As Decimal,
                                                      ct As CancellationToken) As Task(Of Boolean)
            If _lastEntryPrice <= 0D Then Return False

            ' Profit % relative to the primary entry price
            Dim profitPct As Decimal =
                If(_lastEntrySide = OrderSide.Buy,
                   (currentPrice - _lastEntryPrice) / _lastEntryPrice * 100D,
                   (_lastEntryPrice - currentPrice) / _lastEntryPrice * 100D)

            ' ── Check whether a tracked level has been breached ─────────────────
            If _trailLastSteps >= 0 Then
                Dim slBreached = If(_lastEntrySide = OrderSide.Buy,
                                    currentPrice <= _trailTrackedSlPrice,
                                    currentPrice >= _trailTrackedSlPrice)
                Dim tpReached = (_trailTrackedTpPrice > 0D) AndAlso
                                If(_lastEntrySide = OrderSide.Buy,
                                   currentPrice >= _trailTrackedTpPrice,
                                   currentPrice <= _trailTrackedTpPrice)

                If slBreached OrElse tpReached Then
                    Dim reason = If(slBreached, "Trail SL", "Trail TP")
                    Log($"🛑 {reason} HIT — price={currentPrice:F4} " &
                        $"SL={_trailTrackedSlPrice:F4} TP={_trailTrackedTpPrice:F4} " &
                        $"profit={profitPct:F2}% — closing position")
                    Await _orderService.FlattenContractAsync(_strategy.AccountId, _strategy.ContractId, ct)
                    Dim closedCount = Math.Max(1, _openTradeCount)
                    Dim closePnl = _lastApiPnl
                    For i As Integer = 1 To closedCount
                        RaiseEvent TradeClosed(Me, New TradeClosedEventArgs(reason, closePnl))
                        closePnl = 0D
                    Next
                    _positionOpen = False
                    _openPositionId = Nothing
                    _openTradeCount = 0
                    _positionOpenedAt = DateTimeOffset.MinValue
                    _lastApiPnl = 0D
                    ResetTrailState()
                    Return True
                End If
            End If

            ' ── Not yet triggered — check if trailing should activate or step ────
            If profitPct < TrailTriggerPct Then Return False

            Dim steps = CInt(Math.Floor(CDbl(profitPct - TrailTriggerPct) / CDbl(TrailStepPct)))
            Dim steppedProfit = TrailTriggerPct + steps * TrailStepPct

            ' ── Activate on first reach of trigger ───────────────────────────────
            If _trailLastSteps = -1 Then
                ' Lock tpAbove at activation: preserve existing TP offset when one was set,
                ' otherwise default to 2.0% above steppedProfit.
                If _lastTpPrice > 0D Then
                    Dim existingTpPct As Decimal =
                        If(_lastEntrySide = OrderSide.Buy,
                           (_lastTpPrice - _lastEntryPrice) / _lastEntryPrice * 100D,
                           (_lastEntryPrice - _lastTpPrice) / _lastEntryPrice * 100D)
                    Dim computed = existingTpPct - steppedProfit
                    _trailTpAbove = If(computed > 0D, computed, TrailDefaultTpAbove)
                Else
                    _trailTpAbove = TrailDefaultTpAbove
                End If
                _trailLastSteps = steps
                UpdateTrailLevels(steppedProfit)
                Log($"🔰 TRAIL ARMED — profit={profitPct:F2}% step={steps} " &
                    $"steppedProfit={steppedProfit:F2}% " &
                    $"SL={_trailTrackedSlPrice:F4} (+{steppedProfit - TrailSlOffset:F2}%) " &
                    $"TP={_trailTrackedTpPrice:F4} (+{steppedProfit + _trailTpAbove:F2}%)")
                Await PushTrailToAllPositionsAsync(ct)
                Return False
            End If

            ' ── Advance step only when profit crosses the next 0.5% threshold ─────
            If steps <= _trailLastSteps Then Return False

            _trailLastSteps = steps
            UpdateTrailLevels(steppedProfit)
            Log($"⬆  TRAIL STEP {steps} — profit={profitPct:F2}% " &
                $"steppedProfit={steppedProfit:F2}% " &
                $"SL={_trailTrackedSlPrice:F4} (+{steppedProfit - TrailSlOffset:F2}%) " &
                $"TP={_trailTrackedTpPrice:F4} (+{steppedProfit + _trailTpAbove:F2}%)")
            Await PushTrailToAllPositionsAsync(ct)
            Return False
        End Function

        ''' <summary>
        ''' Pushes the current engine-tracked SL/TP to every live position on the broker.
        ''' Called whenever the stepped trail arms or advances a step so the broker-side
        ''' SL/TP is kept in sync — positions remain protected if the engine is stopped.
        ''' </summary>
        Private Async Function PushTrailToAllPositionsAsync(ct As CancellationToken) As Task
            Dim slToSend As Decimal? = If(_trailTrackedSlPrice > 0D, _trailTrackedSlPrice, CType(Nothing, Decimal?))
            Dim tpToSend As Decimal? = If(_trailTrackedTpPrice > 0D, _trailTrackedTpPrice, CType(Nothing, Decimal?))
            Dim liveOrders = Await _orderService.GetLiveWorkingOrdersAsync(_strategy.AccountId, _strategy.ContractId, ct)
            For Each pos In liveOrders
                Dim posId = If(pos.ExternalPositionId, pos.ExternalOrderId)
                If Not posId.HasValue Then Continue For
                Dim ok = Await _orderService.EditPositionSlTpAsync(posId.Value, slToSend, tpToSend, ct)
                If ok Then
                    Log($"✅ Trail SL/TP sent to broker — positionId={posId.Value} " &
                        $"SL={If(slToSend.HasValue, slToSend.Value.ToString("F4"), "none")} " &
                        $"TP={If(tpToSend.HasValue, tpToSend.Value.ToString("F4"), "none")}")
                Else
                    Log($"⚠️  Failed to push trail SL/TP to broker for positionId={posId.Value} — engine-side monitoring still active")
                End If
            Next
        End Function

        ''' <summary>
        ''' Recomputes the tracked SL and TP absolute prices from the latest stepped profit level.
        ''' Never loosens: for Long, prices only increase; for Short, prices only decrease.
        ''' </summary>
        Private Sub UpdateTrailLevels(steppedProfit As Decimal)
            Dim slProfitPct = steppedProfit - TrailSlOffset
            Dim tpProfitPct = steppedProfit + _trailTpAbove

            If _lastEntrySide = OrderSide.Buy Then
                Dim newSl = Math.Round(_lastEntryPrice * (1D + slProfitPct / 100D), 4)
                Dim newTp = Math.Round(_lastEntryPrice * (1D + tpProfitPct / 100D), 4)
                ' Never loosen — for Long, a higher price is tighter
                _trailTrackedSlPrice = If(_trailTrackedSlPrice = 0D, newSl, Math.Max(_trailTrackedSlPrice, newSl))
                _trailTrackedTpPrice = If(_trailTrackedTpPrice = 0D, newTp, Math.Max(_trailTrackedTpPrice, newTp))
            Else
                Dim newSl = Math.Round(_lastEntryPrice * (1D - slProfitPct / 100D), 4)
                Dim newTp = Math.Round(_lastEntryPrice * (1D - tpProfitPct / 100D), 4)
                ' Never loosen — for Short, a lower price is tighter
                _trailTrackedSlPrice = If(_trailTrackedSlPrice = 0D, newSl, Math.Min(_trailTrackedSlPrice, newSl))
                _trailTrackedTpPrice = If(_trailTrackedTpPrice = 0D, newTp, Math.Min(_trailTrackedTpPrice, newTp))
            End If
        End Sub

        ''' <summary>Resets all stepped trailing state. Called on position open, close, reversal, and flatten.</summary>
        Private Sub ResetTrailState()
            _trailLastSteps = -1
            _trailTpAbove = TrailDefaultTpAbove
            _trailTrackedSlPrice = 0D
            _trailTrackedTpPrice = 0D
        End Sub

        Private Sub Log(message As String)
            Dim timestamped = $"{DateTime.Now:HH:mm:ss}  {message}"
            _logger.LogInformation("[StrategyEngine] {Msg}", message)
            RaiseEvent LogMessage(Me, timestamped)
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
