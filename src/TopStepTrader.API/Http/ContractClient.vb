Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API.Http

    Public Class ContractClient
        Inherits ProjectXHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       tokenManager As TokenManager,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of ContractClient))
            MyBase.New(httpClientFactory, tokenManager, rateLimiter, logger)
            _settings = options.Value
        End Sub

        Public Function GetAvailableContractsAsync(
            Optional searchText As String = "",
            Optional cancel As CancellationToken = Nothing) As Task(Of ContractAvailableResponse)

            Dim request = New ContractAvailableRequest With {
                .Live = True,
                .SearchText = searchText
            }
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Contract/available"
            Return PostAsync(Of ContractAvailableRequest, ContractAvailableResponse)(endpoint, request, cancel:=cancel)
        End Function

    End Class

End Namespace
