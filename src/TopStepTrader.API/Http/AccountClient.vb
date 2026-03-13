Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Retrieves demo account portfolio from the eToro API.
    ''' GET /api/v1/trading/info/demo/portfolio
    ''' </summary>
    Public Class AccountClient
        Inherits EToroHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       credentials As EToroCredentialsProvider,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of AccountClient))
            MyBase.New(httpClientFactory, credentials, rateLimiter, logger)
            _settings = options.Value
        End Sub

        ''' <summary>
        ''' Fetches the full demo portfolio including positions, open orders, and available credit.
        ''' </summary>
        Public Function GetPortfolioAsync(
            Optional cancel As CancellationToken = Nothing) As Task(Of PortfolioResponse)

            Dim endpoint = $"{_settings.BaseUrl}/api/v1/trading/info/demo/portfolio"
            Return GetAsync(Of PortfolioResponse)(endpoint, cancel)
        End Function

        ''' <summary>
        ''' Legacy helper — maps portfolio credit to the AccountSearchResponse shape
        ''' so existing AccountService code requires minimal changes.
        ''' </summary>
        Public Async Function SearchAccountsAsync(
            Optional cancel As CancellationToken = Nothing) As Task(Of AccountSearchResponse)

            Dim portfolio = Await GetPortfolioAsync(cancel)
            Dim totalInvestedPlusPnl As Decimal = 0D
            If portfolio?.ClientPortfolio?.Positions IsNot Nothing Then
                For Each pos In portfolio.ClientPortfolio.Positions
                    totalInvestedPlusPnl += CDec(pos.Amount) + CDec(pos.PnL)
                Next
            End If
            Dim credit = CDec(portfolio?.ClientPortfolio?.Credit)
            Dim dto = New AccountDto With {
                .Id = 1L,
                .Name = "eToro Demo Account",
                .Balance = credit,
                .TotalValue = credit + totalInvestedPlusPnl,
                .CanTrade = True,
                .IsVisible = True
            }
            Return New AccountSearchResponse With {
                .Success = True,
                .Accounts = New List(Of AccountDto) From {dto}
            }
        End Function

    End Class

End Namespace
