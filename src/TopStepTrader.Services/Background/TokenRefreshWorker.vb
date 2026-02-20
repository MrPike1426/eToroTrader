Imports System.Threading
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API

Namespace TopStepTrader.Services.Background

    ''' <summary>
    ''' Proactively refreshes the JWT token 5 minutes before expiry.
    ''' Runs as an IHostedService on a 60-second polling interval.
    ''' </summary>
    Public Class TokenRefreshWorker
        Implements IHostedService, IDisposable

        Private ReadOnly _tokenManager As TokenManager
        Private ReadOnly _logger As ILogger(Of TokenRefreshWorker)
        Private _timer As System.Threading.Timer
        Private _disposed As Boolean = False

        Public Sub New(tokenManager As TokenManager, logger As ILogger(Of TokenRefreshWorker))
            _tokenManager = tokenManager
            _logger = logger
        End Sub

        Public Function StartAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StartAsync
            _logger.LogInformation("TokenRefreshWorker started")
            _timer = New System.Threading.Timer(
                AddressOf DoWork, Nothing,
                TimeSpan.FromSeconds(10),   ' Initial delay
                TimeSpan.FromMinutes(1))    ' Poll interval
            Return Task.CompletedTask
        End Function

        Private Async Sub DoWork(state As Object)
            Try
                If Not _tokenManager.IsAuthenticated Then
                    _logger.LogDebug("Token refresh worker: no valid token, skipping")
                    Return
                End If

                ' Refresh if expiring within 5 minutes
                If _tokenManager.TokenExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5) Then
                    _logger.LogInformation("Token expiring soon — proactive refresh")
                    Await _tokenManager.ForceRefreshAsync()
                End If
            Catch ex As Exception
                _logger.LogError(ex, "Error during proactive token refresh")
            End Try
        End Sub

        Public Function StopAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StopAsync
            _logger.LogInformation("TokenRefreshWorker stopping")
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
