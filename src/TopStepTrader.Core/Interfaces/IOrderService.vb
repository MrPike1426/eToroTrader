Imports System.Threading
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Models

Namespace TopStepTrader.Core.Interfaces

    Public Interface IOrderService
        Event OrderFilled As EventHandler(Of OrderFilledEventArgs)
        Event OrderRejected As EventHandler(Of OrderRejectedEventArgs)
        ''' <summary>Fires when the net position changes (via UserHub SignalR stream).</summary>
        Event PositionUpdated As EventHandler(Of PositionUpdateEventArgs)

        Function PlaceOrderAsync(order As Order) As Task(Of Order)
        Function CancelOrderAsync(orderId As Long) As Task(Of Boolean)
        Function CancelAllOpenOrdersAsync() As Task
        Function GetOpenOrdersAsync(accountId As Long) As Task(Of IEnumerable(Of Order))
        Function GetOrderHistoryAsync(accountId As Long, from As DateTime, [to] As DateTime) As Task(Of IEnumerable(Of Order))
        ''' <summary>Returns the avg fill price for an already-placed order, or Nothing if not yet filled.</summary>
        Function TryGetOrderFillPriceAsync(externalOrderId As Long,
                                            accountId As Long,
                                            Optional cancel As CancellationToken = Nothing) As Task(Of Decimal?)

        ''' <summary>
        ''' Queries the eToro API directly for open positions on a specific instrument.
        ''' Does NOT use the local database — only the API has ground truth on live positions.
        ''' </summary>
        Function GetLiveWorkingOrdersAsync(accountId As Long,
                                           contractId As String,
                                           Optional cancel As CancellationToken = Nothing) As Task(Of IEnumerable(Of Order))
    End Interface

End Namespace
