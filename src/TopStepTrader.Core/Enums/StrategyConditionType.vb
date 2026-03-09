Namespace TopStepTrader.Core.Enums

    ''' <summary>
    ''' Entry condition that must be met before the strategy fires an order.
    ''' </summary>
    Public Enum StrategyConditionType
        ''' <summary>Entire candle (High + Low) is outside the Bollinger Bands.</summary>
        FullCandleOutsideBands = 0
        ''' <summary>Close price crosses outside the Bollinger Bands.</summary>
        CloseOutsideBands = 1
        ''' <summary>RSI drops below 30 (oversold) — triggers a Long.</summary>
        RSIOversold = 2
        ''' <summary>RSI rises above 70 (overbought) — triggers a Short.</summary>
        RSIOverbought = 3
        ''' <summary>Faster EMA crosses above slower EMA — triggers a Long.</summary>
        EMACrossAbove = 4
        ''' <summary>Faster EMA crosses below slower EMA — triggers a Short.</summary>
        EMACrossBelow = 5
        ''' <summary>
        ''' Six-signal weighted score: EMA21/EMA50 crossover (25%), price vs EMA21 (20%),
        ''' price vs EMA50 (15%), RSI gradient (20%), EMA21 momentum (10%), recent candles (10%).
        ''' Buy when score &gt; 60%, Sell when score &lt; 40%.
        ''' </summary>
        EmaRsiWeightedScore = 6
        ''' <summary>
        ''' 3-EMA Cascade (Sniper): EMA8/EMA21/EMA50 on 1-minute bars.
        ''' Long when EMA8 crosses above EMA21 AND price is above rising EMA50.
        ''' Short when EMA8 crosses below EMA21 AND price is below falling EMA50.
        ''' Supports pyramiding scale-in up to 10 contracts.
        ''' </summary>
        TripleEmaCascade = 7
        ''' <summary>
        ''' Multi-Confluence Engine: Ichimoku Cloud (9/26/52) + EMA21/50 + MACD(12/26/9) +
        ''' Stochastic RSI(14) + DMI/ADX(14). Designed for 15-minute commodity bars.
        ''' ALL seven long conditions must align for a Long; all seven short conditions for a Short.
        ''' SL = min(1.5×ATR, Ichimoku cloud edge); TP = 2:1 reward-to-risk.
        ''' </summary>
        MultiConfluence = 8
        ''' <summary>
        ''' LULT Divergence: WaveTrend (Market Cipher B) Anchor/Trigger momentum-price divergence.
        ''' 6-step confirmation gate on 5-minute NQ bars.
        ''' SL = trigger wave extreme ± ATR-scaled tick buffer; TP = 2R.
        ''' Time-filtered to 11:00–17:00 UTC (London + NY pre-market, 07:00–13:00 EST/EDT).
        ''' </summary>
        LultDivergence = 9
    End Enum

End Namespace
