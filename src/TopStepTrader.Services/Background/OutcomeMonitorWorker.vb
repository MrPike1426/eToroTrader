Imports System.Threading
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Services.Feedback

Namespace TopStepTrader.Services.Background

    ''' <summary>
    ''' Background worker that runs every 5 minutes to:
    '''   1. Resolve open trade outcomes by checking bar prices.
    '''   2. Check rolling win-rate and trigger automatic retraining when it falls below threshold.
    ''' Retraining is only triggered when enough outcomes exist AND win-rate drops below MinWinRateThreshold.
    ''' Uses IServiceScopeFactory so Scoped services (OutcomeTracker, IModelTrainingService) are resolved
    ''' correctly from within this Singleton hosted service.
    ''' </summary>
    Public Class OutcomeMonitorWorker
        Implements IHostedService, IDisposable

        Private ReadOnly _scopeFactory As IServiceScopeFactory
        Private ReadOnly _logger As ILogger(Of OutcomeMonitorWorker)
        Private _timer As System.Threading.Timer
        Private _disposed As Boolean = False

        ' Retrain when rolling win-rate drops below this threshold
        Private Const MinWinRateThreshold As Single = 0.45F
        ' Don't retrain until we have at least this many outcomes
        Private Const MinOutcomesForRetrain As Integer = 20

        Public Sub New(scopeFactory As IServiceScopeFactory,
                       logger As ILogger(Of OutcomeMonitorWorker))
            _scopeFactory = scopeFactory
            _logger = logger
        End Sub

        Public Function StartAsync(cancel As CancellationToken) As Task _
            Implements IHostedService.StartAsync
            _logger.LogInformation("OutcomeMonitorWorker starting (5-minute scan interval)")
            _timer = New System.Threading.Timer(
                AddressOf DoWork, Nothing, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5))
            Return Task.CompletedTask
        End Function

        Public Function StopAsync(cancel As CancellationToken) As Task _
            Implements IHostedService.StopAsync
            _timer?.Change(Timeout.Infinite, 0)
            Return Task.CompletedTask
        End Function

        Private Async Sub DoWork(state As Object)
            Dim cts = New CancellationTokenSource(TimeSpan.FromMinutes(4))
            Try
                Using scope = _scopeFactory.CreateScope()
                    Dim outcomeTracker = scope.ServiceProvider.GetRequiredService(Of OutcomeTracker)()
                    Dim trainingService = scope.ServiceProvider.GetRequiredService(Of IModelTrainingService)()

                    ' Step 1: Resolve any open outcomes whose exit window has passed
                    Await outcomeTracker.ResolveOpenOutcomesAsync(cts.Token)

                    ' Step 2: Check win-rate and trigger retrain if needed
                    Await CheckAndRetrain(trainingService, cts.Token)
                End Using

            Catch ex As OperationCanceledException
                ' Timer ran over budget — skip this cycle
            Catch ex As Exception
                _logger.LogError(ex, "OutcomeMonitorWorker error")
            Finally
                cts.Dispose()
            End Try
        End Sub

        Private Async Function CheckAndRetrain(trainingService As IModelTrainingService,
                                               cancel As CancellationToken) As Task
            Dim count = Await trainingService.GetOutcomeCountAsync()
            If count < MinOutcomesForRetrain Then
                _logger.LogDebug("Only {Count} outcomes so far, need {Min} before auto-retrain",
                                  count, MinOutcomesForRetrain)
                Return
            End If

            Dim winRate = Await trainingService.GetRollingWinRateAsync(50)
            _logger.LogInformation("Rolling win-rate (last 50 trades): {Rate:P1}", winRate)

            If winRate < MinWinRateThreshold Then
                _logger.LogWarning(
                    "Win-rate {Rate:P1} dropped below threshold {Threshold:P1} — triggering retrain",
                    winRate, MinWinRateThreshold)
                Try
                    Dim metrics = Await trainingService.RetrainAsync(cancel)
                    If metrics IsNot Nothing Then
                        _logger.LogInformation("Auto-retrain complete: Accuracy={Acc:P1}", metrics.Accuracy)
                    End If
                Catch ex As Exception
                    _logger.LogError(ex, "Auto-retrain failed")
                End Try
            End If
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _timer?.Dispose()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
