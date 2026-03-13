Namespace TopStepTrader.Core.Trading

    ''' <summary>
    ''' The 6 favourite eToro instruments shown at the top of every instrument selector.
    ''' ContractId  = eToro ticker symbol (internalSymbolFull).
    ''' InstrumentId = eToro immutable numeric instrument ID (never changes).
    ''' TickSize / TickValue / PointValue are CFD minimums — adjust if eToro revises them.
    ''' </summary>
    Public Class FavouriteContracts

        Public Shared Function GetDefaults() As List(Of FavouriteContract)
            Return New List(Of FavouriteContract) From {
                New FavouriteContract("OIL",     "Oil (Non Expiry)",             17, 0.01D, 0.01D, 1D, minSlDistPct:=0.5D),
                New FavouriteContract("GOLD",    "Gold (Non Expiry)",            18, 0.01D, 0.01D, 1D, minSlDistPct:=0.3D),
                New FavouriteContract("UK100",   "UK100 Index (Non Expiry)",     30, 0.01D, 0.01D, 1D, minSlDistPct:=0.5D),
                New FavouriteContract("NSDQ100", "NASDAQ100 Index (Non Expiry)", 28, 0.01D, 0.01D, 1D, minSlDistPct:=0.5D),
                New FavouriteContract("SPX500",  "SPX500 Index (Non Expiry)",    27, 0.01D, 0.01D, 1D, minSlDistPct:=0.5D),
                New FavouriteContract("BTC",     "Bitcoin",                   100000, 1.0D,  1.0D,  1D, minSlDistPct:=1.0D),
                New FavouriteContract("ETH",     "Ethereum",                  100001, 0.1D,  0.1D,  1D, minSlDistPct:=1.0D),
                New FavouriteContract("XRP",     "Ripple",                    100003, 0.0001D, 0.0001D, 1D, minSlDistPct:=1.5D),
                New FavouriteContract("SOL",     "Solana",                    100063, 0.01D, 0.01D, 1D, minSlDistPct:=1.0D),
                New FavouriteContract("BNB",     "BNB",                       100030, 0.1D,  0.1D,  1D, minSlDistPct:=1.0D)
            }
        End Function

        ''' <summary>Returns the FavouriteContract matching <paramref name="symbol"/> (case-insensitive), or Nothing.</summary>
        Public Shared Function TryGetBySymbol(symbol As String) As FavouriteContract
            Return GetDefaults().FirstOrDefault(
                Function(f) String.Equals(f.ContractId, symbol, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>Returns the FavouriteContract with the given numeric instrumentId, or Nothing.</summary>
        Public Shared Function TryGetById(instrumentId As Integer) As FavouriteContract
            Return GetDefaults().FirstOrDefault(Function(f) f.InstrumentId = instrumentId)
        End Function

    End Class

    Public Class FavouriteContract
        Public Property ContractId As String      ' Ticker symbol, e.g. "GOLD"
        Public Property Name As String
        Public Property InstrumentId As Integer   ' eToro numeric instrument ID
        Public Property TickSize As Decimal
        Public Property TickValue As Decimal
        Public Property PointValue As Decimal
        ''' <summary>
        ''' eToro minimum trade notional in USD (position size including leverage).
        ''' Commodities, currencies and indices = $1,000.  BTC = $25 on eToro demo.
        ''' </summary>
        Public Property MinNotionalUsd As Decimal
        ''' <summary>Default leverage multiplier for this instrument (1 = no leverage).</summary>
        Public Property DefaultLeverage As Integer
        ''' <summary>
        ''' Approximate minimum stop-loss distance as a percentage of instrument price.
        ''' eToro does NOT document this value — it is enforced silently by the broker.
        ''' These are conservative estimates based on observed platform behaviour:
        '''   CFD indices (SPX500, NSDQ100, UK100): ~0.5 %  at standard leverage
        '''   Commodities (GOLD, OIL):               ~0.3-0.5 %
        '''   Crypto (BTC, ETH, SOL, BNB):           ~1.0 %
        '''   High-volatility crypto (XRP):           ~1.5 %
        ''' Use for pre-submission validation and audit logging only.
        ''' The primary mitigation is IsTslEnabled=True, which delegates distance
        ''' enforcement to eToro and is always respected correctly.
        ''' </summary>
        Public Property MinSlDistancePct As Decimal

        ''' <summary>Returns the minimum absolute SL distance in price units for a given current price.</summary>
        Public Function MinSlDistancePoints(currentPrice As Decimal) As Decimal
            If currentPrice <= 0D OrElse MinSlDistancePct <= 0D Then Return 0D
            Return Math.Round(currentPrice * MinSlDistancePct / 100D, 4)
        End Function

        Public Sub New(id As String, name As String, instrumentId As Integer,
                       tickSz As Decimal, tickVal As Decimal, ptVal As Decimal,
                       Optional minNotional As Decimal = 1000D,
                       Optional defaultLeverage As Integer = 1,
                       Optional minSlDistPct As Decimal = 0.5D)
            ContractId = id
            Me.Name = name
            Me.InstrumentId = instrumentId
            TickSize = tickSz
            TickValue = tickVal
            PointValue = ptVal
            MinNotionalUsd = minNotional
            Me.DefaultLeverage = defaultLeverage
            MinSlDistancePct = minSlDistPct
        End Sub
    End Class

End Namespace
