Namespace TopStepTrader.Core.Trading

    ''' <summary>
    ''' Shared list of favourite / frequently-traded eToro instruments.
    ''' ContractId holds the ticker symbol; InstrumentId holds the eToro numeric ID.
    ''' Resolve the numeric ID via GET /api/v1/market-data/search?internalSymbolFull=TICKER.
    ''' Update InstrumentId values once you have resolved them from the eToro API.
    ''' </summary>
    Public Class FavouriteContracts

        Public Shared Function GetDefaults() As List(Of FavouriteContract)
            Return New List(Of FavouriteContract) From {
                New FavouriteContract("GOLD",    "Gold",         1001, 0.01D, 0.01D, 1D),
                New FavouriteContract("OIL",     "Crude Oil",    1002, 0.01D, 0.01D, 1D),
                New FavouriteContract("NSDQ100", "Nasdaq-100",   1003, 0.01D, 0.01D, 1D),
                New FavouriteContract("SPX500",  "S&P 500",      1004, 0.01D, 0.01D, 1D)
            }
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
