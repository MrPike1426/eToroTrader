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
Imports TopStepTrader.Core.Trading
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
                        .TakeProfitRate = If(order.TakeProfitRate.HasValue, CDbl(order.TakeProfitRate.Value), CType(Nothing, Double?)),
                        .IsTslEnabled = If(order.IsTslEnabled, CType(True, Boolean?), CType(Nothing, Boolean?))
                    }
                    response = Await _orderClient.PlaceOrderByAmountAsync(req)
                Else
                    ' Open by units/quantity
                    Dim req = New OpenMarketOrderByUnitsRequest With {
                        .InstrumentId = order.InstrumentId,
                        .IsBuy = (order.Side = OrderSide.Buy),
                        .Leverage = If(order.Leverage > 0, order.Leverage, 1),
                        .AmountInUnits = CDbl(order.Quantity),
                        .StopLossRate = If(order.StopLossRate.HasValue, CDbl(order.StopLossRate.Value), CType(Nothing, Double?)),
                        .TakeProfitRate = If(order.TakeProfitRate.HasValue, CDbl(order.TakeProfitRate.Value), CType(Nothing, Double?)),
                        .IsTslEnabled = If(order.IsTslEnabled, CType(True, Boolean?), CType(Nothing, Boolean?))
                    }
                    response = Await _orderClient.PlaceOrderByUnitsAsync(req)
                End If

                If response.Success Then
                    order.ExternalOrderId = response.OrderId
                    order.Status = OrderStatus.Working
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    _logger.LogInformation("eToro order accepted. orderId={Ext}", order.ExternalOrderId)
                    System.Console.Beep(880, 200)

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

                ' eToro sometimes returns errorCode > 0 as a soft-error even when the position was
                ' actually created (e.g. the requested SL was below the instrument minimum distance
                ' and eToro widened it silently).  Check for a real position FIRST — if one exists,
                ' treat the order as filled regardless of the error code so the engine does not lose
                ' track of a live open position.  Only reject when no position was created at all.
                Dim pos = info?.Positions?.FirstOrDefault()
                If pos IsNot Nothing Then
                    order.ExternalPositionId = pos.PositionId
                    order.FillPrice = CDec(pos.Rate)
                    order.FilledAt = DateTimeOffset.UtcNow
                    order.Status = OrderStatus.Filled
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    If info IsNot Nothing AndAlso info.ErrorCode > 0 Then
                        Dim softMsg = If(String.IsNullOrWhiteSpace(info.ErrorMessage),
                                         $"eToro errorCode={info.ErrorCode}",
                                         info.ErrorMessage)
                        _logger.LogWarning("eToro order {Id} had errorCode but position {PosId} was created — treating as filled. Soft-error: {Msg}",
                                           order.ExternalOrderId, pos.PositionId, softMsg)
                    Else
                        _logger.LogInformation("Position resolved: positionId={PosId} fillPrice={Price}",
                                               order.ExternalPositionId, order.FillPrice)
                    End If
                ElseIf info IsNot Nothing AndAlso info.ErrorCode > 0 Then
                    Dim msg = If(String.IsNullOrWhiteSpace(info.ErrorMessage),
                                 $"eToro errorCode={info.ErrorCode}",
                                 info.ErrorMessage)
                    _logger.LogWarning("eToro order {Id} rejected post-submission (no position created): [{Code}] {Msg}",
                                       order.ExternalOrderId, info.ErrorCode, msg)
                    order.Status = OrderStatus.Rejected
                    Await _orderRepo.UpdateOrderStatusAsync(order.Id, order.Status, order.ExternalOrderId)
                    RaiseEvent OrderRejected(Me, New OrderRejectedEventArgs(order, msg))
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
                ' Use the order-info endpoint: it returns the positions created by this specific
                ' order with the correct fill price ("rate" field).  Polling the portfolio instead
                ' would require matching positionId == orderId, which is never true.
                Dim info = Await _orderClient.GetOrderInfoAsync(externalOrderId, cancel)
                Dim pos = info?.Positions?.FirstOrDefault()
                If pos IsNot Nothing Then
                    Return CDec(pos.Rate)
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
                    ' eToro portfolio exposes p.InstrumentId.ToString() (e.g. "17") as o.ContractId.
                    ' Callers pass the ticker symbol (e.g. "OIL"), so resolve the numeric form too.
                    Dim numericInstrId = FavouriteContracts.TryGetBySymbol(contractId)?.InstrumentId.ToString()
                    Dim working = resp.Orders _
                        .Where(Function(o) o.Status = ApiStatusWorking AndAlso
                                           (String.Equals(o.ContractId, contractId,
                                                          StringComparison.OrdinalIgnoreCase) OrElse
                                            (numericInstrId IsNot Nothing AndAlso
                                             String.Equals(o.ContractId, numericInstrId)))) _
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

        Public Async Function GetLivePositionSnapshotAsync(accountId As Long,
                                                             contractId As String,
                                                             Optional positionId As Long? = Nothing,
                                                             Optional cancel As CancellationToken = Nothing) As Task(Of LivePositionSnapshot) _
            Implements IOrderService.GetLivePositionSnapshotAsync
            Try
                Dim positions = Await _orderClient.GetPortfolioPositionsAsync(cancel)
                Dim numericInstrId = FavouriteContracts.TryGetBySymbol(contractId)?.InstrumentId

                ' When a specific positionId is requested return only that position.
                ' Otherwise aggregate ALL open positions for the instrument so the engine receives
                ' combined units and a weighted-average open rate — essential for accurate multi-
                ' position P&L.  Note: the eToro portfolio API does not include a pnL field;
                ' P&L is calculated by the engine using currentPrice from the latest bar.
                Dim matchList As List(Of EToroPositionDto)
                If positionId.HasValue Then
                    Dim exactMatch = positions.FirstOrDefault(Function(p) p.PositionId = positionId.Value)
                    matchList = If(exactMatch IsNot Nothing,
                                   New List(Of EToroPositionDto) From {exactMatch},
                                   New List(Of EToroPositionDto)())
                ElseIf numericInstrId.HasValue Then
                    matchList = positions.Where(Function(p) p.InstrumentId = numericInstrId.Value).ToList()
                Else
                    matchList = New List(Of EToroPositionDto)()
                End If
                If matchList.Count = 0 Then Return Nothing

                ' Use the first (oldest) position as the representative for PositionId/IsBuy/OpenedAt.
                ' Aggregate units and amount; weighted-average open rate ensures:
                '   P&L = (currentPrice − weightedOpenRate) × totalUnits  gives the correct total.
                Dim rep = matchList.First()
                Dim totalUnits As Double = matchList.Sum(Function(p) p.Units)
                Dim totalAmount As Double = matchList.Sum(Function(p) p.Amount)
                Dim weightedOpenRate As Double = If(totalUnits > 0.0,
                    matchList.Sum(Function(p) p.OpenRate * p.Units) / totalUnits,
                    rep.OpenRate)

                Dim openedAt = DateTimeOffset.MinValue
                Dim parsedDt As DateTimeOffset
                If Not String.IsNullOrEmpty(rep.OpenDateTime) AndAlso
                   DateTimeOffset.TryParse(rep.OpenDateTime, parsedDt) Then
                    openedAt = parsedDt.ToUniversalTime()
                End If

                Return New LivePositionSnapshot With {
                    .PositionId = rep.PositionId,
                    .UnrealizedPnlUsd = CDec(matchList.Sum(Function(p) p.PnL)),
                    .OpenedAtUtc = openedAt,
                    .IsBuy = rep.IsBuy,
                    .Amount = CDec(totalAmount),
                    .OpenRate = CDec(weightedOpenRate),
                    .Units = CDec(totalUnits),
                    .Leverage = CInt(rep.Leverage),
                    .PositionCount = matchList.Count
                }
            Catch ex As Exception
                _logger.LogWarning(ex, "GetLivePositionSnapshotAsync failed for {Contract}", contractId)
                Return Nothing
            End Try
        End Function

        Public Async Function FlattenContractAsync(accountId As Long,
                                                    contractId As String,
                                                    Optional cancel As CancellationToken = Nothing) As Task(Of Boolean) _
            Implements IOrderService.FlattenContractAsync
            Dim liveOrders = Await GetLiveWorkingOrdersAsync(accountId, contractId, cancel)
            If Not liveOrders.Any() Then Return True

            Dim allOk = True
            For Each order In liveOrders
                ' GetLiveWorkingOrdersAsync sets ExternalPositionId = ExternalOrderId = portfolio positionId
                Dim posId = If(order.ExternalPositionId, order.ExternalOrderId)
                If Not posId.HasValue Then Continue For
                Try
                    Dim resp = Await _orderClient.ClosePositionAsync(posId.Value, Nothing, cancel)
                    If resp.Success Then
                        _logger.LogInformation("FlattenContract: closed positionId={PosId} ({Contract})",
                                               posId.Value, contractId)
                    Else
                        _logger.LogWarning("FlattenContract: close failed positionId={PosId}: {Msg}",
                                           posId.Value, resp.ErrorMessage)
                        allOk = False
                    End If
                Catch ex As Exception
                    _logger.LogWarning(ex, "FlattenContract exception positionId={PosId}", posId)
                    allOk = False
                End Try
            Next
            Return allOk
        End Function

        Public Async Function EditPositionSlTpAsync(positionId As Long,
                                                     slRate As Decimal?,
                                                     tpRate As Decimal?,
                                                     Optional enableTsl As Boolean = False,
                                                     Optional cancel As CancellationToken = Nothing) As Task(Of Boolean) _
            Implements IOrderService.EditPositionSlTpAsync
            Try
                Dim req = New EditPositionRequest With {
                    .StopLossRate = If(slRate.HasValue, CDbl(slRate.Value), CType(Nothing, Double?)),
                    .TakeProfitRate = If(tpRate.HasValue, CDbl(tpRate.Value), CType(Nothing, Double?)),
                    .IsTslEnabled = If(enableTsl, CType(True, Boolean?), CType(Nothing, Boolean?))
                }
                Dim resp = Await _orderClient.EditPositionAsync(positionId, req, cancel)
                Dim tslStr = If(enableTsl, " TSL=on", String.Empty)
                If resp.Success Then
                    _logger.LogInformation("EditPositionSlTp: updated positionId={PosId} SL={SL} TP={TP}{Tsl}",
                                           positionId,
                                           If(slRate.HasValue, slRate.Value.ToString("F4"), "none"),
                                           If(tpRate.HasValue, tpRate.Value.ToString("F4"), "none"),
                                           tslStr)
                Else
                    _logger.LogWarning("EditPositionSlTp: API rejected update for positionId={PosId}{Tsl}: {Msg}",
                                       positionId, tslStr, resp.ErrorMessage)
                End If
                Return resp.Success
            Catch ex As Exception
                _logger.LogWarning(ex, "EditPositionSlTpAsync failed for positionId={PosId}", positionId)
                Return False
            End Try
        End Function

    End Class

End Namespace
