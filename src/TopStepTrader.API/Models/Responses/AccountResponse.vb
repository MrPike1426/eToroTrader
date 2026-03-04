Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Responses

    ''' <summary>
    ''' Response from GET /api/v1/trading/info/demo/portfolio
    ''' Contains the full demo account portfolio including positions, orders, and available balance.
    ''' </summary>
    Public Class PortfolioResponse
        <JsonPropertyName("clientPortfolio")>
        Public Property ClientPortfolio As ClientPortfolioDto
    End Class

    Public Class ClientPortfolioDto
        ''' <summary>Available trading balance in USD (funds available for new positions).</summary>
        <JsonPropertyName("credit")>
        Public Property Credit As Double

        ''' <summary>Bonus credit in USD.</summary>
        <JsonPropertyName("bonusCredit")>
        Public Property BonusCredit As Double

        <JsonPropertyName("positions")>
        Public Property Positions As List(Of EToroPositionDto) = New List(Of EToroPositionDto)()

        <JsonPropertyName("ordersForOpen")>
        Public Property OrdersForOpen As List(Of EToroOrderForOpenDto) = New List(Of EToroOrderForOpenDto)()

        <JsonPropertyName("ordersForClose")>
        Public Property OrdersForClose As List(Of Object) = New List(Of Object)()
    End Class

    Public Class EToroPositionDto
        <JsonPropertyName("positionId")>
        Public Property PositionId As Long

        <JsonPropertyName("instrumentId")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("isBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("openRate")>
        Public Property OpenRate As Double

        <JsonPropertyName("amount")>
        Public Property Amount As Double

        <JsonPropertyName("units")>
        Public Property Units As Double

        <JsonPropertyName("leverage")>
        Public Property Leverage As Double

        <JsonPropertyName("stopLossRate")>
        Public Property StopLossRate As Double

        <JsonPropertyName("takeProfitRate")>
        Public Property TakeProfitRate As Double

        <JsonPropertyName("isTslEnabled")>
        Public Property IsTslEnabled As Boolean

        <JsonPropertyName("openDateTime")>
        Public Property OpenDateTime As String = String.Empty

        <JsonPropertyName("orderId")>
        Public Property OrderId As Long

        <JsonPropertyName("pnL")>
        Public Property PnL As Double
    End Class

    Public Class EToroOrderForOpenDto
        <JsonPropertyName("orderId")>
        Public Property OrderId As Long

        <JsonPropertyName("instrumentId")>
        Public Property InstrumentId As Integer

        <JsonPropertyName("isBuy")>
        Public Property IsBuy As Boolean

        <JsonPropertyName("amount")>
        Public Property Amount As Double

        <JsonPropertyName("units")>
        Public Property Units As Double

        <JsonPropertyName("stopLossRate")>
        Public Property StopLossRate As Double

        <JsonPropertyName("takeProfitRate")>
        Public Property TakeProfitRate As Double

        <JsonPropertyName("openDateTime")>
        Public Property OpenDateTime As String = String.Empty
    End Class

    ' ── Legacy alias kept so existing AccountService code compiles with minimal changes ──

    Public Class AccountSearchResponse
        Public Property Success As Boolean = True
        Public Property Accounts As List(Of AccountDto) = New List(Of AccountDto)()
    End Class

    Public Class AccountDto
        Public Property Id As Long
        Public Property Name As String = String.Empty
        Public Property Balance As Decimal
        Public Property CanTrade As Boolean = True
        Public Property IsVisible As Boolean = True
        Public Property StartingBalance As Decimal
    End Class

End Namespace
