Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models

Namespace TopStepTrader.Services.Backtest

    ''' <summary>
    ''' Pure calculation helpers for the backtest engine.
    ''' Extracted from <see cref="BacktestEngine"/> so they can be unit-tested in isolation.
    '''
    ''' All members are Friend — accessible within TopStepTrader.Services and any assembly
    ''' granted access via InternalsVisibleTo (i.e. TopStepTrader.Tests).
    ''' </summary>
    Friend Module BacktestMetrics

        ''' <summary>
        ''' Calculate the dollar P&amp;L for a closed trade using the contract-specific point value
        ''' from <paramref name="config"/>.
        '''
        ''' Formula: (exitPrice − entryPrice) × quantity × pointValue
        ''' where pointValue is dollars per 1.0 price-unit move (set per-contract in BacktestViewModel).
        '''
        ''' Correct values: MES = $5/pt, MNQ = $2/pt, MGC = $10/pt, MCL = $10/pt.
        ''' (Old code hardcoded $50/pt which is the full-size ES — 10× too large for MES.)
        '''
        ''' Returns 0 when no exit price has been recorded (open trade guard).
        ''' </summary>
        Friend Function CalculatePnL(trade As BacktestTrade,
                                      config As BacktestConfiguration) As Decimal
            If Not trade.ExitPrice.HasValue Then Return 0D
            Dim priceDiff = trade.ExitPrice.Value - trade.EntryPrice
            Dim isBuy = trade.Side = "Buy"
            Return If(isBuy, priceDiff, -priceDiff) * trade.Quantity * config.PointValue
        End Function

        ''' <summary>
        ''' Determine whether the current bar triggers a stop-loss or take-profit exit.
        ''' Returns "StopLoss", "TakeProfit", or Nothing if neither level is hit.
        '''
        ''' Tick convention: uses config.TickSize (price units per tick).
        ''' Defaults to 0.25 (MES/MNQ). MGC uses 0.10, MCL uses 0.01.
        ''' Buy  SL: bar.Low  ≤ entryPrice - (slTicks × tickSize)
        ''' Buy  TP: bar.High ≥ entryPrice + (tpTicks × tickSize)
        ''' Sell SL: bar.High ≥ entryPrice + (slTicks × tickSize)
        ''' Sell TP: bar.Low  ≤ entryPrice - (tpTicks × tickSize)
        ''' </summary>
        Friend Function CheckExit(trade As BacktestTrade,
                                   bar As MarketBar,
                                   config As BacktestConfiguration) As String
            Dim isBuy = trade.Side = "Buy"
            Dim stopDelta = config.StopLossTicks * config.TickSize
            Dim tpDelta = config.TakeProfitTicks * config.TickSize

            If isBuy Then
                If bar.Low <= trade.EntryPrice - stopDelta Then Return "StopLoss"
                If bar.High >= trade.EntryPrice + tpDelta Then Return "TakeProfit"
            Else
                If bar.High >= trade.EntryPrice + stopDelta Then Return "StopLoss"
                If bar.Low <= trade.EntryPrice - tpDelta Then Return "TakeProfit"
            End If
            Return Nothing
        End Function

        ''' <summary>
        ''' Returns the exact fill price for a closed trade based on its exit reason.
        '''
        ''' UAT-BUG-006: Using bar.Close as the exit price when SL/TP is triggered on
        ''' bar.High/Low (OHLC detection) produces physically impossible results:
        '''   • If a Sell-side SL is triggered on bar.High but the bar closes below entry
        '''     (bar.Close &lt; entry), the trade would show "StopLoss" with a profit — impossible.
        ''' Fix: when SL or TP fires, fill at the exact level price rather than bar.Close.
        ''' This guarantees StopLoss always produces a loss and TakeProfit always a profit.
        '''
        ''' Rule:
        '''   StopLoss  — Buy:  entry - stopDelta   (fill below entry = loss)
        '''   StopLoss  — Sell: entry + stopDelta   (fill above entry = loss)
        '''   TakeProfit— Buy:  entry + tpDelta     (fill above entry = profit)
        '''   TakeProfit— Sell: entry - tpDelta     (fill below entry = profit)
        '''   EndOfData — any:  bar.Close           (exit at market; no level was hit)
        ''' </summary>
        Friend Function GetExitPrice(trade As BacktestTrade,
                                      bar As MarketBar,
                                      exitReason As String,
                                      config As BacktestConfiguration) As Decimal
            Dim isBuy = trade.Side = "Buy"
            If exitReason = "StopLoss" Then
                Dim stopDelta = config.StopLossTicks * config.TickSize
                Return If(isBuy, trade.EntryPrice - stopDelta, trade.EntryPrice + stopDelta)
            ElseIf exitReason = "TakeProfit" Then
                Dim tpDelta = config.TakeProfitTicks * config.TickSize
                Return If(isBuy, trade.EntryPrice + tpDelta, trade.EntryPrice - tpDelta)
            Else
                Return bar.Close   ' EndOfData or unknown reason — exit at the bar's close price
            End If
        End Function

        ''' <summary>
        ''' Annualised Sharpe ratio computed from the list of trade P&amp;Ls.
        ''' Returns Nothing when fewer than 2 trades exist or when all P&amp;Ls are identical
        ''' (standard deviation is zero — Sharpe is undefined).
        ''' Formula: (avg P&amp;L / stddev P&amp;L) × √252
        ''' </summary>
        Friend Function CalculateSharpe(trades As List(Of BacktestTrade)) As Single?
            If trades.Count < 2 Then Return Nothing
            Dim returns = trades.Select(Function(t) CDbl(t.PnL.GetValueOrDefault())).ToList()
            Dim avg = returns.Average()
            Dim variance = returns.Select(Function(r) (r - avg) * (r - avg)).Average()
            Dim stddev = Math.Sqrt(variance)
            If stddev = 0 Then Return Nothing
            Return CSng(avg / stddev * Math.Sqrt(252))  ' Annualised
        End Function

        ''' <summary>
        ''' Aggregate a completed list of trades and run metadata into a <see cref="BacktestResult"/>.
        ''' Win rate is 0 when no trades were taken; Sharpe is Nothing when undefined.
        ''' </summary>
        Friend Function BuildResult(config As BacktestConfiguration,
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

    End Module

End Namespace
