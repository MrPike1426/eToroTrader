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
    '''   4. RSI14 trending zone (50–70 = +20 pts, else 0 pts) — up to 20 pts
    '''   5. EMA21 momentum (rising since prior bar) — 10 pts
    '''   6. 2+ of last 3 candles bullish — 10 pts
    '''
    ''' Exit rules (EmaRsiWeightedScore):
    '''   TP / SL intrabar fills (price-level triggers via bar.High/Low).
    '''   Neutral confidence exit: score 40–60% → close at bar close (mirrors live engine priority).
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
            Dim from As DateTimeOffset = New DateTimeOffset(DateTime.SpecifyKind(config.StartDate, DateTimeKind.Unspecified), TimeSpan.Zero)
            Dim [to] As DateTimeOffset = New DateTimeOffset(DateTime.SpecifyKind(config.EndDate, DateTimeKind.Unspecified), TimeSpan.Zero).AddDays(1)
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
            Dim allHighs = filteredBars.Select(Function(b) b.High).ToList()
            Dim allLows = filteredBars.Select(Function(b) b.Low).ToList()
            Dim ema21Series = TechnicalIndicators.EMA(allCloses, 21)  ' valid from index 20
            Dim ema50Series = TechnicalIndicators.EMA(allCloses, 50)  ' valid from index 49
            Dim rsi14Series = TechnicalIndicators.RSI(allCloses, 14)  ' valid from index 14
            ' ADX(14) — mirrors the live ADX gate in StrategyExecutionEngine (TICKET-019).
            ' Suppresses EmaRsiWeightedScore entry signals when ADX < 25 (ranging market).
            ' Valid from index 2*14-1=27; warmUp=55 ensures it's valid before any signal fires.
            Dim adx14Series = TechnicalIndicators.DMI(allHighs, allLows, allCloses).ADX

            ' EMA8 only needed for Sniper (TripleEmaCascade) strategy.
            Dim ema8Series As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.TripleEmaCascade Then
                ema8Series = TechnicalIndicators.EMA(allCloses, 8)    ' valid from index 7
            End If

            ' ── QuantLab strategies — pre-calculate indicator series ─────────────

            ' ConnorsRsi2 (=11): RSI(2), SMA(5), SMA(200)
            Dim qlRsi2 As Single() = Nothing
            Dim qlSma5 As Single() = Nothing
            Dim qlSma200 As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.ConnorsRsi2 Then
                qlRsi2 = TechnicalIndicators.Rsi2(allCloses)          ' valid from index 2
                qlSma5 = TechnicalIndicators.SMA(allCloses, 5)        ' valid from index 4
                qlSma200 = TechnicalIndicators.SMA(allCloses, 200)    ' valid from index 199
            End If

            ' SuperTrend (=12): ATR(10) × 3.0 multiplier
            Dim qlStLine As Single() = Nothing
            Dim qlStDir As Single() = Nothing
            Dim qlStAtr10 As Single() = Nothing    ' pre-calculated ATR(10) for TP sizing
            If config.StrategyCondition = StrategyConditionType.SuperTrend Then
                Dim stResult = TechnicalIndicators.SuperTrend(allHighs, allLows, allCloses, 10, 3.0)
                qlStLine = stResult.Line
                qlStDir = stResult.Direction
                qlStAtr10 = TechnicalIndicators.ATR(allHighs, allLows, allCloses, 10)
            End If

            ' DonchianBreakout (=13): 20-bar Donchian channel + 10-bar exit channel
            Dim qlDonUpper20 As Single() = Nothing
            Dim qlDonLower20 As Single() = Nothing
            Dim qlDonMid10 As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.DonchianBreakout Then
                Dim don20 = TechnicalIndicators.DonchianChannel(allHighs, allLows, 20)
                qlDonUpper20 = don20.Upper
                qlDonLower20 = don20.Lower
                Dim don10 = TechnicalIndicators.DonchianChannel(allHighs, allLows, 10)
                qlDonMid10 = don10.Middle     ' exit when close crosses mid of 10-bar channel
            End If

            ' BbRsiMeanReversion (=14): BB(20,2) + RSI(14) — both already computed above
            ' (ema21/rsi14 series already available; BB needs its own pre-calc)
            Dim qlBbUpper As Single() = Nothing
            Dim qlBbMiddle As Single() = Nothing
            Dim qlBbLower As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.BbRsiMeanReversion Then
                Dim bb20 = TechnicalIndicators.BollingerBands(allCloses, 20, 2.0)
                qlBbUpper = bb20.Upper
                qlBbMiddle = bb20.Middle
                qlBbLower = bb20.Lower
            End If

            ' State: open SuperTrend SL/TP prices (price-level exit, like MultiConfluence)
            Dim qlStOpenSlPrice As Decimal = 0D
            Dim qlStOpenTpPrice As Decimal = 0D
            Dim qlStIsLong As Boolean = True
            Dim qlStPrevDir As Single = 0.0F   ' previous SuperTrend direction for flip detection

            ' State: open Donchian / BbRsi exit levels
            Dim qlDonOpenMidExit As Decimal = 0D    ' adverse mid-channel level at entry
            Dim qlDonIsLong As Boolean = True
            Dim qlBbOpenMidExit As Decimal = 0D     ' BB middle band (SMA20) at entry
            Dim qlBbIsLong As Boolean = True

            ' MultiConfluence — pre-calculate full indicator series once for all bars.
            ' Senkou Span B needs senkouBPeriod(52) + displacement(26) = 78 bars minimum.
            Dim mcIchiTenkan As Single() = Nothing
            Dim mcIchiKijun As Single() = Nothing
            Dim mcIchiSpanA As Single() = Nothing
            Dim mcIchiSpanB As Single() = Nothing
            Dim mcPlusDI As Single() = Nothing
            Dim mcMinusDI As Single() = Nothing
            Dim mcAdxSeries As Single() = Nothing
            Dim mcMacdHist As Single() = Nothing
            Dim mcStochRsiK As Single() = Nothing
            Dim mcAtr14 As Single() = Nothing
            If config.StrategyCondition = StrategyConditionType.MultiConfluence Then
                Dim ichi = TechnicalIndicators.IchimokuCloud(allHighs, allLows, allCloses, 9, 26, 52, 26)
                mcIchiTenkan = ichi.Tenkan
                mcIchiKijun = ichi.Kijun
                mcIchiSpanA = ichi.SpanA
                mcIchiSpanB = ichi.SpanB
                Dim dmiMc = TechnicalIndicators.DMI(allHighs, allLows, allCloses, 14)
                mcPlusDI = dmiMc.PlusDI
                mcMinusDI = dmiMc.MinusDI
                mcAdxSeries = dmiMc.ADX
                mcMacdHist = TechnicalIndicators.MACD(allCloses).Histogram
                mcStochRsiK = TechnicalIndicators.StochasticRSI(allCloses).K
                mcAtr14 = TechnicalIndicators.ATR(allHighs, allLows, allCloses, 14)
            End If

            ' Warm-up: EMA50 needs 50 bars; add 5-bar buffer so EMA21Prev also valid.
            ' MultiConfluence warm-up: Senkou Span B + displacement = 78 bars (80 with buffer).
            ' ConnorsRsi2 warm-up: SMA(200) needs 200 bars; add 5-bar buffer = 205.
            ' SuperTrend/DonchianBreakout/BbRsiMeanReversion warm-up: 25 bars is sufficient.
            Dim warmUp As Integer
            Select Case config.StrategyCondition
                Case StrategyConditionType.MultiConfluence
                    warmUp = 80
                Case StrategyConditionType.ConnorsRsi2
                    warmUp = 205
                Case Else
                    warmUp = 55
            End Select

            Dim trades As New List(Of BacktestTrade)()
            Dim capital = config.InitialCapital
            Dim peakCapital = capital
            Dim maxDrawdown = 0D
            ' openLegs holds all entry/scale-in legs for the currently open position.
            ' All legs share the same PositionGroupId and exit together.
            Dim openLegs As New List(Of BacktestTrade)()
            Dim positionGroupCounter As Integer = 0

            ' MultiConfluence ATR-based SL/TP prices — set at entry, cleared on exit.
            ' Used instead of tick-based config values for MultiConfluence positions.
            Dim mcOpenSlPrice As Decimal = 0D
            Dim mcOpenTpPrice As Decimal = 0D
            Dim mcIsLong As Boolean = True

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

                ' ── Check exit for open position ──────────────────────────────────
                ' TP/SL levels are anchored to the first leg's entry price; all legs exit together.
                ' MultiConfluence / SuperTrend use ATR-based price-level checks.
                ' DonchianBreakout / BbRsiMeanReversion use indicator-level exits.
                ' All others use tick-based config.
                If openLegs.Count > 0 Then
                    Dim exitReason As String = Nothing
                    Dim exitPrice As Decimal = bar.Close

                    ' ── SuperTrend price-level exit ────────────────────────────────
                    If config.StrategyCondition = StrategyConditionType.SuperTrend AndAlso qlStOpenSlPrice <> 0D Then
                        If qlStIsLong Then
                            If bar.Low <= qlStOpenSlPrice Then
                                exitReason = "StopLoss"
                                exitPrice = qlStOpenSlPrice
                            ElseIf bar.High >= qlStOpenTpPrice Then
                                exitReason = "TakeProfit"
                                exitPrice = qlStOpenTpPrice
                            End If
                        Else
                            If bar.High >= qlStOpenSlPrice Then
                                exitReason = "StopLoss"
                                exitPrice = qlStOpenSlPrice
                            ElseIf bar.Low <= qlStOpenTpPrice Then
                                exitReason = "TakeProfit"
                                exitPrice = qlStOpenTpPrice
                            End If
                        End If
                    End If

                    ' ── DonchianBreakout indicator-level exit ──────────────────────
                    If exitReason Is Nothing AndAlso
                       config.StrategyCondition = StrategyConditionType.DonchianBreakout AndAlso
                       qlDonOpenMidExit <> 0D Then
                        If qlDonIsLong AndAlso bar.Close < qlDonOpenMidExit Then
                            exitReason = "NeutralExit"
                        ElseIf Not qlDonIsLong AndAlso bar.Close > qlDonOpenMidExit Then
                            exitReason = "NeutralExit"
                        End If
                    End If

                    ' ── BbRsiMeanReversion indicator-level exit ────────────────────
                    ' Exit Long when close >= BB middle (SMA20) or RSI crosses back above 50.
                    ' Exit Short when close <= BB middle (SMA20) or RSI crosses back below 50.
                    If exitReason Is Nothing AndAlso
                       config.StrategyCondition = StrategyConditionType.BbRsiMeanReversion AndAlso
                       qlBbOpenMidExit <> 0D Then
                        Dim rsiExitVal = If(qlBbIsLong,
                                            rsi14Series(i) <= 50.0F,   ' long: exit when RSI reverses ≤ 50
                                            rsi14Series(i) >= 50.0F)   ' short: exit when RSI reverses ≥ 50
                        If qlBbIsLong AndAlso
                           (bar.Close >= qlBbOpenMidExit OrElse
                            (Not Single.IsNaN(rsi14Series(i)) AndAlso rsiExitVal)) Then
                            exitReason = "TakeProfit"
                        ElseIf Not qlBbIsLong AndAlso
                               (bar.Close <= qlBbOpenMidExit OrElse
                                (Not Single.IsNaN(rsi14Series(i)) AndAlso rsiExitVal)) Then
                            exitReason = "TakeProfit"
                        End If
                    End If

                    ' ── ConnorsRsi2 RSI-based exit ─────────────────────────────────
                    ' Exit Long when close > SMA(5) or RSI(2) > 65.
                    ' Exit Short when close < SMA(5) or RSI(2) < 35.
                    If exitReason Is Nothing AndAlso
                       config.StrategyCondition = StrategyConditionType.ConnorsRsi2 AndAlso
                       qlRsi2 IsNot Nothing AndAlso qlSma5 IsNot Nothing Then
                        If Not (Single.IsNaN(qlRsi2(i)) OrElse Single.IsNaN(qlSma5(i))) Then
                            If openLegs(0).Side = "Buy" Then
                                If bar.Close > CDec(qlSma5(i)) OrElse qlRsi2(i) > 65.0F Then
                                    exitReason = "TakeProfit"
                                End If
                            Else
                                If bar.Close < CDec(qlSma5(i)) OrElse qlRsi2(i) < 35.0F Then
                                    exitReason = "TakeProfit"
                                End If
                            End If
                        End If
                    End If

                    If config.StrategyCondition = StrategyConditionType.MultiConfluence AndAlso mcOpenSlPrice <> 0D Then
                        If mcIsLong Then
                            If bar.Low <= mcOpenSlPrice Then
                                exitReason = "StopLoss"
                                exitPrice = mcOpenSlPrice
                            ElseIf bar.High >= mcOpenTpPrice Then
                                exitReason = "TakeProfit"
                                exitPrice = mcOpenTpPrice
                            End If
                        Else
                            If bar.High >= mcOpenSlPrice Then
                                exitReason = "StopLoss"
                                exitPrice = mcOpenSlPrice
                            ElseIf bar.Low <= mcOpenTpPrice Then
                                exitReason = "TakeProfit"
                                exitPrice = mcOpenTpPrice
                            End If
                        End If
                     ElseIf exitReason Is Nothing Then
                        exitReason = BacktestMetrics.CheckExit(openLegs(0), bar, config)
                        If exitReason IsNot Nothing Then
                            exitPrice = BacktestMetrics.GetExitPrice(openLegs(0), bar, exitReason, config)
                        End If
                    End If
                    If exitReason IsNot Nothing Then
                        Dim positionPnL = 0D
                        For Each leg In openLegs
                            leg.ExitTime = bar.Timestamp
                            leg.ExitPrice = exitPrice
                            leg.ExitReason = exitReason
                            Dim pnl = BacktestMetrics.CalculatePnL(leg, config)
                            leg.PnL = pnl
                            positionPnL += pnl
                            trades.Add(leg)
                        Next
                        capital += positionPnL
                        If capital > peakCapital Then peakCapital = capital
                        Dim dd = peakCapital - capital
                        If dd > maxDrawdown Then maxDrawdown = dd
                        openLegs.Clear()
                        mcOpenSlPrice = 0D
                        mcOpenTpPrice = 0D
                        ' Clear QuantLab state
                        qlStOpenSlPrice = 0D
                        qlStOpenTpPrice = 0D
                        qlDonOpenMidExit = 0D
                        qlBbOpenMidExit = 0D
                    End If
                End If

                ' ── Signal evaluation — Only when flat (no open trade) ─────────────
                ' Branches on config.StrategyCondition: EmaRsiWeightedScore or TripleEmaCascade.

                ' ── 3-EMA Cascade (Sniper) signal ─────────────────────────────────
                If openLegs.Count = 0 AndAlso
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
                            positionGroupCounter += 1
                            openLegs.Add(New BacktestTrade With {
                                .PositionGroupId = positionGroupCounter,
                                .EntryTime = bar.Timestamp,
                                .EntryPrice = bar.Close,
                                .Side = cascadeSide,
                                .Quantity = config.Quantity,
                                .SignalConfidence = 1.0F
                            })
                        End If
                    End If

                    Continue For  ' skip EMA/RSI block for this bar
                End If

                ' ── Multi-Confluence Engine signal ─────────────────────────────────
                ' ALL 7 conditions (Ichimoku cloud + EMA21 + Tenkan/Kijun + Chikou +
                ' ADX/DMI + MACD histogram + StochRSI) must align for Long or Short.
                ' SL = min(1.5×ATR, Ichimoku cloud edge); TP = 2:1 reward-to-risk.
                If config.StrategyCondition = StrategyConditionType.MultiConfluence Then
                    Dim mcSpanA = If(mcIchiSpanA IsNot Nothing, mcIchiSpanA(i), Single.NaN)
                    Dim mcSpanB = If(mcIchiSpanB IsNot Nothing, mcIchiSpanB(i), Single.NaN)
                    Dim mcTenkan = If(mcIchiTenkan IsNot Nothing, mcIchiTenkan(i), Single.NaN)
                    Dim mcKijun = If(mcIchiKijun IsNot Nothing, mcIchiKijun(i), Single.NaN)
                    Dim mcAdxVal = If(mcAdxSeries IsNot Nothing, mcAdxSeries(i), Single.NaN)
                    Dim mcPlusDIVal = If(mcPlusDI IsNot Nothing, mcPlusDI(i), Single.NaN)
                    Dim mcMinusDIVal = If(mcMinusDI IsNot Nothing, mcMinusDI(i), Single.NaN)
                    Dim mcHistNow = If(mcMacdHist IsNot Nothing AndAlso Not Single.IsNaN(mcMacdHist(i)), mcMacdHist(i), Single.NaN)
                    Dim mcHistPrev = If(mcMacdHist IsNot Nothing AndAlso i > 0 AndAlso Not Single.IsNaN(mcMacdHist(i - 1)), mcMacdHist(i - 1), Single.NaN)
                    Dim mcStochK = If(mcStochRsiK IsNot Nothing AndAlso Not Single.IsNaN(mcStochRsiK(i)), mcStochRsiK(i), Single.NaN)
                    Dim mcAtrVal = If(mcAtr14 IsNot Nothing AndAlso Not Single.IsNaN(mcAtr14(i)), mcAtr14(i), 0.0F)
                    Dim mcEma21Val = ema21Series(i)
                    Dim mcLastClose = bar.Close

                    ' Skip if any indicator is still warming up
                    If Not (Single.IsNaN(mcSpanA) OrElse Single.IsNaN(mcSpanB) OrElse
                            Single.IsNaN(mcTenkan) OrElse Single.IsNaN(mcKijun) OrElse
                            Single.IsNaN(mcAdxVal) OrElse Single.IsNaN(mcHistNow) OrElse
                            Single.IsNaN(mcHistPrev) OrElse Single.IsNaN(mcStochK) OrElse
                            Single.IsNaN(mcEma21Val)) Then

                        Dim mcCloudTop = CDec(Math.Max(mcSpanA, mcSpanB))
                        Dim mcCloudBottom = CDec(Math.Min(mcSpanA, mcSpanB))
                        Dim mcLagIdx = i - 26
                        Dim mcLagClose = If(mcLagIdx >= 0, filteredBars(mcLagIdx).Close, Decimal.MinValue)

                        ' ── Long: all 7 conditions ────────────────────────────────
                        Dim lcl1 = (mcLastClose > mcCloudTop)
                        Dim lcl2 = (mcLastClose > CDec(mcEma21Val))
                        Dim lcl3 = (mcTenkan > mcKijun)
                        Dim lcl4 = (mcLagIdx >= 0 AndAlso mcLastClose > mcLagClose)
                        Dim lcl5 = (mcAdxVal >= 19.9F AndAlso mcPlusDIVal > mcMinusDIVal)
                        Dim lcl6 = (mcHistNow > 0 AndAlso mcHistNow > mcHistPrev)
                        Dim lcl7 = (mcStochK < 0.8F)

                        ' ── Short: all 7 conditions ───────────────────────────────
                        Dim scl1 = (mcLastClose < mcCloudBottom)
                        Dim scl2 = (mcLastClose < CDec(mcEma21Val))
                        Dim scl3 = (mcTenkan < mcKijun)
                        Dim scl4 = (mcLagIdx >= 0 AndAlso mcLastClose < mcLagClose)
                        Dim scl5 = (mcAdxVal >= 19.9F AndAlso mcMinusDIVal > mcPlusDIVal)
                        Dim scl6 = (mcHistNow < 0 AndAlso mcHistNow < mcHistPrev)
                        Dim scl7 = (mcStochK > 0.2F)

                        If openLegs.Count = 0 Then
                            Dim mcSide As String = Nothing
                            Dim mcSlCand As Decimal = 0D
                            Dim mcTpCand As Decimal = 0D
                            Dim mcAtrSlLevel As Decimal = 0D

                            If lcl1 AndAlso lcl2 AndAlso lcl3 AndAlso lcl4 AndAlso lcl5 AndAlso lcl6 AndAlso lcl7 Then
                                mcSide = "Buy"
                                ' SL = min(1.5×ATR, cloud bottom); TP = 2:1 R:R from actual SL
                                mcAtrSlLevel = mcLastClose - CDec(mcAtrVal * 1.5F)
                                mcSlCand = If(mcCloudBottom > mcAtrSlLevel, mcCloudBottom, mcAtrSlLevel)
                                mcTpCand = mcLastClose + (mcLastClose - mcSlCand) * 2D
                            ElseIf scl1 AndAlso scl2 AndAlso scl3 AndAlso scl4 AndAlso scl5 AndAlso scl6 AndAlso scl7 Then
                                mcSide = "Sell"
                                ' SL = min(1.5×ATR, cloud top); TP = 2:1 R:R from actual SL
                                mcAtrSlLevel = mcLastClose + CDec(mcAtrVal * 1.5F)
                                mcSlCand = If(mcCloudTop < mcAtrSlLevel, mcCloudTop, mcAtrSlLevel)
                                mcTpCand = mcLastClose - (mcSlCand - mcLastClose) * 2D
                            End If

                            If mcSide IsNot Nothing AndAlso mcSlCand <> 0D Then
                                positionGroupCounter += 1
                                openLegs.Add(New BacktestTrade With {
                                    .PositionGroupId = positionGroupCounter,
                                    .EntryTime = bar.Timestamp,
                                    .EntryPrice = mcLastClose,
                                    .Side = mcSide,
                                    .Quantity = config.Quantity,
                                    .SignalConfidence = 1.0F
                                })
                                mcOpenSlPrice = mcSlCand
                                mcOpenTpPrice = mcTpCand
                                mcIsLong = (mcSide = "Buy")
                            End If
                        End If
                    End If

                    Continue For  ' skip EMA/RSI block for this bar
                End If

                ' ── ConnorsRsi2 signal ────────────────────────────────────────────
                ' Long: RSI(2) < 10 AND close > SMA(200) — short-term dip in long-term uptrend.
                ' Short: RSI(2) > 90 AND close < SMA(200) — short-term rally in long-term downtrend.
                If openLegs.Count = 0 AndAlso
                   config.StrategyCondition = StrategyConditionType.ConnorsRsi2 Then
                    If qlRsi2 IsNot Nothing AndAlso qlSma5 IsNot Nothing AndAlso qlSma200 IsNot Nothing Then
                        Dim rsi2Val = qlRsi2(i)
                        Dim sma200Val = qlSma200(i)
                        If Not (Single.IsNaN(rsi2Val) OrElse Single.IsNaN(sma200Val)) Then
                            Dim crSide As String = Nothing
                            If rsi2Val < 10.0F AndAlso bar.Close > CDec(sma200Val) Then
                                crSide = "Buy"
                            ElseIf rsi2Val > 90.0F AndAlso bar.Close < CDec(sma200Val) Then
                                crSide = "Sell"
                            End If
                            If crSide IsNot Nothing Then
                                positionGroupCounter += 1
                                openLegs.Add(New BacktestTrade With {
                                    .PositionGroupId = positionGroupCounter,
                                    .EntryTime = bar.Timestamp,
                                    .EntryPrice = bar.Close,
                                    .Side = crSide,
                                    .Quantity = config.Quantity,
                                    .SignalConfidence = 1.0F
                                })
                            End If
                        End If
                    End If
                    Continue For
                End If

                ' ── SuperTrend signal ──────────────────────────────────────────────
                ' Long on direction flip from −1 to +1; Short on flip from +1 to −1.
                ' SL = SuperTrend line level at entry; TP = 2× ATR(10) from entry.
                If config.StrategyCondition = StrategyConditionType.SuperTrend Then
                    If qlStLine IsNot Nothing AndAlso qlStDir IsNot Nothing Then
                        Dim stDirNow = qlStDir(i)
                        Dim stLineNow = qlStLine(i)
                        If Not (Single.IsNaN(stDirNow) OrElse Single.IsNaN(stLineNow)) Then
                            ' Entry on direction flip (open legs = 0 only)
                            If openLegs.Count = 0 AndAlso qlStPrevDir <> 0.0F Then
                                Dim stSide As String = Nothing
                                If stDirNow = 1.0F AndAlso qlStPrevDir = -1.0F Then
                                    stSide = "Buy"
                                ElseIf stDirNow = -1.0F AndAlso qlStPrevDir = 1.0F Then
                                    stSide = "Sell"
                                End If
                                If stSide IsNot Nothing Then
                                    ' Use pre-calculated ATR(10) for TP sizing
                                    Dim stAtrVal = If(qlStAtr10 IsNot Nothing AndAlso
                                                      i < qlStAtr10.Length AndAlso
                                                      Not Single.IsNaN(qlStAtr10(i)),
                                                      CDec(qlStAtr10(i)), 0D)
                                    positionGroupCounter += 1
                                    openLegs.Add(New BacktestTrade With {
                                        .PositionGroupId = positionGroupCounter,
                                        .EntryTime = bar.Timestamp,
                                        .EntryPrice = bar.Close,
                                        .Side = stSide,
                                        .Quantity = config.Quantity,
                                        .SignalConfidence = 1.0F
                                    })
                                    qlStIsLong = (stSide = "Buy")
                                    ' SL = SuperTrend line; TP = 2× ATR reward
                                    If qlStIsLong Then
                                        qlStOpenSlPrice = CDec(stLineNow)
                                        qlStOpenTpPrice = If(stAtrVal > 0D,
                                                             bar.Close + stAtrVal * 2D,
                                                             bar.Close * 1.02D)
                                    Else
                                        qlStOpenSlPrice = CDec(stLineNow)
                                        qlStOpenTpPrice = If(stAtrVal > 0D,
                                                             bar.Close - stAtrVal * 2D,
                                                             bar.Close * 0.98D)
                                    End If
                                End If
                            End If
                            qlStPrevDir = stDirNow
                        End If
                    End If
                    Continue For
                End If

                ' ── DonchianBreakout signal ────────────────────────────────────────
                ' Long on close > 20-bar upper channel; Short on close < 20-bar lower channel.
                ' Exit when close crosses the 10-bar middle channel in adverse direction.
                If openLegs.Count = 0 AndAlso
                   config.StrategyCondition = StrategyConditionType.DonchianBreakout Then
                    If qlDonUpper20 IsNot Nothing AndAlso qlDonLower20 IsNot Nothing AndAlso qlDonMid10 IsNot Nothing Then
                        Dim donUpperVal = qlDonUpper20(i)
                        Dim donLowerVal = qlDonLower20(i)
                        Dim donMidVal = qlDonMid10(i)
                        Dim prevDonUpperVal = If(i > 0, qlDonUpper20(i - 1), Single.NaN)
                        Dim prevDonLowerVal = If(i > 0, qlDonLower20(i - 1), Single.NaN)
                        If Not (Single.IsNaN(donUpperVal) OrElse Single.IsNaN(donLowerVal) OrElse
                                Single.IsNaN(donMidVal) OrElse Single.IsNaN(prevDonUpperVal) OrElse
                                Single.IsNaN(prevDonLowerVal)) Then
                            Dim donSide As String = Nothing
                            ' Break above prior bar's upper = new breakout long
                            If bar.Close > CDec(prevDonUpperVal) Then
                                donSide = "Buy"
                            ' Break below prior bar's lower = new breakout short
                            ElseIf bar.Close < CDec(prevDonLowerVal) Then
                                donSide = "Sell"
                            End If
                            If donSide IsNot Nothing Then
                                positionGroupCounter += 1
                                openLegs.Add(New BacktestTrade With {
                                    .PositionGroupId = positionGroupCounter,
                                    .EntryTime = bar.Timestamp,
                                    .EntryPrice = bar.Close,
                                    .Side = donSide,
                                    .Quantity = config.Quantity,
                                    .SignalConfidence = 1.0F
                                })
                                qlDonIsLong = (donSide = "Buy")
                                ' Exit when close crosses the 10-bar mid-channel (adverse direction)
                                qlDonOpenMidExit = CDec(donMidVal)
                            End If
                        End If
                    End If
                    Continue For
                End If

                ' ── BbRsiMeanReversion signal ──────────────────────────────────────
                ' Long when close < lower BB(20,2) AND RSI(14) < 30 (dual oversold).
                ' Short when close > upper BB(20,2) AND RSI(14) > 70 (dual overbought).
                ' Exit at middle BB (SMA20) or RSI(14) crosses 50.
                If openLegs.Count = 0 AndAlso
                   config.StrategyCondition = StrategyConditionType.BbRsiMeanReversion Then
                    If qlBbUpper IsNot Nothing AndAlso qlBbLower IsNot Nothing AndAlso qlBbMiddle IsNot Nothing Then
                        Dim bbUpperVal = qlBbUpper(i)
                        Dim bbLowerVal = qlBbLower(i)
                        Dim bbMidVal = qlBbMiddle(i)
                        Dim rsiMrVal = rsi14Series(i)
                        If Not (Single.IsNaN(bbUpperVal) OrElse Single.IsNaN(bbLowerVal) OrElse
                                Single.IsNaN(bbMidVal) OrElse Single.IsNaN(rsiMrVal)) Then
                            Dim mrSide As String = Nothing
                            If bar.Close < CDec(bbLowerVal) AndAlso rsiMrVal < 30.0F Then
                                mrSide = "Buy"
                            ElseIf bar.Close > CDec(bbUpperVal) AndAlso rsiMrVal > 70.0F Then
                                mrSide = "Sell"
                            End If
                            If mrSide IsNot Nothing Then
                                positionGroupCounter += 1
                                openLegs.Add(New BacktestTrade With {
                                    .PositionGroupId = positionGroupCounter,
                                    .EntryTime = bar.Timestamp,
                                    .EntryPrice = bar.Close,
                                    .Side = mrSide,
                                    .Quantity = config.Quantity,
                                    .SignalConfidence = 1.0F
                                })
                                qlBbIsLong = (mrSide = "Buy")
                                ' Exit when price returns to middle BB (SMA20)
                                qlBbOpenMidExit = CDec(bbMidVal)
                            End If
                        End If
                    End If
                    Continue For
                End If

                ' ── EMA/RSI weighted signal — same algorithm as StrategyExecutionEngine ──
                ' Score is computed on every bar (open or flat): used for neutral-confidence
                ' exit when a position is open, and for entry evaluation when flat.
                ' Guarded to avoid running for TripleEmaCascade or MultiConfluence bars.
                If config.StrategyCondition <> StrategyConditionType.TripleEmaCascade AndAlso
                   config.StrategyCondition <> StrategyConditionType.MultiConfluence AndAlso
                   config.StrategyCondition <> StrategyConditionType.ConnorsRsi2 AndAlso
                   config.StrategyCondition <> StrategyConditionType.SuperTrend AndAlso
                   config.StrategyCondition <> StrategyConditionType.DonchianBreakout AndAlso
                   config.StrategyCondition <> StrategyConditionType.BbRsiMeanReversion Then
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

                    ' 4. RSI trending zone — 20 pts
                    ' Mirrors live StrategyExecutionEngine: awards 20 pts when RSI is in the
                    ' 50–70 range (trending bullish, not yet overbought). Zero outside that window.
                    If rsiVal >= 50 AndAlso rsiVal < 70 Then bullScore += 20
                    bullScore = Math.Max(0.0, Math.Min(100.0, bullScore))  ' clamp after RSI contribution

                    ' 5. EMA21 momentum (rising since prior bar) — 10 pts
                    If ema21Now > ema21Prev Then bullScore += 10

                    ' 6. Recent 3 candles: ≥ 2 bullish — 10 pts
                    Dim bullCandles As Integer = 0
                    For c = i - 2 To i
                        If filteredBars(c).IsBullish Then bullCandles += 1
                    Next
                    If bullCandles >= 2 Then bullScore += 10

                    Dim downPct As Double = 100.0 - bullScore

                    ' ── Neutral confidence exit ───────────────────────────────────────────
                    ' Mirrors live EvaluateConfidenceActionsAsync: when the score falls into
                    ' the 40–60% neutral band, close all open legs at bar close.
                    ' TP/SL intrabar price-level fills are handled first (above); this exit
                    ' applies only when neither level was touched this bar.
                    If openLegs.Count > 0 AndAlso
                       bullScore >= 40.0 AndAlso bullScore <= 60.0 Then
                        Dim neutralPositionPnL = 0D
                        For Each leg In openLegs
                            leg.ExitTime = bar.Timestamp
                            leg.ExitPrice = bar.Close
                            leg.ExitReason = "NeutralExit"
                            Dim pnl = BacktestMetrics.CalculatePnL(leg, config)
                            leg.PnL = pnl
                            neutralPositionPnL += pnl
                            trades.Add(leg)
                        Next
                        capital += neutralPositionPnL
                        If capital > peakCapital Then peakCapital = capital
                        Dim neutralDd = peakCapital - capital
                        If neutralDd > maxDrawdown Then maxDrawdown = neutralDd
                        openLegs.Clear()
                    End If

                    ' ── Entry signal — initial entry when flat, scale-in when same direction ──
                    ' Initial entry: fires when no position is open and signal meets threshold.
                    ' Scale-in:      fires when one leg is already open in the same direction;
                    '                capped at one additional entry (two legs max per position).
                    ' ── ADX trend-strength gate (configurable) ───────────────────────
                    ' config.MinAdxThreshold = 0  → gate disabled, all bars evaluated.
                    ' config.MinAdxThreshold = 25 → matches live StrategyExecutionEngine.
                    Dim adxVal = adx14Series(i)
                    Dim adxGate = config.MinAdxThreshold <= 0.0F OrElse
                                  (Not Single.IsNaN(adxVal) AndAlso adxVal >= config.MinAdxThreshold)

                    Dim tradeableSide As String = Nothing
                    Dim sigConf As Single = 0
                    If adxGate Then
                        If bullScore >= minPct Then
                            tradeableSide = "Buy"
                            sigConf = CSng(bullScore) / 100.0F
                        ElseIf downPct >= minPct Then
                            tradeableSide = "Sell"
                            sigConf = CSng(downPct) / 100.0F
                        End If
                    End If

                    If tradeableSide IsNot Nothing Then
                        If openLegs.Count = 0 Then
                            positionGroupCounter += 1
                            openLegs.Add(New BacktestTrade With {
                                .PositionGroupId = positionGroupCounter,
                                .EntryTime = bar.Timestamp,
                                .EntryPrice = bar.Close,
                                .Side = tradeableSide,
                                .Quantity = config.Quantity,
                                .SignalConfidence = sigConf
                            })
                        ElseIf openLegs.Count = 1 AndAlso openLegs(0).Side = tradeableSide Then
                            ' Scale-in: same direction, one additional leg
                            openLegs.Add(New BacktestTrade With {
                                .PositionGroupId = openLegs(0).PositionGroupId,
                                .EntryTime = bar.Timestamp,
                                .EntryPrice = bar.Close,
                                .Side = tradeableSide,
                                .Quantity = config.Quantity,
                                .SignalConfidence = sigConf
                            })
                        End If
                    End If
                End If
            Next

            ' Close any open position at end of data
            If openLegs.Count > 0 Then
                Dim lastBar = filteredBars.Last()
                For Each leg In openLegs
                    leg.ExitTime = lastBar.Timestamp
                    leg.ExitPrice = lastBar.Close
                    leg.ExitReason = "EndOfData"
                    leg.PnL = BacktestMetrics.CalculatePnL(leg, config)
                    capital += leg.PnL.GetValueOrDefault()
                    trades.Add(leg)
                Next
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
                    .SignalConfidence = t.SignalConfidence,
                    .PositionGroupId = t.PositionGroupId
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
