Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API.Http

    Public Class AuthClient
        Inherits ProjectXHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       tokenManager As TokenManager,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of AuthClient))
            MyBase.New(httpClientFactory, tokenManager, rateLimiter, logger)
            _settings = options.Value
        End Sub

        Public Async Function LoginWithKeyAsync(
            Optional cancel As CancellationToken = Nothing) As Task(Of AuthResponse)

            Dim request = New LoginKeyRequest With {
                .UserName = _settings.UserName,
                .ApiKey = _settings.ApiKey
            }
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Auth/loginKey"
            Return Await PostAsync(Of LoginKeyRequest, AuthResponse)(endpoint, request, cancel:=cancel)
        End Function

        Public Async Function ValidateTokenAsync(
            Optional cancel As CancellationToken = Nothing) As Task(Of AuthResponse)

            Dim endpoint = $"{_settings.RestBaseUrl}/api/Auth/validate"
            ' Validate uses an empty body
            Return Await PostAsync(Of Object, AuthResponse)(endpoint, New Object(), cancel:=cancel)
        End Function

    End Class

End Namespace
