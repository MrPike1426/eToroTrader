Namespace TopStepTrader.Core.Trading

    ''' <summary>
    ''' Shared list of favourite / frequently-traded contracts.
    ''' Used by AI Trade, Backtest, and Test Trade tabs so the dropdown
    ''' favourites stay in sync without duplicating data across ViewModels.
    ''' </summary>
    Public Class FavouriteContracts

        ''' <summary>
        ''' Returns the default favourites list.
        ''' Update this single location when contracts roll to new expiry months.
        ''' </summary>
        Public Shared Function GetDefaults() As List(Of FavouriteContract)
            Return New List(Of FavouriteContract) From {
                New FavouriteContract("CON.F.US.MGC.J26", "MGCJ26 — Micro Gold", 0.1D, 1.0D, 10D),
                New FavouriteContract("CON.F.US.MCL.J26", "MCLJ26 — Micro Oil", 0.01D, 1.0D, 100D),
                New FavouriteContract("CON.F.US.MNQ.H26", "MNQH26 — Micro Nasdaq-100", 0.25D, 0.5D, 2D),
                New FavouriteContract("CON.F.US.MES.H26", "MESH26 — Micro S&P 500", 0.25D, 1.25D, 5D)
            }
        End Function

    End Class

    ''' <summary>
    ''' Lightweight record for a favourite contract.
    ''' PointValue = dollar value of one full point move (e.g. MES = $5/pt, MNQ = $2/pt).
    ''' </summary>
    Public Class FavouriteContract
        Public Property ContractId As String
        Public Property Name As String
        Public Property TickSize As Decimal
        Public Property TickValue As Decimal
        ''' <summary>Dollar value per 1.0 point move on this contract.</summary>
        Public Property PointValue As Decimal

        Public Sub New(id As String, name As String, tickSz As Decimal, tickVal As Decimal, ptVal As Decimal)
            ContractId = id
            Name = name
            TickSize = tickSz
            TickValue = tickVal
            PointValue = ptVal
        End Sub
    End Class

End Namespace
