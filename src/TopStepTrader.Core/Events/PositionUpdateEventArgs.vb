Namespace TopStepTrader.Core.Events

    Public Class PositionUpdateEventArgs
        Inherits EventArgs

        Public ReadOnly Property ContractId As String
        Public ReadOnly Property NetPosition As Integer
        Public ReadOnly Property AveragePrice As Decimal

        Public Sub New(contractId As String, netPos As Integer, avgPrice As Decimal)
            Me.ContractId = contractId
            Me.NetPosition = netPos
            Me.AveragePrice = avgPrice
        End Sub
    End Class

End Namespace
