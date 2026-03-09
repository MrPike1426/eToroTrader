Namespace TopStepTrader.Core.Enums

    ''' <summary>
    ''' Technical indicator used by a trading strategy.
    ''' </summary>
    Public Enum StrategyIndicatorType
        BollingerBands = 0
        RSI = 1
        MACD = 2
        EMA = 3
        ''' <summary>Combined EMA21/EMA50/RSI14 weighted scoring — same signals as Test Trade tab.</summary>
        EmaRsiCombined = 4
        ''' <summary>Triple EMA cascade (EMA8/EMA21/EMA50) on 1-minute bars — Sniper strategy.</summary>
        TripleEma = 5
        ''' <summary>
        ''' Multi-Confluence Engine: Ichimoku Cloud + EMA21/50 + MACD + Stochastic RSI + DMI/ADX.
        ''' All seven conditions must align before an entry signal fires.
        ''' </summary>
        MultiConfluence = 6
        ''' <summary>
        ''' LULT Divergence: WaveTrend (Market Cipher B simulation) Anchor/Trigger wave divergence.
        ''' 6-step gate — Anchor (WT1 ≷ ±60) → Trigger (shallower) → price divergence →
        ''' Green/Red Dot (WT1×WT2 cross) → engulfing volume candle.
        ''' Optimised for NQ 5-minute bars; time-filtered to 11:00–17:00 UTC (London + NY pre-market).
        ''' </summary>
        LultDivergence = 7
    End Enum

End Namespace
