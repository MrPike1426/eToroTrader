Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.Http
Imports TopStepTrader.API.Hubs
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Trading

    ''' <summary>
    ''' Implements IOrderService — places/cancels orders via the ProjectX API
    ''' and maintains an audit trail in the database.
    ''' The contract ID passed into PlaceOrderAsync is a numeric ID;
    ''' we store it as-is but the API wants a string contract code.
    ''' Callers must set Order.Notes to the string contract code when available.
    ''' </summary>
    Public Class OrderService
        Implements IOrderService

        Private ReadOnly _orderClient As OrderClient
        Private ReadOnly _hubClient As UserHubClient
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
            _hubClient = hubClient
            _orderRepo = orderRepo
            _logger = logger

            ' Bridge SignalR events to IOrderService consumers
            AddHandler _hubClient.PositionUpdated, AddressOf OnHubPositionUpdated
        End Sub

        Private Sub OnHubPositionUpdated(sender As Object, e As API.Hubs.PositionUpdateEventArgs)
            ' Map API-layer DTO to Core-layer EventArgs
            ' UserPositionData: ContractId, NetPos, AvgPrice, etc.
            Dim coreArgs As New TopStepTrader.Core.Events.PositionUpdateEventArgs(e.PositionData.ContractId,
                                                        e.PositionData.NetPos,
                                                        e.PositionData.AvgPrice)
            RaiseEvent PositionUpdated(Me, coreArgs)
        End Sub

        Public Async Function PlaceOrderAsync(order As Order) As Task(Of Order) _
            Implements IOrderService.PlaceOrderAsync

            ' Map domain order to API request
            ' Notes field carries the string contractId from the contract search
            Dim contractCode = If(Not String.IsNullOrWhiteSpace(order.ContractId), order.ContractId, order.Notes)

            Dim request = New PlaceOrderRequest With {
                .AccountId = order.AccountId,
                .ContractId = contractCode,
                .OrderType = MapOrderType(order.OrderType),
                .Side = CInt(order.Side),
                .Size = order.Quantity,
                .LimitPrice = If(order.LimitPrice.HasValue, CDbl(order.LimitPrice.Value), CType(Nothing, Double?)),
                .StopPrice = If(order.StopPrice.HasValue, CDbl(order.StopPrice.Value), CType(Nothing, Double?))
            }

            _logger.LogInformation("Placing {Type} {Side} x{Qty} on {Contract}",
                                   order.OrderType, order.Side, order.Quantity, contractCode)

            order.PlacedAt = DateTimeOffset.UtcNow
            order.Status = OrderStatus.Pending

            ' Persist before sending (audit trail; repo maps domain → entity internally)
            order.Id = Await _orderRepo.SaveOrderAsync(order)

            ' VB.NET cannot Await inside Catch — capture exception and await updates after Try/Catch
            Dim caughtEx As Exception = Nothing
            Try
                Dim response = Await _orderClient.PlaceOrderAsync(request)

                If response.Success Then
                    order.ExternalOrderId = response.OrderId
                    order.Status = OrderStatus.Working
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    _logger.LogInformation("Order accepted by exchange. ExternalId={Ext}", order.ExternalOrderId)
                Else
                    order.Status = OrderStatus.Rejected
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status)
                    _logger.LogWarning("Order rejected: {Msg}", response.ErrorMessage)
                    RaiseEvent OrderRejected(Me, New OrderRejectedEventArgs(order, If(response.ErrorMessage, "Unknown")))
                End If
            Catch ex As Exception
                caughtEx = ex
            End Try

            ' Handle exception after try-catch so Await is legal
            If caughtEx IsNot Nothing Then
                order.Status = OrderStatus.Rejected
                Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status)
                _logger.LogError(caughtEx, "Exception placing order")
                RaiseEvent OrderRejected(Me, New OrderRejectedEventArgs(order, caughtEx.Message))
            End If

            Return order
        End Function

        Public Async Function CancelOrderAsync(orderId As Long) As Task(Of Boolean) _
            Implements IOrderService.CancelOrderAsync
            _logger.LogInformation("Cancelling order {Id}", orderId)
            Dim response = Await _orderClient.CancelOrderAsync(orderId)
            If response.Success Then
                Await _orderRepo.UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled)
            Else
                _logger.LogWarning("Cancel failed for order {Id}: {Msg}", orderId, response.ErrorMessage)
            End If
            Return response.Success
        End Function

        Public Async Function CancelAllOpenOrdersAsync() As Task _
            Implements IOrderService.CancelAllOpenOrdersAsync
            ' Cancels all locally-known open orders; in production you'd also call a bulk cancel endpoint
            _logger.LogWarning("CancelAllOpenOrders called — cancelling all known open orders")
            Dim openOrders = Await _orderRepo.GetOpenOrdersAsync()
            Dim tasks = openOrders.
                Where(Function(o) o.ExternalOrderId.HasValue).
                Select(Function(o) CancelOrderAsync(o.ExternalOrderId.Value))
            Await Task.WhenAll(tasks)
        End Function

        Public Async Function GetOpenOrdersAsync(accountId As Long) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetOpenOrdersAsync
            ' Repository already returns domain Order objects
            Return Await _orderRepo.GetOpenOrdersAsync(accountId)
        End Function

        Public Async Function GetOrderHistoryAsync(accountId As Long,
                                                    from As DateTime,
                                                    [to] As DateTime) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetOrderHistoryAsync
            ' Repository already returns domain Order objects
            Return Await _orderRepo.GetOrderHistoryAsync(accountId, from, [to])
        End Function

        Public Async Function TryGetOrderFillPriceAsync(externalOrderId As Long,
                                                         accountId As Long,
                                                         Optional cancel As CancellationToken = Nothing) As Task(Of Decimal?) _
            Implements IOrderService.TryGetOrderFillPriceAsync
            Try
                Dim resp = Await _orderClient.SearchOrdersAsync(accountId, cancel:=cancel)
                If resp?.Success = True AndAlso resp.Orders IsNot Nothing Then
                    Dim match = resp.Orders.FirstOrDefault(Function(o) o.Id = externalOrderId)
                    If match IsNot Nothing AndAlso match.AvgFillPrice.HasValue Then
                        Return CDec(match.AvgFillPrice.Value)
                    End If
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "Could not retrieve fill price for order {Id}", externalOrderId)
            End Try
            Return Nothing
        End Function

        Public Async Function GetLiveWorkingOrdersAsync(accountId As Long,
                                                         contractId As String,
                                                         Optional cancel As CancellationToken = Nothing) As Task(Of IEnumerable(Of Order)) _
            Implements IOrderService.GetLiveWorkingOrdersAsync
            ' Compare against the raw API integer 1 (Working), NOT our local enum, because the
            ' local OrderStatus enum values diverge from the API status codes at value ≥ 3.
            Const ApiStatusWorking As Integer = 1
            Try
                Dim resp = Await _orderClient.SearchOrdersAsync(accountId, cancel:=cancel)
                If resp?.Success = True AndAlso resp.Orders IsNot Nothing Then
                    Dim working = resp.Orders _
                        .Where(Function(o) o.Status = ApiStatusWorking AndAlso
                                           String.Equals(o.ContractId, contractId,
                                                         StringComparison.OrdinalIgnoreCase)) _
                        .Select(Function(o) New Order With {
                            .ExternalOrderId = o.Id,
                            .AccountId = o.AccountId,
                            .ContractId = o.ContractId,
                            .Status = OrderStatus.Working
                        }).ToList()
                    _logger.LogDebug("GetLiveWorkingOrders: {Count} working order(s) for {Contract}",
                                     working.Count, contractId)
                    Return working
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "GetLiveWorkingOrdersAsync failed for {AccountId}/{ContractId}",
                                   accountId, contractId)
            End Try
            Return Enumerable.Empty(Of Order)()
        End Function

        ' ─── Mapping helpers ────────────────────────────────────────────────────

        Private Shared Function MapOrderType(ot As OrderType) As Integer
            Select Case ot
                Case OrderType.Limit : Return 1
                Case OrderType.Market : Return 2
                Case OrderType.StopOrder : Return 3
                Case OrderType.StopLimit : Return 4
                Case Else : Return 2
            End Select
        End Function

    End Class

End Namespace
