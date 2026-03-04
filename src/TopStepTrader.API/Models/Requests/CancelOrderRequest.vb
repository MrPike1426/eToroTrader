Namespace TopStepTrader.API.Models.Requests

    ''' <summary>
    ''' eToro order cancellation is done via DELETE with the orderId in the URL path.
    ''' No request body is required. Kept as a stub for project file compatibility.
    ''' </summary>
    Public Class CancelOrderRequest
        Public Property OrderId As Long
    End Class

    ''' <summary>
    ''' eToro order/position search — queries the portfolio endpoint, no POST body needed.
    ''' </summary>
    Public Class SearchOrderRequest
        Public Property AccountId As Long
    End Class

End Namespace
