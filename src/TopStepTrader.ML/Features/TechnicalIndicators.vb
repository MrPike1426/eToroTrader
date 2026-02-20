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
