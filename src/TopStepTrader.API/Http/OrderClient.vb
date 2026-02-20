Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API.Http

    Public Class OrderClient
        Inherits ProjectXHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       tokenManager As TokenManager,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of OrderClient))
            MyBase.New(httpClientFactory, tokenManager, rateLimiter, logger)
            _settings = options.Value
        End Sub

        Public Function PlaceOrderAsync(request As PlaceOrderRequest,
                                        Optional cancel As CancellationToken = Nothing) As Task(Of PlaceOrderResponse)
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Order/place"
            Return PostAsync(Of PlaceOrderRequest, PlaceOrderResponse)(endpoint, request, cancel:=cancel)
        End Function

        Public Function CancelOrderAsync(orderId As Long,
                                         Optional cancel As CancellationToken = Nothing) As Task(Of CancelOrderResponse)
            Dim request = New CancelOrderRequest With {.OrderId = orderId}
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Order/cancel"
            Return PostAsync(Of CancelOrderRequest, CancelOrderResponse)(endpoint, request, cancel:=cancel)
        End Function

        Public Function SearchOrdersAsync(accountId As Long,
                                          Optional startTimestamp As Long? = Nothing,
                                          Optional endTimestamp As Long? = Nothing,
                                          Optional cancel As CancellationToken = Nothing) As Task(Of OrderSearchResponse)
            Dim request = New SearchOrderRequest With {
                .AccountId = accountId,
                .StartTimestamp = startTimestamp,
                .EndTimestamp = endTimestamp
            }
            Dim endpoint = $"{_settings.RestBaseUrl}/api/Order/search"
            Return PostAsync(Of SearchOrderRequest, OrderSearchResponse)(endpoint, request, cancel:=cancel)
        End Function

    End Class

End Namespace
