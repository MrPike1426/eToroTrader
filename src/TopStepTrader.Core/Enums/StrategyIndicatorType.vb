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
    End Enum

End Namespace
