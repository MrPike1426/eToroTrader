Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Hubs

    ''' <summary>
    ''' eToro WebSocket client stub for real-time user events (fills, account updates, positions).
    ''' eToro uses a standard WebSocket (wss://), not SignalR.
    ''' Full WebSocket implementation: see eToro API docs /api-reference/websocket/topics.md
    '''
    ''' Current behaviour: exposes the same event interface as before so the rest of the
    ''' application compiles unchanged. Real-time events are not yet wired.
    ''' The eToro REST portfolio endpoint can be polled as an alternative.
    ''' </summary>
    Public Class UserHubClient
        Implements IAsyncDisposable

        Private ReadOnly _logger As ILogger(Of UserHubClient)

        Public Event OrderFillReceived As EventHandler(Of OrderFillEventArgs)
        Public Event AccountUpdated As EventHandler(Of AccountUpdateEventArgs)
        Public Event PositionUpdated As EventHandler(Of PositionUpdateEventArgs)

        Public Sub New(options As IOptions(Of ApiSettings),
                       credentials As EToroCredentialsProvider,
                       logger As ILogger(Of UserHubClient))
            _logger = logger
        End Sub

        Public ReadOnly Property State As String
            Get
                Return "Disconnected"
            End Get
        End Property

        Public Function StartAsync(Optional cancel As CancellationToken = Nothing) As Task
            _logger.LogInformation("UserHubClient: eToro WebSocket not yet implemented. " &
                                   "Poll /api/v1/trading/info/demo/portfolio for account state.")
            Return Task.CompletedTask
        End Function

        Public Function StopAsync(Optional cancel As CancellationToken = Nothing) As Task
            Return Task.CompletedTask
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function

    End Class

    ' ─── DTOs kept for interface compatibility ───────────────────────────────────

    Public Class UserOrderFillData
        Public Property OrderId As Long
        Public Property AccountId As Long
        Public Property ContractId As String = String.Empty
        Public Property Side As Integer
        Public Property FillPrice As Double
        Public Property FillSize As Integer
        Public Property Status As Integer
        Public Property Timestamp As Long
    End Class

    Public Class UserAccountData
        Public Property AccountId As Long
        Public Property Balance As Double
        Public Property OpenPnL As Double
        Public Property DailyPnL As Double
    End Class

    Public Class UserPositionData
        Public Property AccountId As Long
        Public Property ContractId As String = String.Empty
        Public Property NetPos As Integer
        Public Property AvgPrice As Double
        Public Property OpenPnL As Double
    End Class

    Public Class OrderFillEventArgs
        Inherits EventArgs
        Public ReadOnly Property FillData As UserOrderFillData
        Public Sub New(data As UserOrderFillData)
            FillData = data
        End Sub
    End Class

    Public Class AccountUpdateEventArgs
        Inherits EventArgs
        Public ReadOnly Property AccountData As UserAccountData
        Public Sub New(data As UserAccountData)
            AccountData = data
        End Sub
    End Class

    Public Class PositionUpdateEventArgs
        Inherits EventArgs
        Public ReadOnly Property PositionData As UserPositionData
        Public Sub New(data As UserPositionData)
            PositionData = data
        End Sub
    End Class

End Namespace
