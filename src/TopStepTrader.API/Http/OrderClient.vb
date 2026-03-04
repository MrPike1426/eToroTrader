Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Executes trading operations against the eToro DEMO trading endpoints.
    ''' All calls use /api/v1/trading/execution/demo/* and /api/v1/trading/info/demo/*.
    ''' </summary>
    Public Class OrderClient
        Inherits EToroHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       credentials As EToroCredentialsProvider,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of OrderClient))
            MyBase.New(httpClientFactory, credentials, rateLimiter, logger)
            _settings = options.Value
        End Sub

        ''' <summary>
        ''' Opens a market position by specifying units (shares/contracts).
        ''' StopLossRate and TakeProfitRate are set natively on the position — no bracket orders needed.
        ''' POST /api/v1/trading/execution/demo/market-open-orders/by-units
        ''' </summary>
        Public Function PlaceOrderByUnitsAsync(
            request As OpenMarketOrderByUnitsRequest,
            Optional cancel As CancellationToken = Nothing) As Task(Of PlaceOrderResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/execution/demo/market-open-orders/by-units"
            Return PostAsync(Of OpenMarketOrderByUnitsRequest, PlaceOrderResponse)(endpoint, request, cancel)
        End Function

        ''' <summary>
        ''' Opens a market position by specifying a USD cash amount.
        ''' POST /api/v1/trading/execution/demo/market-open-orders/by-amount
        ''' </summary>
        Public Function PlaceOrderByAmountAsync(
            request As OpenMarketOrderByAmountRequest,
            Optional cancel As CancellationToken = Nothing) As Task(Of PlaceOrderResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/execution/demo/market-open-orders/by-amount"
            Return PostAsync(Of OpenMarketOrderByAmountRequest, PlaceOrderResponse)(endpoint, request, cancel)
        End Function

        ''' <summary>
        ''' Closes a position fully or partially.
        ''' POST /api/v1/trading/execution/demo/market-close-orders/positions/{positionId}
        ''' </summary>
        Public Function ClosePositionAsync(
            positionId As Long,
            Optional unitsToDeduct As Double? = Nothing,
            Optional cancel As CancellationToken = Nothing) As Task(Of PlaceOrderResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/execution/demo/market-close-orders/positions/{positionId}"
            Dim body = New ClosePositionRequest With {.UnitsToDeduct = unitsToDeduct}
            Return PostAsync(Of ClosePositionRequest, PlaceOrderResponse)(endpoint, body, cancel)
        End Function

        ''' <summary>
        ''' Cancels a pending (not yet executed) market-open order.
        ''' DELETE /api/v1/trading/execution/demo/market-open-orders/{orderId}
        ''' </summary>
        Public Function CancelPendingOpenOrderAsync(
            orderId As Long,
            Optional cancel As CancellationToken = Nothing) As Task(Of CancelOrderResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/execution/demo/market-open-orders/{orderId}"
            Return DeleteAsync(Of CancelOrderResponse)(endpoint, cancel)
        End Function

        ''' <summary>
        ''' Cancels a pending limit (market-if-touched) order.
        ''' DELETE /api/v1/trading/execution/demo/limit-orders/{orderId}
        ''' </summary>
        Public Function CancelLimitOrderAsync(
            orderId As Long,
            Optional cancel As CancellationToken = Nothing) As Task(Of CancelOrderResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/execution/demo/limit-orders/{orderId}"
            Return DeleteAsync(Of CancelOrderResponse)(endpoint, cancel)
        End Function

        ''' <summary>
        ''' Retrieves order status and the positions opened by a specific order.
        ''' GET /api/v1/trading/info/demo/orders/{orderId}
        ''' </summary>
        Public Function GetOrderInfoAsync(
            orderId As Long,
            Optional cancel As CancellationToken = Nothing) As Task(Of OrderInfoResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/info/demo/orders/{orderId}"
            Return GetAsync(Of OrderInfoResponse)(endpoint, cancel)
        End Function

        ''' <summary>
        ''' Returns live open positions from the portfolio, mapped to the OrderSearchResponse shape.
        ''' Used by OrderService.GetLiveWorkingOrdersAsync / TryGetOrderFillPriceAsync.
        ''' GET /api/v1/trading/info/demo/portfolio
        ''' </summary>
        Public Async Function SearchOrdersAsync(
            Optional cancel As CancellationToken = Nothing) As Task(Of OrderSearchResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/info/demo/portfolio"
            Dim portfolio = Await GetAsync(Of PortfolioResponse)(endpoint, cancel)

            Dim orders = portfolio?.ClientPortfolio?.Positions?.
                Select(Function(p) New OrderDto With {
                    .Id = p.PositionId,
                    .ContractId = p.InstrumentId.ToString(),
                    .Status = 1,
                    .AvgFillPrice = p.OpenRate
                }).ToList()

            Return New OrderSearchResponse With {
                .Success = True,
                .Orders = If(orders, New List(Of OrderDto)())
            }
        End Function

    End Class

End Namespace
