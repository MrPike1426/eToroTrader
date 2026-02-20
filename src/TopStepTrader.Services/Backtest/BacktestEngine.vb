Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Entities
Imports TopStepTrader.Data.Repositories
Imports TopStepTrader.ML.Prediction

Namespace TopStepTrader.Services.Backtest

    ''' <summary>
    ''' Walk-forward backtest engine. Replays historical bars through the ML predictor,
    ''' simulating entries/exits and computing P&amp;L metrics.
    ''' </summary>
    Public Class BacktestEngine
        Implements IBacktestService

        Private ReadOnly _barRepository As BarRepository
        Private ReadOnly _backtestRepository As BacktestRepository
        Private ReadOnly _predictor As SignalPredictor
        Private ReadOnly _logger As ILogger(Of BacktestEngine)

        Public Event ProgressUpdated As EventHandler(Of BacktestProgressEventArgs) _
            Implements IBacktestService.ProgressUpdated

        Public Sub New(barRepository As BarRepository,
                       backtestRepository As BacktestRepository,
                       predictor As SignalPredictor,
                       logger As ILogger(Of BacktestEngine))
            _barRepository = barRepository
            _backtestRepository = backtestRepository
            _predictor = predictor
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

            Dim trades As New List(Of BacktestTrade)()
            Dim capital = config.InitialCapital
            Dim peakCapital = capital
            Dim maxDrawdown = 0D
            Dim openTrade As BacktestTrade = Nothing
            Dim windowSize = 30  ' Minimum bars for signal

            For i = windowSize To filteredBars.Count - 1
                cancel.ThrowIfCancellationRequested()

                ' Progress events every 5%
                Dim pct = CInt((i / CDbl(filteredBars.Count)) * 100)
                If i Mod CInt(filteredBars.Count / 20) = 0 Then
                    RaiseEvent ProgressUpdated(Me, New BacktestProgressEventArgs(
                        pct, filteredBars(i).Timestamp.Date, trades.Count))
                End If

                ' filteredBars is already List(Of MarketBar) — no mapping needed
                Dim bar = filteredBars(i)
                Dim windowBars = filteredBars.Skip(i - windowSize).Take(windowSize + 1).ToList()

                ' Check exit for open trade
                If openTrade IsNot Nothing Then
                    Dim exitReason = CheckExit(openTrade, bar, config)
                    If exitReason IsNot Nothing Then
                        openTrade.ExitTime = bar.Timestamp
                        openTrade.ExitPrice = bar.Close
                        openTrade.ExitReason = exitReason
                        Dim pnl = CalculatePnL(openTrade)
                        openTrade.PnL = pnl
                        capital += pnl
                        If capital > peakCapital Then peakCapital = capital
                        Dim dd = peakCapital - capital
                        If dd > maxDrawdown Then maxDrawdown = dd
                        trades.Add(openTrade)
                        openTrade = Nothing
                    End If
                End If

                ' Generate signal for potential entry (only when flat)
                If openTrade Is Nothing AndAlso _predictor.IsModelLoaded Then
                    Dim prediction = _predictor.Predict(windowBars)
                    If prediction IsNot Nothing Then
                        Dim sig = prediction.ToSignalType(config.MinSignalConfidence)
                        If sig <> SignalType.Hold Then
                            openTrade = New BacktestTrade With {
                                .EntryTime = bar.Timestamp,
                                .EntryPrice = bar.Close,
                                .Side = sig.ToString(),
                                .Quantity = 1,
                                .SignalConfidence = prediction.Confidence
                            }
                        End If
                    End If
                End If
            Next

            ' Close any open trade at end of data
            If openTrade IsNot Nothing Then
                Dim lastBar = filteredBars.Last()
                openTrade.ExitTime = lastBar.Timestamp
                openTrade.ExitPrice = lastBar.Close
                openTrade.ExitReason = "EndOfData"
                openTrade.PnL = CalculatePnL(openTrade)
                capital += openTrade.PnL.GetValueOrDefault()
                trades.Add(openTrade)
            End If

            ' Calculate metrics
            Dim result = BuildResult(config, trades, capital, maxDrawdown)

            ' Persist to database
            Try
                Await PersistResultAsync(result, filteredBars.Count)
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

        Private Shared Function CheckExit(trade As BacktestTrade,
                                           bar As MarketBar,
                                           config As BacktestConfiguration) As String
            Dim isBuy = trade.Side = "Buy"
            ' Stop loss
            Dim stopDelta = config.StopLossTicks * 0.25D  ' 1 tick = $12.50 on ES (0.25 pts)
            Dim tpDelta = config.TakeProfitTicks * 0.25D

            If isBuy Then
                If bar.Low <= trade.EntryPrice - stopDelta Then Return "StopLoss"
                If bar.High >= trade.EntryPrice + tpDelta Then Return "TakeProfit"
            Else
                If bar.High >= trade.EntryPrice + stopDelta Then Return "StopLoss"
                If bar.Low <= trade.EntryPrice - tpDelta Then Return "TakeProfit"
            End If
            Return Nothing
        End Function

        Private Shared Function CalculatePnL(trade As BacktestTrade) As Decimal
            If Not trade.ExitPrice.HasValue Then Return 0D
            Dim priceDiff = trade.ExitPrice.Value - trade.EntryPrice
            Dim isBuy = trade.Side = "Buy"
            ' ES futures: $50 per point
            Dim multiplier = 50D
            Return If(isBuy, priceDiff, -priceDiff) * trade.Quantity * multiplier
        End Function

        Private Shared Function BuildResult(config As BacktestConfiguration,
                                             trades As List(Of BacktestTrade),
                                             finalCapital As Decimal,
                                             maxDrawdown As Decimal) As BacktestResult
            Dim winners = trades.Where(Function(t) t.PnL.GetValueOrDefault() > 0).ToList()
            Dim losers = trades.Where(Function(t) t.PnL.GetValueOrDefault() <= 0).ToList()
            Dim totalPnL = trades.Sum(Function(t) t.PnL.GetValueOrDefault())

            Return New BacktestResult With {
                .RunName = config.RunName,
                .ContractId = config.ContractId,
                .StartDate = config.StartDate,
                .EndDate = config.EndDate,
                .InitialCapital = config.InitialCapital,
                .FinalCapital = finalCapital,
                .TotalTrades = trades.Count,
                .WinningTrades = winners.Count,
                .LosingTrades = losers.Count,
                .TotalPnL = totalPnL,
                .MaxDrawdown = maxDrawdown,
                .WinRate = If(trades.Count > 0, CSng(winners.Count) / trades.Count, 0F),
                .AveragePnLPerTrade = If(trades.Count > 0, totalPnL / trades.Count, 0D),
                .SharpeRatio = CalculateSharpe(trades),
                .Trades = trades
            }
        End Function

        Private Shared Function CalculateSharpe(trades As List(Of BacktestTrade)) As Single?
            If trades.Count < 2 Then Return Nothing
            Dim returns = trades.Select(Function(t) CDbl(t.PnL.GetValueOrDefault())).ToList()
            Dim avg = returns.Average()
            Dim variance = returns.Select(Function(r) (r - avg) * (r - avg)).Average()
            Dim stddev = Math.Sqrt(variance)
            If stddev = 0 Then Return Nothing
            Return CSng(avg / stddev * Math.Sqrt(252))  ' Annualised
        End Function

        Private Async Function PersistResultAsync(result As BacktestResult, barCount As Integer) As Task
            Dim entity = New BacktestRunEntity With {
                .RunName = result.RunName,
                .ContractId = result.ContractId,
                .Timeframe = 5,
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
                .ModelVersion = _predictor.ModelVersion,
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
