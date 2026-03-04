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
        Private ReadOnly _logger As ILogger(Of StrategyExecutionEngine)

        ' ── State ─────────────────────────────────────────────────────────────────
        Private _strategy As StrategyDefinition
        Private _timer As System.Threading.Timer
        Private _cts As CancellationTokenSource       ' cancelled by Stop() / Dispose()
        Private _callbackRunning As Integer = 0       ' Interlocked reentrancy guard
        Private _positionOpen As Boolean = False
        Private _disposed As Boolean = False
        Private _running As Boolean = False

        ' ── Events ────────────────────────────────────────────────────────────────
        ''' <summary>Raised on the thread-pool whenever a log line is produced.</summary>
        Public Event LogMessage As EventHandler(Of String)
        ''' <summary>Raised when the engine stops (expired, stopped, or errored).</summary>
        Public Event ExecutionStopped As EventHandler(Of String)
        ''' <summary>Raised when an entry order is placed (trade opened).</summary>
        Public Event TradeOpened As EventHandler(Of TradeOpenedEventArgs)
        ''' <summary>Raised when the bracket position closes (TP or SL filled).</summary>
        Public Event TradeClosed As EventHandler(Of TradeClosedEventArgs)

        ' ── Trade-tracking state (for performance panel) ──────────────────────────
        Private _lastEntryPrice As Decimal = 0D
        Private _lastEntrySide As OrderSide = OrderSide.Buy
        Private _lastConfidencePct As Integer = 0
        Private _lastTpPrice As Decimal = 0D
        Private _lastSlPrice As Decimal = 0D
        Private _lastTpExternalId As Long? = Nothing
        Private _pendingConfidencePct As Integer = 0

        Public Sub New(ingestionService As BarIngestionService,
                       orderService As IOrderService,
                       logger As ILogger(Of StrategyExecutionEngine))
            _ingestionService = ingestionService
            _orderService = orderService
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
            _running = True
            _cts = New CancellationTokenSource()
            Interlocked.Exchange(_callbackRunning, 0)

            Log($"Strategy started — {strategy.ContractId} | {strategy.Name}")
            Log($"Duration: {strategy.DurationHours}hrs | Expires: {strategy.ExpiresAt:HH:mm} UTC")
            Log($"Checking bars every 30 seconds...")

            _timer = New System.Threading.Timer(AddressOf TimerCallback, Nothing,
                                                TimeSpan.Zero, TimeSpan.FromSeconds(30))
        End Sub

        ''' <summary>Stop the engine and raise ExecutionStopped event.</summary>
        Public Sub [Stop](Optional reason As String = "Stopped by user")
            If Not _running Then Return
            _running = False
            _cts?.Cancel()                          ' cancel any in-flight API call immediately
            _timer?.Change(Timeout.Infinite, 0)     ' prevent future timer ticks
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

            ' First try to ingest fresh bars (no-op if already up to date)
            Await _ingestionService.IngestAsync(_strategy.ContractId, timeframe, 50, ct)

            Dim bars = Await _ingestionService.GetBarsForMLAsync(_strategy.ContractId, timeframe, 60, ct)
            Dim minBars = _strategy.IndicatorPeriod + 5

            If bars Is Nothing OrElse bars.Count < minBars Then
                Dim barCount = If(bars Is Nothing, 0, bars.Count)
                If barCount = 0 Then
                    Log($"No bars returned for '{_strategy.ContractId}' — market may be closed or outside trading hours. Retrying… ({remStr})")
                Else
                    Log($"Waiting for bars — have {barCount}/{minBars} needed ({remStr})")
                End If
                Return
            End If

            ' ── Evaluate condition ────────────────────────────────────────────────
            Dim closes = bars.Select(Function(b) b.Close).ToList()
            Dim highs = bars.Select(Function(b) b.High).ToList()
            Dim lows = bars.Select(Function(b) b.Low).ToList()

            Dim lastBar = bars.Last()
            Dim side As OrderSide? = Nothing

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

                    If _strategy.Condition = StrategyConditionType.RSIOversold Then
                        If _strategy.GoLongWhenBelowBands AndAlso rsiVal < 30 Then
                            Log($"✅ RSI oversold! RSI={rsiVal:F1} < 30")
                            side = OrderSide.Buy
                        ElseIf _strategy.GoShortWhenAboveBands AndAlso rsiVal > 70 Then
                            Log($"✅ RSI overbought! RSI={rsiVal:F1} > 70")
                            side = OrderSide.Sell
                        Else
                            Log($"Bar checked — RSI={rsiVal:F1} | no signal ({remStr})")
                        End If
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

                    ' Accumulate bull score (max 100)
                    Dim bullScore As Double = 0

                    ' 1. EMA21 vs EMA50 crossover — 25%
                    If ema21Now > ema50Now Then bullScore += 25

                    ' 2. Price vs EMA21 — 20%
                    If lastClose > CDec(ema21Now) Then bullScore += 20

                    ' 3. Price vs EMA50 — 15%
                    If lastClose > CDec(ema50Now) Then bullScore += 15

                    ' 4. RSI gradient — 20%  (oversold=bullish, overbought=bearish)
                    Dim rsiScore As Double
                    If rsiVal <= 30 Then
                        rsiScore = 20
                    ElseIf rsiVal >= 70 Then
                        rsiScore = 0
                    Else
                        rsiScore = (70.0 - rsiVal) / 40.0 * 20.0
                    End If
                    bullScore += rsiScore

                    ' 5. EMA21 momentum — 10%
                    If ema21Now > ema21Prev Then bullScore += 10

                    ' 6. Recent 3 candles — 10%  (majority green = bullish)
                    Dim lastThree = bars.Skip(bars.Count - 3).ToList()
                    Dim bullCandles = lastThree.Where(Function(b) b.Close >= b.Open).Count()
                    If bullCandles >= 2 Then bullScore += 10

                    Dim upPct As Double = bullScore
                    Dim downPct As Double = 100 - bullScore
                    Dim minPct As Integer = _strategy.MinConfidencePct  ' user-set threshold (default 75)

                    If upPct >= minPct Then
                        _pendingConfidencePct = CInt(upPct)
                        Log($"✅ EMA/RSI weighted: UP={upPct:F0}% ≥ {minPct}% — LONG signal! Close={lastClose:F2} EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1}")
                        side = OrderSide.Buy
                    ElseIf downPct >= minPct Then
                        _pendingConfidencePct = CInt(downPct)
                        Log($"✅ EMA/RSI weighted: DOWN={downPct:F0}% ≥ {minPct}% — SHORT signal! Close={lastClose:F2} EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1}")
                        side = OrderSide.Sell
                    Else
                        Log($"Bar checked — EMA/RSI: UP={upPct:F0}% DOWN={downPct:F0}% | no signal (need ≥{minPct}%) | EMA21={ema21Now:F2} EMA50={ema50Now:F2} RSI={rsiVal:F1} | {remStr}")
                    End If

                Case Else
                    Log($"Condition '{_strategy.Condition}' not yet implemented")
            End Select

            ' ── Reset position flag when position has closed ────────────────────
            ' PlaceBracketOrdersAsync sets _positionOpen=True but the TP/SL fills happen
            ' externally on the exchange.  The local DB is NEVER updated when orders fill
            ' or cancel — only the TopStepX API has ground truth.
            ' Poll the API: if no Working orders remain for this contract, the bracket
            ' position has closed (TP filled / SL filled / both cancelled).
            If _positionOpen Then
                Dim liveOrders = Await _orderService.GetLiveWorkingOrdersAsync(
                    _strategy.AccountId, _strategy.ContractId, _cts.Token)
                Dim stillOpen = liveOrders.Any()
                If Not stillOpen Then
                    _positionOpen = False
                    Log($"✓ Position closed — no live Working orders on exchange. Ready for next signal.")

                    ' Determine TP vs SL and calculate P&L for the performance panel.
                    ' Check if the TP order filled via API; if not, assume SL.
                    Dim exitReason As String = "Closed"
                    Dim closePnl As Decimal = 0D
                    Try
                        If _lastTpExternalId.HasValue Then
                            Dim tpFill = Await _orderService.TryGetOrderFillPriceAsync(
                                _lastTpExternalId.Value, _strategy.AccountId)
                            If tpFill.HasValue Then
                                exitReason = "TP"
                                Dim priceMove = If(_lastEntrySide = OrderSide.Buy,
                                                   tpFill.Value - _lastEntryPrice,
                                                   _lastEntryPrice - tpFill.Value)
                                closePnl = priceMove / _strategy.TickSize * _strategy.TickValue * _strategy.Quantity
                            Else
                                ' TP not filled → SL must have triggered; use the known SL price
                                exitReason = "SL"
                                Dim priceMove = If(_lastEntrySide = OrderSide.Buy,
                                                   _lastSlPrice - _lastEntryPrice,
                                                   _lastEntryPrice - _lastSlPrice)
                                closePnl = priceMove / _strategy.TickSize * _strategy.TickValue * _strategy.Quantity
                            End If
                        End If
                    Catch
                        ' Non-critical: P&L will show 0 if lookup fails
                    End Try
                    RaiseEvent TradeClosed(Me, New TradeClosedEventArgs(exitReason, closePnl))
                End If
            End If

            ' ── Place orders if condition met ─────────────────────────────────────
            If side.HasValue AndAlso Not _positionOpen Then
                _positionOpen = True
                Await PlaceBracketOrdersAsync(side.Value, lastBar.Close)
            End If
        End Function

        Private Async Function PlaceBracketOrdersAsync(side As OrderSide, lastClose As Decimal) As Task
            ' ── Entry: Market order ───────────────────────────────────────────────
            Dim entryOrder As New Order With {
                .AccountId = _strategy.AccountId,
                .ContractId = _strategy.ContractId,
                .Side = side,
                .OrderType = OrderType.Market,
                .Quantity = _strategy.Quantity,
                .Status = OrderStatus.Pending,
                .PlacedAt = DateTimeOffset.UtcNow,
                .Notes = $"AI Strategy: {_strategy.Name}"
            }

            Try
                Await _orderService.PlaceOrderAsync(entryOrder)
                Log($"Entry {side} order placed — Market, qty={_strategy.Quantity}")
            Catch ex As Exception
                Log($"⚠️  Entry order failed: {ex.Message}")
                _positionOpen = False
                Return
            End Try

            ' Use last close as approximate entry price for bracket calculations
            Dim entryPrice = lastClose
            Dim tick = If(_strategy.TickSize > 0, _strategy.TickSize, 1D)

            ' Save entry context for P&L and performance panel
            _lastEntryPrice = entryPrice
            _lastEntrySide = side
            _lastConfidencePct = _pendingConfidencePct
            _lastTpExternalId = Nothing

            ' Raise TradeOpened so the performance panel row appears immediately
            RaiseEvent TradeOpened(Me, New TradeOpenedEventArgs(side, _strategy.ContractId,
                                                                _lastConfidencePct,
                                                                DateTimeOffset.UtcNow,
                                                                entryOrder.ExternalOrderId))

            ' ── Take Profit: Limit order ──────────────────────────────────────────
            If _strategy.TakeProfitTicks > 0 Then
                Dim tpOffset = _strategy.TakeProfitTicks * tick
                Dim tpPrice = If(side = OrderSide.Buy, entryPrice + tpOffset, entryPrice - tpOffset)

                Dim tpOrder As New Order With {
                    .AccountId = _strategy.AccountId,
                    .ContractId = _strategy.ContractId,
                    .Side = If(side = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy), ' opposite
                    .OrderType = OrderType.Limit,
                    .Quantity = _strategy.Quantity,
                    .LimitPrice = tpPrice,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"AI Strategy TP: {_strategy.Name}"
                }

                Try
                    Dim placed = Await _orderService.PlaceOrderAsync(tpOrder)
                    If placed.Status = OrderStatus.Rejected Then
                        Log($"⚠️  Take Profit REJECTED by API @ {tpPrice:F2}")
                    Else
                        _lastTpExternalId = placed.ExternalOrderId   ' saved for close detection
                        _lastTpPrice = tpPrice
                        Log($"Take Profit Limit placed @ {tpPrice:F2} (+{_strategy.TakeProfitTicks} ticks)")
                    End If
                Catch ex As Exception
                    Log($"⚠️  Take Profit order failed: {ex.Message}")
                End Try
            End If

            ' ── Stop Loss: StopLimit order ─────────────────────────────────────────
            ' UAT-BUG-008: ProjectX API rejects type=3 (Stop-Market) orders.
            ' Use type=4 (StopLimit) instead: the stop price triggers the order and the
            ' limit price (5 ticks of slippage beyond the stop) ensures it fills.
            ' For a Buy SL: stop triggers if price FALLS to slPrice; limit is 5 ticks lower.
            ' For a Sell SL: stop triggers if price RISES to slPrice; limit is 5 ticks higher.
            If _strategy.StopLossTicks > 0 Then
                Dim slOffset = _strategy.StopLossTicks * tick
                Dim slPrice = If(side = OrderSide.Buy, entryPrice - slOffset, entryPrice + slOffset)
                Dim slippage = 5 * tick   ' 5-tick limit buffer beyond stop to ensure fill
                Dim slLimit = If(side = OrderSide.Buy, slPrice - slippage, slPrice + slippage)

                Dim slOrder As New Order With {
                    .AccountId = _strategy.AccountId,
                    .ContractId = _strategy.ContractId,
                    .Side = If(side = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy), ' opposite
                    .OrderType = OrderType.StopLimit,
                    .Quantity = _strategy.Quantity,
                    .StopPrice = slPrice,
                    .LimitPrice = slLimit,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"AI Strategy SL: {_strategy.Name}"
                }

                Try
                    Dim placed = Await _orderService.PlaceOrderAsync(slOrder)
                    If placed.Status = OrderStatus.Rejected Then
                        Log($"⚠️  Stop Loss REJECTED by API @ {slPrice:F2} — position is UNPROTECTED!")
                        _logger.LogError("SL order rejected for {Contract} — no stop loss active", _strategy.ContractId)
                    Else
                        _lastSlPrice = slPrice
                        Log($"Stop Loss (StopLimit) placed @ {slPrice:F2} limit {slLimit:F2} (-{_strategy.StopLossTicks} ticks)")
                    End If
                Catch ex As Exception
                    Log($"⚠️  Stop Loss order failed: {ex.Message}")
                End Try
            End If

            Log($"Bracket orders placed. Position open — engine will continue monitoring.")
        End Function

        ' ── Helpers ───────────────────────────────────────────────────────────────

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
