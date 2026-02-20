Imports System.Threading
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Services.Market

Namespace TopStepTrader.Services.Background

    ''' <summary>
    ''' Periodically triggers ML signal generation for the active contract.
    ''' Pulls recent bars from the DB and invokes ISignalService.GenerateSignalAsync.
    ''' Default interval: every 5 minutes (aligned with 5-min bar close).
    ''' </summary>
    Public Class SignalGenerationWorker
        Implements IHostedService, IDisposable

        Private ReadOnly _signalService As ISignalService
        Private ReadOnly _ingestionService As BarIngestionService
        Private ReadOnly _logger As ILogger(Of SignalGenerationWorker)
        Private _timer As System.Threading.Timer
        Private _disposed As Boolean = False

        Public Property ContractId As Integer = 0
        Public Property Timeframe As BarTimeframe = BarTimeframe.FiveMinute

        Public Sub New(signalService As ISignalService,
                       ingestionService As BarIngestionService,
                       logger As ILogger(Of SignalGenerationWorker))
            _signalService = signalService
            _ingestionService = ingestionService
            _logger = logger
        End Sub

        Public Function StartAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StartAsync
            _logger.LogInformation("SignalGenerationWorker started")
            _timer = New System.Threading.Timer(
                AddressOf DoWork, Nothing,
                TimeSpan.FromSeconds(60),   ' Initial delay
                TimeSpan.FromMinutes(5))    ' Poll interval
            Return Task.CompletedTask
        End Function

        Private Async Sub DoWork(state As Object)
            If ContractId = 0 Then
                _logger.LogDebug("SignalGenerationWorker: no contract configured, skipping")
                Return
            End If
            Try
                ' Get the most recent bars for the contract
                Dim bars = Await _ingestionService.GetBarsForMLAsync(ContractId, Timeframe, maxBars:=100)
                If bars.Count < 30 Then
                    _logger.LogDebug("Insufficient bars ({N}) for signal generation", bars.Count)
                    Return
                End If
                Await _signalService.GenerateSignalAsync(ContractId, bars)
            Catch ex As Exception
                _logger.LogError(ex, "SignalGenerationWorker: error generating signal for contract {Id}", ContractId)
            End Try
        End Sub

        Public Function StopAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StopAsync
            _logger.LogInformation("SignalGenerationWorker stopping")
            _timer?.Change(Timeout.Infinite, 0)
            Return Task.CompletedTask
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _timer?.Dispose()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
