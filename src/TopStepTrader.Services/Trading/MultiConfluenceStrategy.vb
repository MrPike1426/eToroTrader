Imports TopStepTrader.Core.Enums
Imports TopStepTrader.ML.Features

Namespace TopStepTrader.Services.Trading

    ''' <summary>
    ''' Result returned by <see cref="MultiConfluenceStrategy.Evaluate"/>.
    ''' </summary>
    Public Class MultiConfluenceResult
        ''' <summary>Trade direction signalled, or Nothing when no signal fires.</summary>
        Public Property Side As OrderSide? = Nothing
        ''' <summary>Percentage of bullish confluence conditions met (0–100).</summary>
        Public Property BullScore As Integer = 0
        ''' <summary>Percentage of bearish confluence conditions met (0–100).</summary>
        Public Property BearScore As Integer = 0
        ''' <summary>ATR(14) value — drives dynamic SL/TP sizing.</summary>
        Public Property AtrValue As Decimal = 0D
        ''' <summary>
        ''' Ichimoku cloud edge to use as the SL candidate price.
        ''' Cloud bottom for Long entries; cloud top for Short entries.
        ''' Used by <see cref="StrategyExecutionEngine"/> to tighten SL vs the ATR-based level.
        ''' Nothing when cloud values are not yet available.
        ''' </summary>
        Public Property CloudEdgeSl As Decimal? = Nothing
        ''' <summary>Single-line summary of all indicator values for the execution log.</summary>
        Public Property StatusLine As String = String.Empty
        ''' <summary>Raw ADX(14) value at bar-check time. Forwarded to ConfidenceUpdatedEventArgs so the UI card can display it.</summary>
        Public Property AdxValue As Single = 0F
        ' ── Extended indicator snapshot for the Hydra grid display ──────────────
        Public Property Cloud1 As Decimal = 0D         ' higher of SpanA / SpanB
        Public Property Cloud2 As Decimal = 0D         ' lower of SpanA / SpanB
        Public Property Tenkan As Decimal = 0D
        Public Property Kijun As Decimal = 0D
        Public Property Ema21 As Decimal = 0D
        Public Property Ema50 As Decimal = 0D
        Public Property PlusDI As Single = 0F
        Public Property MinusDI As Single = 0F
        Public Property MacdHist As Single = 0F
        Public Property MacdHistPrev As Single = 0F
        Public Property StochRsiK As Single = 0F
        Public Property LongCount As Integer = 0
        Public Property ShortCount As Integer = 0
    End Class

    ''' <summary>
    ''' Multi-Confluence Engine for commodities trading via the eToro API.
    '''
    ''' Combines seven independent signals on 15-minute bars:
    '''   1. Ichimoku Cloud (9 / 26 / 52 / displacement 26)
    '''   2. EMA 21  (primary trend)
    '''   3. EMA 50  (big-picture trend)
    '''   4. MACD histogram (12 / 26 / 9)
    '''   5. Stochastic RSI (14 / 14)
    '''   6. DMI / ADX (14) trend-strength gate
    '''   7. Ichimoku Lagging Span (Chikou) confirmation
    '''
    ''' ALL seven long conditions must align for a Long entry;
    ''' all seven short conditions must align for a Short entry.
    ''' SL = min(1.5×ATR, Ichimoku cloud edge); TP = 2:1 reward-to-risk.
    ''' Minimum <see cref="MinBarsRequired"/> bars required for full warm-up.
    ''' </summary>
    Public NotInheritable Class MultiConfluenceStrategy

        Private Sub New()
        End Sub

        ' ── Ichimoku periods (per specification) ──────────────────────────────────
        Private Const TenkanPeriod As Integer = 9
        Private Const KijunPeriod As Integer = 26
        Private Const SenkouBPeriod As Integer = 52
        Private Const IchimokuDisplacement As Integer = 26

        ''' <summary>
        ''' Minimum number of bars required for all indicators to fully warm up.
        ''' Senkou Span B needs senkouBPeriod(52) + displacement(26) = 78 bars minimum;
        ''' an 80-bar buffer provides a small safety margin.
        ''' </summary>
        Public Const MinBarsRequired As Integer = 80

        ''' <summary>
        ''' Evaluates all seven confluence conditions and returns a trade signal.
        ''' Returns a <see cref="MultiConfluenceResult"/> with Side = Nothing when no signal fires.
        ''' </summary>
        Public Shared Function Evaluate(
                highs As IList(Of Decimal),
                lows As IList(Of Decimal),
                closes As IList(Of Decimal)) As MultiConfluenceResult

            Dim result As New MultiConfluenceResult()
            Dim n = closes.Count

            If n < MinBarsRequired Then
                result.StatusLine = $"Warming up — {n}/{MinBarsRequired} bars required"
                Return result
            End If

            ' ── Ichimoku Cloud ────────────────────────────────────────────────────
            Dim ichi = TechnicalIndicators.IchimokuCloud(highs, lows, closes,
                TenkanPeriod, KijunPeriod, SenkouBPeriod, IchimokuDisplacement)

            Dim spanANow = ichi.SpanA(n - 1)
            Dim spanBNow = ichi.SpanB(n - 1)

            If Single.IsNaN(spanANow) OrElse Single.IsNaN(spanBNow) Then
                result.StatusLine = $"Ichimoku Span B warming up — need {MinBarsRequired}+ bars"
                Return result
            End If

            Dim tenkanNow = ichi.Tenkan(n - 1)
            Dim kijunNow = ichi.Kijun(n - 1)
            Dim cloudTop = CDec(Math.Max(spanANow, spanBNow))
            Dim cloudBottom = CDec(Math.Min(spanANow, spanBNow))
            Dim lastClose = closes(n - 1)

            ' ── Lagging span: current close vs price 26 bars ago ──────────────────
            ' Chikou confirmation: close must be above (Long) / below (Short) the price
            ' that appeared 26 bars ago on the chart.
            Dim lagIdx = n - 1 - IchimokuDisplacement
            Dim lagClose = If(lagIdx >= 0, closes(lagIdx), Decimal.MinValue)

            ' ── EMA 21 / EMA 50 ───────────────────────────────────────────────────
            Dim ema21Arr = TechnicalIndicators.EMA(closes, 21)
            Dim ema50Arr = TechnicalIndicators.EMA(closes, 50)
            Dim ema21Now = TechnicalIndicators.LastValid(ema21Arr)
            Dim ema50Now = TechnicalIndicators.LastValid(ema50Arr)

            ' ── DMI / ADX (14) ────────────────────────────────────────────────────
            Dim dmi = TechnicalIndicators.DMI(highs, lows, closes)
            Dim plusDINow = TechnicalIndicators.LastValid(dmi.PlusDI)
            Dim minusDINow = TechnicalIndicators.LastValid(dmi.MinusDI)
            Dim adxNow = TechnicalIndicators.LastValid(dmi.ADX)

            ' ── MACD (12, 26, 9) ──────────────────────────────────────────────────
            Dim macd = TechnicalIndicators.MACD(closes)
            Dim histNow = TechnicalIndicators.LastValid(macd.Histogram)
            Dim histPrev = TechnicalIndicators.PreviousValid(macd.Histogram)

            ' ── Stochastic RSI (14 / 14) ──────────────────────────────────────────
            Dim stochRsi = TechnicalIndicators.StochasticRSI(closes)
            Dim stochKNow = TechnicalIndicators.LastValid(stochRsi.K)

            ' ── ATR (14) — for SL/TP sizing ───────────────────────────────────────
            Dim atrArr = TechnicalIndicators.ATR(highs, lows, closes, 14)
            result.AtrValue = CDec(TechnicalIndicators.LastValid(atrArr))

            ' ── Long conditions (all 7 must be True) ──────────────────────────────
            Dim tenkanKijunValid = Not Single.IsNaN(tenkanNow) AndAlso Not Single.IsNaN(kijunNow)

            Dim lc1 = (lastClose > cloudTop)                                     ' 1. Price above cloud
            Dim lc2 = (lastClose > CDec(ema21Now))                               ' 2. Price above EMA 21
            Dim lc3 = (tenkanKijunValid AndAlso tenkanNow > kijunNow)            ' 3. Tenkan-sen > Kijun-sen
            Dim lc4 = (lagIdx >= 0 AndAlso lastClose > lagClose)                 ' 4. Chikou above price 26 bars ago
            Dim lc5 = (adxNow >= 19.9F AndAlso plusDINow > minusDINow)           ' 5. ADX
            Dim lc6 = (histNow > 0 AndAlso histNow > histPrev _
                       AndAlso Not Single.IsNaN(histPrev))                       ' 6. MACD histogram positive and rising
            Dim lc7 = (Not Single.IsNaN(stochKNow) AndAlso stochKNow < 0.8F)    ' 7. StochRSI K < 0.8

            ' ── Short conditions (all 7 must be True) ─────────────────────────────
            Dim sc1 = (lastClose < cloudBottom)                                  ' 1. Price below cloud
            Dim sc2 = (lastClose < CDec(ema21Now))                               ' 2. Price below EMA 21
            Dim sc3 = (tenkanKijunValid AndAlso tenkanNow < kijunNow)            ' 3. Tenkan-sen < Kijun-sen
            Dim sc4 = (lagIdx >= 0 AndAlso lastClose < lagClose)                 ' 4. Chikou below price 26 bars ago
            Dim sc5 = (adxNow >= 19.9F AndAlso minusDINow > plusDINow)           ' 5. ADX
            Dim sc6 = (histNow < 0 AndAlso histNow < histPrev _
                       AndAlso Not Single.IsNaN(histPrev))                       ' 6. MACD histogram negative and falling
            Dim sc7 = (Not Single.IsNaN(stochKNow) AndAlso stochKNow > 0.2F)    ' 7. StochRSI K > 0.2

            Dim longCount = {lc1, lc2, lc3, lc4, lc5, lc6, lc7}.Count(Function(c) c)
            Dim shortCount = {sc1, sc2, sc3, sc4, sc5, sc6, sc7}.Count(Function(c) c)

            result.BullScore = CInt(longCount / 7 * 100)
            result.BearScore = CInt(shortCount / 7 * 100)
            result.AdxValue = adxNow
            result.Cloud1 = cloudTop
            result.Cloud2 = cloudBottom
            result.Tenkan = If(Single.IsNaN(tenkanNow), 0D, CDec(tenkanNow))
            result.Kijun = If(Single.IsNaN(kijunNow), 0D, CDec(kijunNow))
            result.Ema21 = CDec(ema21Now)
            result.Ema50 = CDec(ema50Now)
            result.PlusDI = plusDINow
            result.MinusDI = minusDINow
            result.MacdHist = histNow
            result.MacdHistPrev = If(Single.IsNaN(histPrev), 0F, histPrev)
            result.StochRsiK = If(Single.IsNaN(stochKNow), 0F, stochKNow)
            result.LongCount = longCount
            result.ShortCount = shortCount

            ' ── Build diagnostic status line ───────────────────────────────────────
            result.StatusLine =
                $"Close={lastClose:F4} | Cloud=[{cloudBottom:F4}–{cloudTop:F4}] | " &
                $"T={CDec(tenkanNow):F4} K={CDec(kijunNow):F4} | " &
                $"EMA21={CDec(ema21Now):F4} EMA50={CDec(ema50Now):F4} | " &
                $"ADX={adxNow:F1} DI+={plusDINow:F1} DI-={minusDINow:F1} | " &
                $"MACD-H={histNow:F4}(prev={histPrev:F4}) | StochRSI={stochKNow:F3} | " &
                $"Long={longCount}/7 Short={shortCount}/7"

            ' ── Signal: all 7 conditions met in one direction ─────────────────────
            If longCount = 7 Then
                result.Side = OrderSide.Buy
                result.CloudEdgeSl = cloudBottom    ' cloud floor = Long SL candidate
            ElseIf shortCount = 7 Then
                result.Side = OrderSide.Sell
                result.CloudEdgeSl = cloudTop       ' cloud ceiling = Short SL candidate
            End If

            Return result
        End Function

    End Class

End Namespace
