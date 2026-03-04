Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.Http
Imports TopStepTrader.API.Hubs
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Trading

    ''' <summary>
    ''' Implements IOrderService using the eToro DEMO trading API.
    ''' Key differences from TopStepX:
    '''   - Authentication: static header keys (no JWT)
    '''   - Order identification: eToro uses instrumentId (integer), not string contractId
    '''   - Stop Loss / Take Profit: set natively on the open order — no separate bracket orders
    '''   - Positions: identified by positionId (stored in Order.ExternalPositionId)
    '''   - Close: POST market-close-orders/positions/{positionId}
    '''   - Cancel pending: DELETE market-open-orders/{orderId}
    ''' </summary>
    Public Class OrderService
        Implements IOrderService

        Private ReadOnly _orderClient As OrderClient
        Private ReadOnly _orderRepo As OrderRepository
        Private ReadOnly _logger As ILogger(Of OrderService)

        Public Event OrderFilled As EventHandler(Of OrderFilledEventArgs) Implements IOrderService.OrderFilled
        Public Event OrderRejected As EventHandler(Of OrderRejectedEventArgs) Implements IOrderService.OrderRejected
        Public Event PositionUpdated As EventHandler(Of TopStepTrader.Core.Events.PositionUpdateEventArgs) Implements IOrderService.PositionUpdated

        Public Sub New(orderClient As OrderClient,
                       hubClient As UserHubClient,
                       orderRepo As OrderRepository,
                       logger As ILogger(Of OrderService))
            _orderClient = orderClient
            _orderRepo = orderRepo
            _logger = logger
        End Sub

        Public Async Function PlaceOrderAsync(order As Order) As Task(Of Order) _
            Implements IOrderService.PlaceOrderAsync

            order.PlacedAt = DateTimeOffset.UtcNow
            order.Status = OrderStatus.Pending
            order.Id = Await _orderRepo.SaveOrderAsync(order)

            _logger.LogInformation(
                "Placing eToro DEMO {Side} x{Qty} InstrumentId={InstrId} SL={SL} TP={TP}",
                order.Side, order.Quantity, order.InstrumentId, order.StopLossRate, order.TakeProfitRate)

            Dim caughtEx As Exception = Nothing
            Try
                Dim response As PlaceOrderResponse

                If order.Amount.HasValue Then
                    ' Open by USD amount
                    Dim req = New OpenMarketOrderByAmountRequest With {
                        .InstrumentId = order.InstrumentId,
                        .IsBuy = (order.Side = OrderSide.Buy),
                        .Leverage = If(order.Leverage > 0, order.Leverage, 1),
                        .Amount = CDbl(order.Amount.Value),
                        .StopLossRate = If(order.StopLossRate.HasValue, CDbl(order.StopLossRate.Value), CType(Nothing, Double?)),
                        .TakeProfitRate = If(order.TakeProfitRate.HasValue, CDbl(order.TakeProfitRate.Value), CType(Nothing, Double?))
                    }
                    response = Await _orderClient.PlaceOrderByAmountAsync(req)
                Else
                    ' Open by units/quantity
                    Dim req = New OpenMarketOrderByUnitsRequest With {
                        .InstrumentId = order.InstrumentId,
                        .IsBuy = (order.Side = OrderSide.Buy),
                        .Leverage = If(order.Leverage > 0, order.Leverage, 1),
                        .Units = CDbl(order.Quantity),
                        .StopLossRate = If(order.StopLossRate.HasValue, CDbl(order.StopLossRate.Value), CType(Nothing, Double?)),
                        .TakeProfitRate = If(order.TakeProfitRate.HasValue, CDbl(order.TakeProfitRate.Value), CType(Nothing, Double?))
                    }
                    response = Await _orderClient.PlaceOrderByUnitsAsync(req)
                End If

                If response.Success Then
                    order.ExternalOrderId = response.OrderId
                    order.Status = OrderStatus.Working
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    _logger.LogInformation("eToro order accepted. orderId={Ext}", order.ExternalOrderId)

                    ' Resolve positionId asynchronously so callers can close by position
                    Await ResolvePositionIdAsync(order)
                Else
                    order.Status = OrderStatus.Rejected
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status)
                    _logger.LogWarning("eToro order rejected: {Msg}", response.ErrorMessage)
                    RaiseEvent OrderRejected(Me, New OrderRejectedEventArgs(order, If(response.ErrorMessage, "Unknown")))
                End If
            Catch ex As Exception
                caughtEx = ex
            End Try

            If caughtEx IsNot Nothing Then
                order.Status = OrderStatus.Rejected
                Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status)
                _logger.LogError(caughtEx, "Exception placing eToro order")
                RaiseEvent OrderRejected(Me, New OrderRejectedEventArgs(order, caughtEx.Message))
            End If

            Return order
        End Function

        ''' <summary>
        ''' Queries the order-info endpoint to discover the positionId opened by this order.
        ''' Stores the positionId in Order.ExternalPositionId for later close calls.
        ''' </summary>
        Private Async Function ResolvePositionIdAsync(order As Order) As Task
            If Not order.ExternalOrderId.HasValue Then Return
            Try
                Dim info = Await _orderClient.GetOrderInfoAsync(order.ExternalOrderId.Value)
                Dim pos = info?.Positions?.FirstOrDefault()
                If pos IsNot Nothing Then
                    order.ExternalPositionId = pos.PositionId
                    order.FillPrice = CDec(pos.OpenRate)
                    order.FilledAt = DateTimeOffset.UtcNow
                    order.Status = OrderStatus.Filled
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    _logger.LogInformation("Position resolved: positionId={PosId} fillPrice={Price}",
                                           order.ExternalPositionId, order.FillPrice)
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "Could not resolve positionId for orderId={Id}", order.ExternalOrderId)
            End Try
        End Function

        ''' <summary>
        ''' Closes an open position (by ExternalPositionId) or cancels a pending order (by ExternalOrderId).
        ''' </summary>
        Public Async Function CancelOrderAsync(orderId As Long) As Task(Of Boolean) _
            Implements IOrderService.CancelOrderAsync

            ' Try to close by positionId first (open position)
            Dim dbOrder = (Await _orderRepo.GetOpenOrdersAsync()).
                FirstOrDefault(Function(o) o.ExternalOrderId = orderId OrElse o.ExternalPositionId = orderId)

            Dim caughtEx As Exception = Nothing
            Dim success = False
            Try
                If dbOrder IsNot Nothing AndAlso dbOrder.ExternalPositionId.HasValue Then
                    _logger.LogInformation("Closing eToro position positionId={PosId}", dbOrder.ExternalPositionId)
                    Dim closeResp = Await _orderClient.ClosePositionAsync(dbOrder.ExternalPositionId.Value)
                    success = closeResp.Success
                Else
                    _logger.LogInformation("Cancelling eToro pending open order orderId={Id}", orderId)
                    Dim cancelResp = Await _orderClient.CancelPendingOpenOrderAsync(orderId)
                    success = cancelResp.Success
                End If
            Catch ex As Exception
                caughtEx = ex
            End Try

            If caughtEx IsNot Nothing Then
                _logger.LogWarning(caughtEx, "Exception cancelling/closing eToro order {Id}", orderId)
                Return False
            End If

            If success AndAlso dbOrder IsNot Nothing Then
                Await _orderRepo.UpdateOrderStatusAsync(dbOrder.Id, OrderStatus.Cancelled)
            End If
            Return success
        End Function

        Public Async Function CancelAllOpenOrdersAsync() As Task _
            Implements IOrderService.CancelAllOpenOrdersAsync
            _logger.LogWarning("CancelAllOpenOrders — closing all known open eToro positions")
            Dim openOrders = Await _orderRepo.GetOpenOrdersAsync()
            Dim tasks = openOrders.
                Where(Function(o) o.ExternalOrderId.HasValue OrElse o.ExternalPositionId.HasValue).
                Select(Function(o) CancelOrderAsync(
                    If(o.ExternalPositionId.HasValue, o.ExternalPositionId.Value, o.ExternalOrderId.Value)))
            Await Task.WhenAll(tasks)
        End Function

        Public Async Function GetOpenOrdersAsync(accountId As Long) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetOpenOrdersAsync
            Return Await _orderRepo.GetOpenOrdersAsync(accountId)
        End Function

        Public Async Function GetOrderHistoryAsync(accountId As Long,
                                                    from As DateTime,
                                                    [to] As DateTime) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetOrderHistoryAsync
            Return Await _orderRepo.GetOrderHistoryAsync(accountId, from, [to])
        End Function

        Public Async Function TryGetOrderFillPriceAsync(externalOrderId As Long,
                                                         accountId As Long,
                                                         Optional cancel As CancellationToken = Nothing) As Task(Of Decimal?) _
            Implements IOrderService.TryGetOrderFillPriceAsync
            Try
                Dim resp = Await _orderClient.SearchOrdersAsync(cancel)
                Dim match = resp?.Orders?.FirstOrDefault(Function(o) o.Id = externalOrderId)
                If match IsNot Nothing AndAlso match.AvgFillPrice.HasValue Then
                    Return CDec(match.AvgFillPrice.Value)
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "Could not retrieve fill price for orderId={Id}", externalOrderId)
            End Try
            Return Nothing
        End Function

        Public Async Function GetLiveWorkingOrdersAsync(accountId As Long,
                                                         contractId As String,
                                                         Optional cancel As CancellationToken = Nothing) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetLiveWorkingOrdersAsync
            Const ApiStatusWorking As Integer = 1
            Try
                Dim resp = Await _orderClient.SearchOrdersAsync(cancel)
                If resp?.Success = True AndAlso resp.Orders IsNot Nothing Then
                    Dim working = resp.Orders _
                        .Where(Function(o) o.Status = ApiStatusWorking AndAlso
                                           String.Equals(o.ContractId, contractId,
                                                         StringComparison.OrdinalIgnoreCase)) _
                        .Select(Function(o) New Order With {
                            .ExternalOrderId = o.Id,
                            .ExternalPositionId = o.Id,
                            .ContractId = o.ContractId,
                            .Status = OrderStatus.Working
                        }).ToList()
                    _logger.LogDebug("GetLiveWorkingOrders: {Count} open position(s) for {Contract}",
                                     working.Count, contractId)
                    Return working
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "GetLiveWorkingOrdersAsync failed for instrument {ContractId}", contractId)
            End Try
            Return Enumerable.Empty(Of Order)()
        End Function

    End Class

End Namespace
