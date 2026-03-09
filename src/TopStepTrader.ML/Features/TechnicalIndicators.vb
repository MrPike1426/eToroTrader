Namespace TopStepTrader.ML.Features

    ''' <summary>
    ''' Pure-math technical indicator calculations.
    ''' All methods take a price series and return a Single array.
    ''' No external dependencies — fully unit-testable.
    ''' </summary>
    Public Module TechnicalIndicators

        ' ── EMA ──────────────────────────────────────────────────────────────

        ''' <summary>Exponential Moving Average. Returns array same length as prices (NaN-padded start).</summary>
        Public Function EMA(prices As IList(Of Decimal), period As Integer) As Single()
            Dim result(prices.Count - 1) As Single
            If prices.Count = 0 OrElse period <= 0 Then Return result

            Dim k = 2.0F / (period + 1.0F)
            Dim firstValid = period - 1

            ' Seed with SMA of first 'period' values
            Dim sum As Double = 0
            For i = 0 To period - 1
                If i >= prices.Count Then Return result
                sum += CDbl(prices(i))
                result(i) = Single.NaN
            Next
            result(firstValid) = CSng(sum / period)

            For i = period To prices.Count - 1
                result(i) = CSng(CDbl(prices(i)) * k + CDbl(result(i - 1)) * (1 - k))
            Next
            Return result
        End Function

        ' ── SMA ──────────────────────────────────────────────────────────────

        Public Function SMA(prices As IList(Of Decimal), period As Integer) As Single()
            Dim result(prices.Count - 1) As Single
            For i = 0 To prices.Count - 1
                result(i) = Single.NaN
            Next
            For i = period - 1 To prices.Count - 1
                Dim sum As Double = 0
                For j = i - period + 1 To i
                    sum += CDbl(prices(j))
                Next
                result(i) = CSng(sum / period)
            Next
            Return result
        End Function

        ' ── RSI ──────────────────────────────────────────────────────────────

        ''' <summary>Relative Strength Index (Wilder smoothing).</summary>
        Public Function RSI(closes As IList(Of Decimal), period As Integer) As Single()
            Dim result(closes.Count - 1) As Single
            For i = 0 To closes.Count - 1
                result(i) = Single.NaN
            Next
            If closes.Count < period + 1 Then Return result

            Dim gains As Double = 0
            Dim losses As Double = 0

            For i = 1 To period
                Dim change = CDbl(closes(i)) - CDbl(closes(i - 1))
                If change >= 0 Then gains += change Else losses -= change
            Next
            Dim avgGain = gains / period
            Dim avgLoss = losses / period

            result(period) = If(avgLoss = 0, 100.0F,
                                CSng(100.0 - 100.0 / (1.0 + avgGain / avgLoss)))

            For i = period + 1 To closes.Count - 1
                Dim change = CDbl(closes(i)) - CDbl(closes(i - 1))
                Dim gain = If(change > 0, change, 0.0)
                Dim loss = If(change < 0, -change, 0.0)
                avgGain = (avgGain * (period - 1) + gain) / period
                avgLoss = (avgLoss * (period - 1) + loss) / period
                result(i) = If(avgLoss = 0, 100.0F,
                               CSng(100.0 - 100.0 / (1.0 + avgGain / avgLoss)))
            Next
            Return result
        End Function

        ' ── MACD ─────────────────────────────────────────────────────────────

        ''' <summary>Returns (macdLine, signalLine, histogram) arrays.</summary>
        Public Function MACD(closes As IList(Of Decimal),
                              Optional fastPeriod As Integer = 12,
                              Optional slowPeriod As Integer = 26,
                              Optional signalPeriod As Integer = 9) As (Line As Single(), Signal As Single(), Histogram As Single())

            Dim fastEma = EMA(closes, fastPeriod)
            Dim slowEma = EMA(closes, slowPeriod)

            Dim macdLine(closes.Count - 1) As Single
            For i = 0 To closes.Count - 1
                If Single.IsNaN(fastEma(i)) OrElse Single.IsNaN(slowEma(i)) Then
                    macdLine(i) = Single.NaN
                Else
                    macdLine(i) = fastEma(i) - slowEma(i)
                End If
            Next

            ' Signal = EMA of MACD line
            Dim validMacd = macdLine.Where(Function(v) Not Single.IsNaN(v)).Select(Function(v) CDec(v)).ToList()
            Dim signalRaw = EMA(validMacd, signalPeriod)

            Dim signalLine(closes.Count - 1) As Single
            Dim histogram(closes.Count - 1) As Single
            Dim validIdx = 0
            For i = 0 To closes.Count - 1
                If Not Single.IsNaN(macdLine(i)) Then
                    If validIdx < signalRaw.Length Then
                        signalLine(i) = signalRaw(validIdx)
                        histogram(i) = If(Single.IsNaN(signalRaw(validIdx)), Single.NaN,
                                          macdLine(i) - signalRaw(validIdx))
                        validIdx += 1
                    End If
                Else
                    signalLine(i) = Single.NaN
                    histogram(i) = Single.NaN
                End If
            Next
            Return (macdLine, signalLine, histogram)
        End Function

        ' ── ATR ──────────────────────────────────────────────────────────────

        ''' <summary>Average True Range (Wilder smoothing).</summary>
        Public Function ATR(highs As IList(Of Decimal),
                             lows As IList(Of Decimal),
                             closes As IList(Of Decimal),
                             period As Integer) As Single()

            Dim n = Math.Min(Math.Min(highs.Count, lows.Count), closes.Count)
            Dim result(n - 1) As Single
            For i = 0 To n - 1
                result(i) = Single.NaN
            Next
            If n < 2 Then Return result

            ' True ranges
            Dim trs(n - 1) As Double
            trs(0) = CDbl(highs(0) - lows(0))
            For i = 1 To n - 1
                Dim hl = CDbl(highs(i) - lows(i))
                Dim hc = Math.Abs(CDbl(highs(i)) - CDbl(closes(i - 1)))
                Dim lc = Math.Abs(CDbl(lows(i)) - CDbl(closes(i - 1)))
                trs(i) = Math.Max(hl, Math.Max(hc, lc))
            Next

            ' Seed ATR with SMA of first 'period' TRs
            Dim sum As Double = 0
            For i = 0 To period - 1
                sum += trs(i)
            Next
            result(period - 1) = CSng(sum / period)

            For i = period To n - 1
                result(i) = CSng((CDbl(result(i - 1)) * (period - 1) + trs(i)) / period)
            Next
            Return result
        End Function

        ' ── VWAP ─────────────────────────────────────────────────────────────

        ''' <summary>Volume-Weighted Average Price (cumulative for the series).</summary>
        Public Function VWAP(highs As IList(Of Decimal),
                              lows As IList(Of Decimal),
                              closes As IList(Of Decimal),
                              volumes As IList(Of Long)) As Single()
            Dim n = Math.Min(Math.Min(highs.Count, lows.Count),
                             Math.Min(closes.Count, volumes.Count))
            Dim result(n - 1) As Single
            Dim cumPV As Double = 0
            Dim cumVol As Double = 0
            For i = 0 To n - 1
                Dim typicalPrice = (CDbl(highs(i)) + CDbl(lows(i)) + CDbl(closes(i))) / 3.0
                cumPV += typicalPrice * volumes(i)
                cumVol += volumes(i)
                result(i) = If(cumVol > 0, CSng(cumPV / cumVol), CSng(closes(i)))
            Next
            Return result
        End Function

        ' ── Bollinger Bands ───────────────────────────────────────────────────

        ''' <summary>Returns (upperBand, middleBand, lowerBand) arrays.</summary>
        Public Function BollingerBands(closes As IList(Of Decimal),
                                       period As Integer,
                                       Optional stdDevMultiplier As Double = 2.0) As (Upper As Single(), Middle As Single(), Lower As Single())
            Dim middle = SMA(closes, period)
            Dim upper(closes.Count - 1) As Single
            Dim lower(closes.Count - 1) As Single

            For i = 0 To closes.Count - 1
                upper(i) = Single.NaN
                lower(i) = Single.NaN
            Next

            For i = period - 1 To closes.Count - 1
                If Single.IsNaN(middle(i)) Then Continue For
                Dim variance As Double = 0
                For j = i - period + 1 To i
                    Dim diff = CDbl(closes(j)) - CDbl(middle(i))
                    variance += diff * diff
                Next
                Dim stdDev = Math.Sqrt(variance / period)
                upper(i) = CSng(CDbl(middle(i)) + stdDevMultiplier * stdDev)
                lower(i) = CSng(CDbl(middle(i)) - stdDevMultiplier * stdDev)
            Next
            Return (upper, middle, lower)
        End Function

        ' ── Helpers ──────────────────────────────────────────────────────────

        ' ── DMI / ADX ────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Directional Movement Index — returns (+DI, -DI, ADX) arrays using
        ''' Wilder smoothing (identical to the ATR and RSI smoothing convention).
        ''' First valid index is 2×period−1 (warm-up matches TradingView default).
        ''' </summary>
        Public Function DMI(highs As IList(Of Decimal),
                            lows As IList(Of Decimal),
                            closes As IList(Of Decimal),
                            Optional period As Integer = 14) As (PlusDI As Single(), MinusDI As Single(), ADX As Single())

            Dim n = Math.Min(Math.Min(highs.Count, lows.Count), closes.Count)
            Dim plusDI(n - 1) As Single
            Dim minusDI(n - 1) As Single
            Dim adxArr(n - 1) As Single
            For i = 0 To n - 1
                plusDI(i) = Single.NaN
                minusDI(i) = Single.NaN
                adxArr(i) = Single.NaN
            Next
            If n < period + 1 Then Return (plusDI, minusDI, adxArr)

            ' ── Raw TR, +DM, -DM ──────────────────────────────────────────────
            Dim trs(n - 1) As Double
            Dim pdms(n - 1) As Double   ' +DM
            Dim mdms(n - 1) As Double   ' -DM
            For i = 1 To n - 1
                Dim hl = CDbl(highs(i) - lows(i))
                Dim hc = Math.Abs(CDbl(highs(i)) - CDbl(closes(i - 1)))
                Dim lc = Math.Abs(CDbl(lows(i)) - CDbl(closes(i - 1)))
                trs(i) = Math.Max(hl, Math.Max(hc, lc))

                Dim upMove = CDbl(highs(i) - highs(i - 1))
                Dim downMove = CDbl(lows(i - 1) - lows(i))
                pdms(i) = If(upMove > downMove AndAlso upMove > 0, upMove, 0.0)
                mdms(i) = If(downMove > upMove AndAlso downMove > 0, downMove, 0.0)
            Next

            ' ── Wilder-smooth TR, +DM, -DM (seed with sum of first `period` values) ──
            Dim smoothTR = trs.Skip(1).Take(period).Sum()
            Dim smoothPDM = pdms.Skip(1).Take(period).Sum()
            Dim smoothMDM = mdms.Skip(1).Take(period).Sum()

            ' ── DX and seed ADX ───────────────────────────────────────────────
            Dim dxArr(n - 1) As Double
            Dim firstValid = period   ' index of first valid DI value (1-based DM array → period index)

            Dim pdi0 = If(smoothTR > 0, CSng(100.0 * smoothPDM / smoothTR), 0.0F)
            Dim mdi0 = If(smoothTR > 0, CSng(100.0 * smoothMDM / smoothTR), 0.0F)
            plusDI(firstValid) = pdi0
            minusDI(firstValid) = mdi0
            Dim diSum0 = CDbl(pdi0) + CDbl(mdi0)
            dxArr(firstValid) = If(diSum0 > 0, 100.0 * Math.Abs(CDbl(pdi0) - CDbl(mdi0)) / diSum0, 0.0)

            ' ── Rolling Wilder smoothing for the rest ──────────────────────────
            Dim adxSeedSum As Double = dxArr(firstValid)
            Dim adxSeedCount As Integer = 1

            For i = firstValid + 1 To n - 1
                smoothTR = smoothTR - smoothTR / period + trs(i)
                smoothPDM = smoothPDM - smoothPDM / period + pdms(i)
                smoothMDM = smoothMDM - smoothMDM / period + mdms(i)

                Dim pdi = If(smoothTR > 0, CSng(100.0 * smoothPDM / smoothTR), 0.0F)
                Dim mdi = If(smoothTR > 0, CSng(100.0 * smoothMDM / smoothTR), 0.0F)
                plusDI(i) = pdi
                minusDI(i) = mdi

                Dim diSum = CDbl(pdi) + CDbl(mdi)
                dxArr(i) = If(diSum > 0, 100.0 * Math.Abs(CDbl(pdi) - CDbl(mdi)) / diSum, 0.0)

                ' Accumulate DX until we have `period` values, then start Wilder ADX
                If adxSeedCount < period Then
                    adxSeedSum += dxArr(i)
                    adxSeedCount += 1
                    If adxSeedCount = period Then
                        ' Seed the ADX with the average of the first `period` DX values
                        adxArr(i) = CSng(adxSeedSum / period)
                    End If
                Else
                    ' Wilder smoothing: ADX(i) = (ADX(i-1)×(period-1) + DX(i)) / period
                    Dim prevAdx = If(Single.IsNaN(adxArr(i - 1)), 0.0F, adxArr(i - 1))
                    adxArr(i) = CSng((CDbl(prevAdx) * (period - 1) + dxArr(i)) / period)
                End If
            Next

            Return (plusDI, minusDI, adxArr)
        End Function

        ' ── Ichimoku Cloud ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Ichimoku Kinkō Hyō — returns Tenkan-sen, Kijun-sen, Senkou Span A and Span B arrays,
        ''' each the same length as the input series.
        ''' SpanA and SpanB are projected <paramref name="displacement"/> bars forward so the
        ''' value at the last index reflects the "current" cloud visible on the chart.
        ''' The Chikou (Lagging) Span is not returned — callers compare
        ''' closes(n-1) vs closes(n-1-displacement) directly.
        ''' </summary>
        Public Function IchimokuCloud(
                highs As IList(Of Decimal),
                lows As IList(Of Decimal),
                closes As IList(Of Decimal),
                Optional tenkanPeriod As Integer = 9,
                Optional kijunPeriod As Integer = 26,
                Optional senkouBPeriod As Integer = 52,
                Optional displacement As Integer = 26) As (Tenkan As Single(), Kijun As Single(), SpanA As Single(), SpanB As Single())

            Dim n = Math.Min(Math.Min(highs.Count, lows.Count), closes.Count)
            Dim tenkan(n - 1) As Single
            Dim kijun(n - 1) As Single
            Dim spanA(n - 1) As Single
            Dim spanB(n - 1) As Single
            For i = 0 To n - 1
                tenkan(i) = Single.NaN
                kijun(i) = Single.NaN
                spanA(i) = Single.NaN
                spanB(i) = Single.NaN
            Next
            If n = 0 Then Return (tenkan, kijun, spanA, spanB)

            ' Compute Tenkan-sen and Kijun-sen (no displacement)
            For i = 0 To n - 1
                If i >= tenkanPeriod - 1 Then
                    Dim hh = CDbl(highs(i))
                    Dim ll = CDbl(lows(i))
                    For j = i - tenkanPeriod + 1 To i
                        If CDbl(highs(j)) > hh Then hh = CDbl(highs(j))
                        If CDbl(lows(j)) < ll Then ll = CDbl(lows(j))
                    Next
                    tenkan(i) = CSng((hh + ll) / 2.0)
                End If

                If i >= kijunPeriod - 1 Then
                    Dim hh = CDbl(highs(i))
                    Dim ll = CDbl(lows(i))
                    For j = i - kijunPeriod + 1 To i
                        If CDbl(highs(j)) > hh Then hh = CDbl(highs(j))
                        If CDbl(lows(j)) < ll Then ll = CDbl(lows(j))
                    Next
                    kijun(i) = CSng((hh + ll) / 2.0)
                End If
            Next

            ' Project SpanA and SpanB forward by `displacement` bars.
            ' SpanA[i + displacement] = (Tenkan[i] + Kijun[i]) / 2
            ' SpanB[i + displacement] = (highest_high(senkouBPeriod, i) + lowest_low(senkouBPeriod, i)) / 2
            For i = 0 To n - 1
                Dim fwd = i + displacement
                If fwd >= n Then Continue For

                If Not Single.IsNaN(tenkan(i)) AndAlso Not Single.IsNaN(kijun(i)) Then
                    spanA(fwd) = CSng((CDbl(tenkan(i)) + CDbl(kijun(i))) / 2.0)
                End If

                If i >= senkouBPeriod - 1 Then
                    Dim hh = CDbl(highs(i))
                    Dim ll = CDbl(lows(i))
                    For j = i - senkouBPeriod + 1 To i
                        If CDbl(highs(j)) > hh Then hh = CDbl(highs(j))
                        If CDbl(lows(j)) < ll Then ll = CDbl(lows(j))
                    Next
                    spanB(fwd) = CSng((hh + ll) / 2.0)
                End If
            Next

            Return (tenkan, kijun, spanA, spanB)
        End Function

        ' ── Stochastic RSI ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Stochastic RSI: applies the Stochastic formula to RSI(rsiPeriod) values.
        ''' Returns (%K, %D) normalised to the range 0.0–1.0.
        ''' %K = (RSI - MinRSI_stochPeriod) / (MaxRSI_stochPeriod - MinRSI_stochPeriod)
        ''' %D = signalPeriod-bar SMA of %K (signal / smoothing line).
        ''' When the RSI range within the stoch window is effectively zero, %K is pinned at 0.5.
        ''' </summary>
        Public Function StochasticRSI(
                closes As IList(Of Decimal),
                Optional rsiPeriod As Integer = 14,
                Optional stochPeriod As Integer = 14,
                Optional signalPeriod As Integer = 3) As (K As Single(), D As Single())

            Dim n = closes.Count
            Dim kArr(n - 1) As Single
            Dim dArr(n - 1) As Single
            For i = 0 To n - 1
                kArr(i) = Single.NaN
                dArr(i) = Single.NaN
            Next
            If n < rsiPeriod + stochPeriod Then Return (kArr, dArr)

            Dim rsiValues = RSI(closes, rsiPeriod)

            ' Compute %K over a rolling stochPeriod window of RSI values
            For i = stochPeriod - 1 To n - 1
                If Single.IsNaN(rsiValues(i)) Then Continue For
                Dim minRsi = Single.MaxValue
                Dim maxRsi = Single.MinValue
                For j = i - stochPeriod + 1 To i
                    If Not Single.IsNaN(rsiValues(j)) Then
                        If rsiValues(j) < minRsi Then minRsi = rsiValues(j)
                        If rsiValues(j) > maxRsi Then maxRsi = rsiValues(j)
                    End If
                Next
                Dim rng = maxRsi - minRsi
                kArr(i) = If(rng < 0.0001F, 0.5F,
                              CSng((CDbl(rsiValues(i)) - CDbl(minRsi)) / CDbl(rng)))
            Next

            ' %D = signalPeriod-period SMA of %K
            For i = signalPeriod - 1 To n - 1
                Dim sum As Double = 0
                Dim cnt = 0
                For j = i - signalPeriod + 1 To i
                    If Not Single.IsNaN(kArr(j)) Then
                        sum += CDbl(kArr(j))
                        cnt += 1
                    End If
                Next
                If cnt = signalPeriod Then
                    dArr(i) = CSng(sum / signalPeriod)
                End If
            Next

            Return (kArr, dArr)
        End Function

        ' ── WaveTrend (Market Cipher B simulation) ───────────────────────────────

        ''' <summary>
        ''' WaveTrend oscillator — simulates the momentum "blue wave" of Market Cipher B.
        ''' WT1 oscillates around zero; ±60 are the standard overbought/oversold thresholds.
        ''' WT2 is a <paramref name="signalSmooth"/>-bar SMA of WT1 (signal / smoothing line).
        ''' A Green Dot fires when WT1 crosses above WT2 near an oversold trough (WT1 &lt; -60).
        ''' A Red Dot fires when WT1 crosses below WT2 near an overbought peak (WT1 &gt; +60).
        ''' Formula:
        '''   HLC3 = (H + L + C) / 3
        '''   ESA  = EMA(HLC3, channelLength)
        '''   D    = EMA(|HLC3 − ESA|, channelLength)
        '''   CI   = (HLC3 − ESA) / (0.015 × D)
        '''   WT1  = EMA(CI, avgLength)
        '''   WT2  = SMA(WT1, signalSmooth)
        ''' </summary>
        Public Function WaveTrend(
                highs As IList(Of Decimal),
                lows As IList(Of Decimal),
                closes As IList(Of Decimal),
                Optional channelLength As Integer = 10,
                Optional avgLength As Integer = 21,
                Optional signalSmooth As Integer = 4) As (Wt1 As Single(), Wt2 As Single())

            Dim n = Math.Min(Math.Min(highs.Count, lows.Count), closes.Count)
            Dim wt1(n - 1) As Single
            Dim wt2(n - 1) As Single
            For i = 0 To n - 1
                wt1(i) = Single.NaN
                wt2(i) = Single.NaN
            Next
            If n < channelLength + avgLength + signalSmooth Then Return (wt1, wt2)

            ' HLC3 — typical price
            Dim hlc3(n - 1) As Decimal
            For i = 0 To n - 1
                hlc3(i) = (highs(i) + lows(i) + closes(i)) / 3D
            Next

            ' ESA = EMA(HLC3, channelLength)
            Dim esaArr = EMA(hlc3, channelLength)

            ' D = EMA(|HLC3 − ESA|, channelLength)
            Dim dInput(n - 1) As Decimal
            For i = 0 To n - 1
                dInput(i) = If(Single.IsNaN(esaArr(i)), 0D, Math.Abs(hlc3(i) - CDec(esaArr(i))))
            Next
            Dim dArr = EMA(dInput, channelLength)

            ' CI = (HLC3 − ESA) / (0.015 × D);  guard against near-zero D to avoid ÷0
            Dim ciArr(n - 1) As Decimal
            For i = 0 To n - 1
                If Single.IsNaN(esaArr(i)) OrElse Single.IsNaN(dArr(i)) OrElse dArr(i) < 0.00001F Then
                    ciArr(i) = 0D
                Else
                    ciArr(i) = (hlc3(i) - CDec(esaArr(i))) / (0.015D * CDec(dArr(i)))
                End If
            Next

            ' WT1 = EMA(CI, avgLength)
            wt1 = EMA(ciArr, avgLength)

            ' WT2 = rolling SMA(WT1, signalSmooth)
            For i = signalSmooth - 1 To n - 1
                If Single.IsNaN(wt1(i)) Then Continue For
                Dim wtSum As Double = 0
                Dim cnt As Integer = 0
                For j = i - signalSmooth + 1 To i
                    If Not Single.IsNaN(wt1(j)) Then
                        wtSum += CDbl(wt1(j))
                        cnt += 1
                    End If
                Next
                If cnt = signalSmooth Then wt2(i) = CSng(wtSum / signalSmooth)
            Next

            Return (wt1, wt2)
        End Function

        ' ── Helpers ──────────────────────────────────────────────────────────────

        ''' <summary>Get the last non-NaN value from an indicator array.</summary>
        Public Function LastValid(series As Single()) As Single
            For i = series.Length - 1 To 0 Step -1
                If Not Single.IsNaN(series(i)) Then Return series(i)
            Next
            Return 0.0F
        End Function

        ''' <summary>Get the second-to-last non-NaN value (for momentum/diff calculations).</summary>
        Public Function PreviousValid(series As Single()) As Single
            Dim count = 0
            For i = series.Length - 1 To 0 Step -1
                If Not Single.IsNaN(series(i)) Then
                    count += 1
                    If count = 2 Then Return series(i)
                End If
            Next
            Return 0.0F
        End Function

    End Module

End Namespace
