Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Entities
Imports TopStepTrader.Data.Repositories
Imports TopStepTrader.ML.Features

Namespace TopStepTrader.Services.Backtest

    ''' <summary>
    ''' Walk-forward backtest engine. Replays historical bars through the same EMA/RSI
    ''' weighted-scoring algorithm used by StrategyExecutionEngine (live trading), so that
    ''' backtest results represent what live trading will actually produce.
    '''
    ''' Signal algorithm (mirrors StrategyExecutionEngine.EmaRsiWeightedScore):
    '''   Six signals scored 0–100; fire Long when bullScore ≥ threshold, Short when bearScore ≥ threshold.
    '''   1. EMA21 > EMA50 crossover — 25 pts
    '''   2. Close > EMA21 — 20 pts
    '''   3. Close > EMA50 — 15 pts
    '''   4. RSI14 gradient (oversold=bullish, overbought=bearish) — up to 20 pts
    '''   5. EMA21 momentum (rising since prior bar) — 10 pts
    '''   6. 2+ of last 3 candles bullish — 10 pts
    '''
    ''' Pure calculation logic lives in <see cref="BacktestMetrics"/> (Friend module)
    ''' so it can be unit-tested independently.
    ''' </summary>
    Public Class BacktestEngine
        Implements IBacktestService

        Private ReadOnly _barRepository As BarRepository
        Private ReadOnly _backtestRepository As BacktestRepository
        Private ReadOnly _logger As ILogger(Of BacktestEngine)

        Public Event ProgressUpdated As EventHandler(Of BacktestProgressEventArgs) _
            Implements IBacktestService.ProgressUpdated

        Public Sub New(barRepository As BarRepository,
                       backtestRepository As BacktestRepository,
                       logger As ILogger(Of BacktestEngine))
            _barRepository = barRepository
            _backtestRepository = backtestRepository
            _logger = logger
        End Sub

        Public Async Function RunBacktestAsync(config As BacktestConfiguration,
                                                cancel As CancellationToken) _
            As Task(Of BacktestResult) Implements IBacktestService.RunBacktestAsync

            _logger.LogInformation("Starting backtest '{Name}' from {Start} to {End}",
                                   config.RunName, config.StartDate, config.EndDate)

            ' Load bars for the configured date range — GetBarsAsync returns domain MarketBar objects
            Dim from As DateTimeOffset = New DateTimeOffset(config.StartDate, TimeSpan.Zero)
            Dim [to] As DateTimeOffset = New DateTimeOffset(config.EndDate, TimeSpan.Zero).AddDays(1)
            Dim filteredBars = Await _barRepository.GetBarsAsync(
                config.ContractId, CType(config.Timeframe, BarTimeframe), from, [to], cancel)

            If filteredBars.Count < 50 Then
                Throw New InvalidOperationException(
                    $"Insufficient bars for backtest: {filteredBars.Count}. Need at least 50.")
            End If

            _logger.LogInformation("Replaying {N} bars", filteredBars.Count)

            ' ── Pre-calculate full indicator series ONCE for all bars ────────────
            ' This mirrors how the live engine works: EMA/RSI carries full price history,
            ' not a truncated window.  Much more accurate and efficient than per-bar recalc.
            Dim allCloses = filteredBars.Select(Function(b) b.Close).ToList()
            Dim ema21Series = TechnicalIndicators.EMA(allCloses, 21)  ' valid from index 20
            Dim ema50Series = TechnicalIndicators.EMA(allCloses, 50)  ' valid from index 49
            Dim rsi14Series = TechnicalIndicators.RSI(allCloses, 14)  ' valid from index 14

            ' EMA8 only needed for Sniper (TripleEmaCascade) strategy.
            Dim ema8Series As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.TripleEmaCascade Then
                ema8Series = TechnicalIndicators.EMA(allCloses, 8)    ' valid from index 7
            End If

            ' Warm-up: EMA50 needs 50 bars; add 5-bar buffer so EMA21Prev also valid.
            ' Bars before this index simply have no signal — same as live engine startup.
            Dim warmUp = 55

            Dim trades As New List(Of BacktestTrade)()
            Dim capital = config.InitialCapital
            Dim peakCapital = capital
            Dim maxDrawdown = 0D
            Dim openTrade As BacktestTrade = Nothing

            ' MinSignalConfidence is stored as 0.0–1.0 (e.g. 0.75 = 75%).
            ' The EMA/RSI score produces 0–100, so convert once here.
            Dim minPct As Double = config.MinSignalConfidence * 100.0

            Dim progressStep = Math.Max(1, CInt(filteredBars.Count / 20))

            For i = warmUp To filteredBars.Count - 1
                cancel.ThrowIfCancellationRequested()

                ' Progress events every ~5%
                If i Mod progressStep = 0 Then
                    Dim pct = CInt((i / CDbl(filteredBars.Count)) * 100)
                    RaiseEvent ProgressUpdated(Me, New BacktestProgressEventArgs(
                        pct, filteredBars(i).Timestamp.Date, trades.Count))
                End If

                Dim bar = filteredBars(i)

                ' ── Check exit for open trade ──────────────────────────────────
                If openTrade IsNot Nothing Then
                    Dim exitReason = BacktestMetrics.CheckExit(openTrade, bar, config)
                    If exitReason IsNot Nothing Then
                        openTrade.ExitTime = bar.Timestamp
                        ' UAT-BUG-006: use the exact SL/TP level price, not bar.Close.
                        ' CheckExit detects triggers via bar.High/Low (OHLC); if exit price
                        ' were set to bar.Close the bar could close on the profitable side of
                        ' entry even though a StopLoss was hit intrabar, yielding a "StopLoss"
                        ' trade with positive P&L — physically impossible.
                        openTrade.ExitPrice = BacktestMetrics.GetExitPrice(openTrade, bar, exitReason, config)
                        openTrade.ExitReason = exitReason
                        Dim pnl = BacktestMetrics.CalculatePnL(openTrade, config)
                        openTrade.PnL = pnl
                        capital += pnl
                        If capital > peakCapital Then peakCapital = capital
                        Dim dd = peakCapital - capital
                        If dd > maxDrawdown Then maxDrawdown = dd
                        trades.Add(openTrade)
                        openTrade = Nothing
                    End If
                End If

                ' ── Signal evaluation — Only when flat (no open trade) ─────────────
                ' Branches on config.StrategyCondition: EmaRsiWeightedScore or TripleEmaCascade.

                ' ── 3-EMA Cascade (Sniper) signal ─────────────────────────────────
                If openTrade Is Nothing AndAlso
                   config.StrategyCondition = StrategyConditionType.TripleEmaCascade Then

                    Dim ema8Now = ema8Series(i)
                    Dim ema8Prev = ema8Series(i - 1)
                    Dim ema21CascNow = ema21Series(i)
                    Dim ema50CascNow = ema50Series(i)
                    Dim ema50CascPrev = ema50Series(i - 1)

                    If Not (Single.IsNaN(ema8Now) OrElse Single.IsNaN(ema8Prev) OrElse
                            Single.IsNaN(ema21CascNow) OrElse Single.IsNaN(ema50CascNow) OrElse
                            Single.IsNaN(ema50CascPrev)) Then

                        Dim lastCascadeClose = bar.Close
                        Dim crossedAbove = ema8Prev <= ema21Series(i - 1) AndAlso ema8Now > ema21CascNow
                        Dim crossedBelow = ema8Prev >= ema21Series(i - 1) AndAlso ema8Now < ema21CascNow
                        Dim ema50Rising = ema50CascNow > ema50CascPrev
                        Dim ema50Falling = ema50CascNow < ema50CascPrev

                        Dim cascadeSide As String = Nothing
                        If crossedAbove AndAlso lastCascadeClose > CDec(ema50CascNow) AndAlso ema50Rising Then
                            cascadeSide = "Buy"
                        ElseIf crossedBelow AndAlso lastCascadeClose < CDec(ema50CascNow) AndAlso ema50Falling Then
                            cascadeSide = "Sell"
                        End If

                        If cascadeSide IsNot Nothing Then
                            openTrade = New BacktestTrade With {
                                .EntryTime = bar.Timestamp,
                                .EntryPrice = bar.Close,
                                .Side = cascadeSide,
                                .Quantity = config.Quantity,
                                .SignalConfidence = 1.0F
                            }
                        End If
                    End If

                    Continue For  ' skip EMA/RSI block for this bar
                End If

                ' ── EMA/RSI weighted signal — same algorithm as StrategyExecutionEngine ──
                ' Only evaluate when flat (no open trade).
                If openTrade Is Nothing Then
                    Dim ema21Now = ema21Series(i)
                    Dim ema21Prev = ema21Series(i - 1)
                    Dim ema50Now = ema50Series(i)
                    Dim rsiVal = rsi14Series(i)

                    ' Skip bar if any indicator hasn't finished warming up yet
                    If Single.IsNaN(ema21Now) OrElse Single.IsNaN(ema21Prev) OrElse
                       Single.IsNaN(ema50Now) OrElse Single.IsNaN(rsiVal) Then Continue For

                    Dim lastClose = bar.Close
                    Dim bullScore As Double = 0

                    ' 1. EMA21 vs EMA50 crossover — 25 pts
                    If ema21Now > ema50Now Then bullScore += 25

                    ' 2. Close vs EMA21 — 20 pts
                    If lastClose > CDec(ema21Now) Then bullScore += 20

                    ' 3. Close vs EMA50 — 15 pts
                    If lastClose > CDec(ema50Now) Then bullScore += 15

                    ' 4. RSI gradient — 20 pts  (oversold=bullish, overbought=bearish)
                    Dim rsiScore As Double
                    If rsiVal <= 30 Then
                        rsiScore = 20
                    ElseIf rsiVal >= 70 Then
                        rsiScore = 0
                    Else
                        rsiScore = (70.0 - rsiVal) / 40.0 * 20.0
                    End If
                    bullScore += rsiScore

                    ' 5. EMA21 momentum (rising since prior bar) — 10 pts
                    If ema21Now > ema21Prev Then bullScore += 10

                    ' 6. Recent 3 candles: ≥ 2 bullish — 10 pts
                    Dim bullCandles As Integer = 0
                    For c = i - 2 To i
                        If filteredBars(c).IsBullish Then bullCandles += 1
                    Next
                    If bullCandles >= 2 Then bullScore += 10

                    Dim downPct As Double = 100.0 - bullScore

                    ' Fire entry when score meets the user-configured threshold
                    Dim tradeableSide As String = Nothing
                    Dim sigConf As Single = 0
                    If bullScore >= minPct Then
                        tradeableSide = "Buy"
                        sigConf = CSng(bullScore) / 100.0F
                    ElseIf downPct >= minPct Then
                        tradeableSide = "Sell"
                        sigConf = CSng(downPct) / 100.0F
                    End If

                    If tradeableSide IsNot Nothing Then
                        openTrade = New BacktestTrade With {
                            .EntryTime = bar.Timestamp,
                            .EntryPrice = bar.Close,
                            .Side = tradeableSide,
                            .Quantity = config.Quantity,
                            .SignalConfidence = sigConf
                        }
                    End If
                End If
            Next

            ' Close any open trade at end of data
            If openTrade IsNot Nothing Then
                Dim lastBar = filteredBars.Last()
                openTrade.ExitTime = lastBar.Timestamp
                openTrade.ExitPrice = lastBar.Close
                openTrade.ExitReason = "EndOfData"
                openTrade.PnL = BacktestMetrics.CalculatePnL(openTrade, config)
                capital += openTrade.PnL.GetValueOrDefault()
                trades.Add(openTrade)
            End If

            ' Calculate metrics
            Dim result = BacktestMetrics.BuildResult(config, trades, capital, maxDrawdown)

            ' Persist to database
            Try
                Await PersistResultAsync(result, filteredBars.Count, config.Timeframe)
            Catch ex As Exception
                _logger.LogError(ex, "Failed to persist backtest result")
            End Try

            _logger.LogInformation(
                "Backtest complete: {Trades} trades, PnL={PnL:C}, WinRate={WR:P1}",
                result.TotalTrades, result.TotalPnL, result.WinRate)

            Return result
        End Function

        Public Async Function GetBacktestRunsAsync() As Task(Of IEnumerable(Of BacktestResult)) _
            Implements IBacktestService.GetBacktestRunsAsync
            Dim entities = Await _backtestRepository.GetRecentRunsAsync()
            Return entities.Select(Function(e) MapRunToResult(e))
        End Function

        ' ─── Helpers ────────────────────────────────────────────────────────────

        Private Async Function PersistResultAsync(result As BacktestResult, barCount As Integer, timeframe As Integer) As Task
            Dim entity = New BacktestRunEntity With {
                .RunName = result.RunName,
                .ContractId = result.ContractId,
                .Timeframe = timeframe,
                .StartDate = result.StartDate,
                .EndDate = result.EndDate,
                .InitialCapital = result.InitialCapital,
                .FinalCapital = result.FinalCapital,
                .TotalTrades = result.TotalTrades,
                .WinningTrades = result.WinningTrades,
                .LosingTrades = result.LosingTrades,
                .TotalPnL = result.TotalPnL,
                .MaxDrawdown = result.MaxDrawdown,
                .WinRate = result.WinRate,
                .SharpeRatio = result.SharpeRatio,
                .AveragePnLPerTrade = result.AveragePnLPerTrade,
                .ModelVersion = "EMA/RSI-Rule-Based",
                .CompletedAt = DateTimeOffset.UtcNow,
                .Trades = result.Trades.Select(Function(t) New BacktestTradeEntity With {
                    .EntryTime = t.EntryTime,
                    .ExitTime = t.ExitTime,
                    .Side = t.Side,
                    .EntryPrice = t.EntryPrice,
                    .ExitPrice = t.ExitPrice,
                    .Quantity = t.Quantity,
                    .PnL = t.PnL,
                    .ExitReason = t.ExitReason,
                    .SignalConfidence = t.SignalConfidence
                }).ToList()
            }
            result.Id = Await _backtestRepository.SaveRunAsync(entity)
        End Function

        Private Shared Function MapRunToResult(e As BacktestRunEntity) As BacktestResult
            Return New BacktestResult With {
                .Id = e.Id,
                .RunName = e.RunName,
                .ContractId = e.ContractId,
                .StartDate = e.StartDate,
                .EndDate = e.EndDate,
                .InitialCapital = e.InitialCapital,
                .FinalCapital = e.FinalCapital,
                .TotalTrades = e.TotalTrades,
                .WinningTrades = e.WinningTrades,
                .LosingTrades = e.LosingTrades,
                .TotalPnL = e.TotalPnL,
                .MaxDrawdown = e.MaxDrawdown,
                .WinRate = e.WinRate.GetValueOrDefault(),
                .SharpeRatio = e.SharpeRatio,
                .AveragePnLPerTrade = e.AveragePnLPerTrade
            }
        End Function

    End Class

End Namespace
