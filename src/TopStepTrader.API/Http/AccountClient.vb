Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API.Http

    Public Class AccountClient
        Inherits ProjectXHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       tokenManager As TokenManager,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of AccountClient))
            MyBase.New(httpClientFactory, tokenManager, rateLimiter, logger)
            _settings = options.Value
        End Sub

        Public Function SearchAccountsAsync(
            Optional onlyActive As Boolean = True,
            Optional cancel As CancellationToken = Nothing) As Task(Of AccountSearchResponse)

            Dim request = New AccountSearchRequest With {.OnlyActiveAccounts = onlyActive}
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Account/search"
            Return PostAsync(Of AccountSearchRequest, AccountSearchResponse)(endpoint, request, cancel:=cancel)
        End Function

    End Class

End Namespace
