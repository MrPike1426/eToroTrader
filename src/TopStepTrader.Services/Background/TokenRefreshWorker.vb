Imports System.Threading
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging

Namespace TopStepTrader.Services.Background

    ''' <summary>
    ''' No-op worker — eToro uses static API key headers and has no JWT token to refresh.
    ''' Kept to avoid removing the class from DI registration and to surface a clear log message.
    ''' </summary>
    Public Class TokenRefreshWorker
        Implements IHostedService

        Private ReadOnly _logger As ILogger(Of TokenRefreshWorker)

        Public Sub New(logger As ILogger(Of TokenRefreshWorker))
            _logger = logger
        End Sub

        Public Function StartAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StartAsync
            _logger.LogInformation("TokenRefreshWorker: eToro uses static keys — no token refresh needed.")
            Return Task.CompletedTask
        End Function

        Public Function StopAsync(cancellationToken As CancellationToken) As Task _
            Implements IHostedService.StopAsync
            Return Task.CompletedTask
        End Function

    End Class

End Namespace
