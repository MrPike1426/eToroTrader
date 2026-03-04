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
                New FavouriteContract("OIL",     "Oil (Non Expiry)",             17, 0.01D, 0.01D, 1D),
                New FavouriteContract("GOLD",    "Gold (Non Expiry)",            18, 0.01D, 0.01D, 1D),
                New FavouriteContract("UK100",   "UK100 Index (Non Expiry)",     30, 0.01D, 0.01D, 1D),
                New FavouriteContract("NSDQ100", "NASDAQ100 Index (Non Expiry)", 28, 0.01D, 0.01D, 1D),
                New FavouriteContract("SPX500",  "SPX500 Index (Non Expiry)",    27, 0.01D, 0.01D, 1D),
                New FavouriteContract("BTC",     "Bitcoin",                   100000, 1.0D,  1.0D,  1D)
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

        Public Sub New(id As String, name As String, instrumentId As Integer,
                       tickSz As Decimal, tickVal As Decimal, ptVal As Decimal)
            ContractId = id
            Me.Name = name
            Me.InstrumentId = instrumentId
            TickSize = tickSz
            TickValue = tickVal
            PointValue = ptVal
        End Sub
    End Class

End Namespace
