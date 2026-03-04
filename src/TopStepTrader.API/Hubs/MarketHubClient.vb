Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Hubs

    ''' <summary>
    ''' eToro WebSocket client stub for real-time market quotes and bars.
    ''' eToro uses a standard WebSocket (wss://), not SignalR.
    ''' Full WebSocket implementation: see eToro API docs /api-reference/websocket/topics.md
    '''
    ''' Current behaviour: exposes the same event/subscribe interface as before so the
    ''' rest of the application compiles unchanged. Real-time streaming is not yet wired.
    ''' Use GET /api/v1/market-data/instruments/rates for current prices,
    ''' and GET /api/v1/market-data/instruments/{id}/history/candles/... for bar data.
    ''' </summary>
    Public Class MarketHubClient
        Implements IAsyncDisposable

        Private ReadOnly _logger As ILogger(Of MarketHubClient)
        Private ReadOnly _subscribedContracts As New HashSet(Of String)

        Public Event QuoteReceived As EventHandler(Of MarketQuoteEventArgs)
        Public Event BarReceived As EventHandler(Of MarketBarEventArgs)

        Public Sub New(options As IOptions(Of ApiSettings),
                       credentials As EToroCredentialsProvider,
                       logger As ILogger(Of MarketHubClient))
            _logger = logger
        End Sub

        Public ReadOnly Property State As String
            Get
                Return "Disconnected"
            End Get
        End Property

        Public Function StartAsync(Optional cancel As CancellationToken = Nothing) As Task
            _logger.LogInformation("MarketHubClient: eToro WebSocket not yet implemented. " &
                                   "Use REST market-data endpoints for quotes and candle history.")
            Return Task.CompletedTask
        End Function

        Public Function SubscribeContractAsync(contractId As String,
                                               Optional cancel As CancellationToken = Nothing) As Task
            _subscribedContracts.Add(contractId)
            _logger.LogDebug("MarketHubClient: registered subscription for {ContractId} (WebSocket not active)", contractId)
            Return Task.CompletedTask
        End Function

        Public Function UnsubscribeContractAsync(contractId As String,
                                                  Optional cancel As CancellationToken = Nothing) As Task
            _subscribedContracts.Remove(contractId)
            Return Task.CompletedTask
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function

    End Class

    ' ─── DTOs kept for interface compatibility ───────────────────────────────────

    Public Class MarketQuoteData
        Public Property ContractId As String = String.Empty
        Public Property Bp As Double
        Public Property Ap As Double
        Public Property Lp As Double
        Public Property Bs As Integer
        Public Property AskSize As Integer
        Public Property V As Long
    End Class

    Public Class MarketBarData
        Public Property ContractId As String = String.Empty
        Public Property T As Long
        Public Property O As Double
        Public Property H As Double
        Public Property L As Double
        Public Property C As Double
        Public Property V As Long
    End Class

    Public Class MarketQuoteEventArgs
        Inherits EventArgs
        Public ReadOnly Property Quote As Quote
        Public Sub New(quote As Quote)
            Me.Quote = quote
        End Sub
    End Class

    Public Class MarketBarEventArgs
        Inherits EventArgs
        Public ReadOnly Property Bar As MarketBar
        Public Sub New(bar As MarketBar)
            Me.Bar = bar
        End Sub
    End Class

End Namespace
